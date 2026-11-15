param(
    [switch]$RefreshTimestamps
)

$ErrorActionPreference = "Stop"
$seedDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($RefreshTimestamps) {
    Write-Host "Tarihler guncel zamana gore yenileniyor..." -ForegroundColor Cyan
    python "$seedDir\generate_seed_db.py"
}

$targets = @(
    @{ Serial = "emulator-5554"; DbFile = "$seedDir\PocketLocal_5554.db3" },
    @{ Serial = "emulator-5556"; DbFile = "$seedDir\PocketLocal_5556.db3" }
)

$connectedDevices = (adb devices) -join "`n"

foreach ($t in $targets) {
    $emu = $t.Serial
    $dbPath = $t.DbFile

    if ($connectedDevices -notmatch "$emu\s+device") {
        Write-Warning "$emu bagli degil, atlaniyor."
        continue
    }

    Write-Host "=== $emu cihazina ornek fotograflar ve veritabani yukleniyor ===" -ForegroundColor Green

    adb -s $emu shell am force-stop com.companyname.pocket.client.debug

    adb -s $emu shell "rm -rf /data/local/tmp/seed_media /data/local/tmp/seed_profile; mkdir -p /data/local/tmp/seed_media /data/local/tmp/seed_profile"
    adb -s $emu push "$seedDir\Media\." /data/local/tmp/seed_media/ | Out-Null
    adb -s $emu push "$seedDir\Profile\." /data/local/tmp/seed_profile/ | Out-Null
    adb -s $emu shell "chmod -R 777 /data/local/tmp/seed_media /data/local/tmp/seed_profile"

    adb -s $emu shell "run-as com.companyname.pocket.client.debug mkdir -p files/Media files/Profile"
    adb -s $emu shell "run-as com.companyname.pocket.client.debug sh -c 'cp /data/local/tmp/seed_media/*.jpg files/Media/ && cp /data/local/tmp/seed_profile/*.jpg files/Profile/'"

    adb -s $emu shell "mkdir -p /sdcard/DCIM/Camera && cp /data/local/tmp/seed_media/*.jpg /sdcard/DCIM/Camera/"
    adb -s $emu shell "content call --uri content://media/none/media_scanner --method scan_volume --arg external_primary" | Out-Null

    adb -s $emu push $dbPath /data/local/tmp/new.db3 | Out-Null
    adb -s $emu shell "chmod 777 /data/local/tmp/new.db3; run-as com.companyname.pocket.client.debug rm -f files/PocketLocal.db3-wal files/PocketLocal.db3-shm; run-as com.companyname.pocket.client.debug cp /data/local/tmp/new.db3 files/PocketLocal.db3"

    adb -s $emu shell am start -n com.companyname.pocket.client.debug/crc646dd0ab311caf03fd.MainActivity | Out-Null
    Write-Host "$emu basariyla guncellendi ve uygulama baslatildi." -ForegroundColor Cyan
}
