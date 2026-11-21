param(
    [switch]$RefreshTimestamps
)

$ErrorActionPreference = "Stop"
$seedDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($RefreshTimestamps) {
    Write-Host "Tarihler guncel zamana gore yenileniyor..." -ForegroundColor Cyan
    python "$seedDir\generate_seed_db.py"
}

# Sabit eslesme (Debug APK'daki gomulu seed ile ayni): emulator-5554 = Selin, digerleri (emulator-5556, telefon) = Mert.
$serials = adb devices | Select-String "^(\S+)\s+device$" | ForEach-Object { $_.Matches[0].Groups[1].Value }
if (-not $serials) { Write-Warning "Bagli cihaz yok."; return }

foreach ($emu in $serials) {
    $seedUser = if ($emu -eq "emulator-5554") { "selin" } else { "mert" }
    $dbPath = "$seedDir\PocketLocal_$seedUser.db3"

    adb -s $emu shell pm path com.yunuscakir.pocket.client.debug | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "$emu cihazinda Debug uygulamasi kurulu degil, atlaniyor."
        continue
    }

    Write-Host "=== $emu cihazina '$seedUser' ornek fotograflari ve veritabani yukleniyor ===" -ForegroundColor Green

    adb -s $emu shell am force-stop com.yunuscakir.pocket.client.debug

    adb -s $emu shell "rm -rf /data/local/tmp/seed_media /data/local/tmp/seed_profile; mkdir -p /data/local/tmp/seed_media /data/local/tmp/seed_profile"
    adb -s $emu push "$seedDir\Media\." /data/local/tmp/seed_media/ | Out-Null
    adb -s $emu push "$seedDir\Profile\." /data/local/tmp/seed_profile/ | Out-Null
    adb -s $emu shell "chmod -R 777 /data/local/tmp/seed_media /data/local/tmp/seed_profile"

    adb -s $emu shell "run-as com.yunuscakir.pocket.client.debug mkdir -p files/Media files/Profile"
    adb -s $emu shell "run-as com.yunuscakir.pocket.client.debug sh -c 'cp /data/local/tmp/seed_media/*.jpg files/Media/ && cp /data/local/tmp/seed_profile/*.jpg files/Profile/'"

    # Ornek fotograflari galeriye yalnizca emulatorlerde kopyala (telefonun gercek galerisini kirletme).
    if ($emu -like "emulator-*") {
        adb -s $emu shell "mkdir -p /sdcard/DCIM/Camera && cp /data/local/tmp/seed_media/*.jpg /sdcard/DCIM/Camera/"
        adb -s $emu shell "content call --uri content://media/none/media_scanner --method scan_volume --arg external_primary" | Out-Null
    }

    adb -s $emu push $dbPath /data/local/tmp/new.db3 | Out-Null
    adb -s $emu shell "chmod 777 /data/local/tmp/new.db3; run-as com.yunuscakir.pocket.client.debug rm -f files/PocketLocal.db3-wal files/PocketLocal.db3-shm; run-as com.yunuscakir.pocket.client.debug cp /data/local/tmp/new.db3 files/PocketLocal.db3"

    adb -s $emu shell am start -n com.yunuscakir.pocket.client.debug/crc646dd0ab311caf03fd.MainActivity | Out-Null
    Write-Host "$emu basariyla guncellendi ve uygulama baslatildi." -ForegroundColor Cyan
}
