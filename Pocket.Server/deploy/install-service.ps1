#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publishes Pocket.Server, installs/updates it as a Windows Service and installs the two-way
    watchdog (scheduled task "PocketServerWatchdog") that decides when the service runs.
.DESCRIPTION
    Safe to re-run: stops the watchdog and the service, copies the new build over it and starts
    the watchdog again.
    -Role standby (default; the Mac is the primary): the watchdog starts the service only while the
      Mac is unreachable or draining, and stops it (handing off to the Mac) once the Mac is back.
    -Role primary: the watchdog keeps the service running.
    By default the service listens on loopback only (127.0.0.1:5200), so from outside it can be
    reached only through Tailscale Funnel / Cloudflare Tunnel. Pass -Urls "http://0.0.0.0:5200"
    to also allow LAN access.
#>
param(
    [string]$InstallDir  = "C:\Services\PocketServer",
    [string]$Urls        = "http://127.0.0.1:5200",
    [string]$ServiceName = "PocketServer",
    # This node's role in the two-node cluster (see README.md).
    [ValidateSet('primary', 'standby')]
    [string]$Role        = "standby",
    # The other node: watched by the watchdog, handoff target and identity-registry sync partner.
    [string]$PeerUrl     = "https://yunuscakir-mac.tail4d1102.ts.net",
    # Update the build/config but leave the watchdog and the service stopped.
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$project = (Resolve-Path (Join-Path $PSScriptRoot '..\Pocket.Server.csproj')).Path
$staging = Join-Path $env:TEMP 'PocketServer-publish'

Write-Host "==> Publishing $project" -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained false -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$taskName = "${ServiceName}Watchdog"
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Write-Host "==> Stopping watchdog task $taskName" -ForegroundColor Cyan
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
    # A running node drains, waits for the peer to take over and hands off undelivered messages.
    Write-Host "==> Stopping existing service (hands off to the peer, can take up to ~3 min)" -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -NoWait
    try { $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(210)) } catch {
        Write-Warning "Service did not stop in time; killing it."
        $procId = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").ProcessId
        if ($procId) { Stop-Process -Id $procId -Force }
        Start-Sleep -Seconds 2
    }
}

Write-Host "==> Copying build to $InstallDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $InstallDir -Recurse -Force

# Service runs in the Production environment; this file sets its listen address and its
# cluster identity (primary). The shared secret is generated once and kept across re-installs;
# the Mac standby needs the same value (deploy/mac/install.sh --secret ...).
$prodSettings = Join-Path $InstallDir 'appsettings.Production.json'
$secret = $null
if (Test-Path $prodSettings) {
    try { $secret = (Get-Content $prodSettings -Raw | ConvertFrom-Json).Cluster.Secret } catch { }
}
if ([string]::IsNullOrWhiteSpace($secret)) {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $secret = -join ($bytes | ForEach-Object { $_.ToString('x2') })
}
@{
    Urls    = $Urls
    Cluster = @{ NodeName = 'pc'; Role = $Role; Secret = $secret; PeerUrl = $PeerUrl; HandoffUrl = $PeerUrl }
} | ConvertTo-Json -Depth 3 | Out-File -FilePath $prodSettings -Encoding utf8

$exe = Join-Path $InstallDir 'Pocket.Server.exe'
if (-not $svc) {
    Write-Host "==> Creating service $ServiceName" -ForegroundColor Cyan
    New-Service -Name $ServiceName `
        -BinaryPathName "`"$exe`"" `
        -DisplayName 'Pocket Relay Server' `
        -Description 'Pocket transient E2EE relay (SignalR). Started/stopped by the PocketServerWatchdog task.' `
        -StartupType Manual | Out-Null
}
# The watchdog decides when the service runs; it must not start on its own at boot.
Set-Service -Name $ServiceName -StartupType Manual

$logs = Join-Path $InstallDir 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'windows\watchdog.ps1') -Destination (Join-Path $InstallDir 'watchdog.ps1') -Force

Write-Host "==> Registering watchdog task $taskName ($Role, peer $PeerUrl)" -ForegroundColor Cyan
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'watchdog.ps1')`"" `
    -WorkingDirectory $InstallDir
$trigger   = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'Pocket two-way watchdog: starts/stops PocketServer depending on the peer node.' `
    -Force | Out-Null

# Restart automatically on crash: 5s, 5s, then 30s; reset failure counter daily.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

if ($NoStart) {
    Write-Host "==> Installed; watchdog and service left stopped (-NoStart). Start with: Start-ScheduledTask $taskName" -ForegroundColor Yellow
    return
}

Write-Host "==> Starting watchdog" -ForegroundColor Cyan
Start-ScheduledTask -TaskName $taskName

if ($Role -eq 'standby') {
    Write-Host ""
    Write-Host "Standby: the watchdog starts $ServiceName only while $PeerUrl is down." -ForegroundColor Green
    Write-Host "Log: $(Join-Path $logs 'watchdog.log')"
    Write-Host ""
    Write-Host "Cluster secret (for the Mac: ./install.sh --secret ...):" -ForegroundColor Cyan
    Write-Host "  $secret"
    return
}

$healthUrl = (($Urls -split ';')[0] -replace '0\.0\.0\.0|\+|\*', '127.0.0.1').TrimEnd('/') + '/'
$ok = $false
for ($i = 0; $i -lt 15 -and -not $ok; $i++) {
    Start-Sleep -Seconds 1
    try { $resp = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 3; $ok = $true } catch { }
}

if ($ok) {
    Write-Host "OK  $healthUrl -> $resp" -ForegroundColor Green
    Write-Host ""
    Write-Host "Cluster secret (for the Mac standby: ./install.sh --secret ...):" -ForegroundColor Cyan
    Write-Host "  $secret"
} else {
    Write-Warning "Service started but $healthUrl did not answer. Check Event Viewer > Windows Logs > Application (source: PocketServer / .NET Runtime)."
}
