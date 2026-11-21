# Emulatorleri sabit portlarda baslatir. Debug seed eslemesi ADB serial'a (porta) bagli oldugu icin
# acilis sirasindan bagimsiz olarak her AVD hep ayni serial'i almali:
#   Latest (API 35) -> emulator-5554 (Selin)
#   Zebra  (API 34) -> emulator-5556 (Mert)
#   Note9  (API 29) -> emulator-5558 (Selin)
# Kullanim: .\Start-Emulators.ps1            (hepsi)
#           .\Start-Emulators.ps1 Note9 Zebra (secilenler)
param([string[]]$Names = @("Latest", "Zebra", "Note9"))

$ports = [ordered]@{ Latest = 5554; Zebra = 5556; Note9 = 5558 }

$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { "C:\Program Files (x86)\Android\android-sdk" }
$emulator = Join-Path $sdk "emulator\emulator.exe"
$adb = Join-Path $sdk "platform-tools\adb.exe"
$running = & $adb devices | Select-String "^(emulator-\d+)\s" | ForEach-Object { $_.Matches[0].Groups[1].Value }

foreach ($name in $Names) {
    if (-not $ports.Contains($name)) { Write-Warning "Bilinmeyen AVD: $name (gecerli: $($ports.Keys -join ', '))"; continue }
    $serial = "emulator-$($ports[$name])"
    if ($running -contains $serial) {
        $avd = (& $adb -s $serial emu avd name | Select-Object -First 1).Trim()
        if ($avd -eq $name) { Write-Host "$name zaten $serial uzerinde calisiyor." -ForegroundColor Yellow }
        else { Write-Warning "$serial portunu '$avd' kullaniyor; $name baslatilmadi. Once onu kapatin." }
        continue
    }
    Write-Host "$name -> $serial baslatiliyor..." -ForegroundColor Green
    Start-Process $emulator -ArgumentList "-avd", $name, "-port", $ports[$name]
}
