#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publishes Pocket.Server and installs/updates it as an auto-starting Windows Service.
.DESCRIPTION
    Safe to re-run: stops the existing service, copies the new build over it and starts it again.
    By default the service listens on loopback only (127.0.0.1:5200), so from outside it can be
    reached only through Cloudflare Tunnel. Pass -Urls "http://0.0.0.0:5200" to also allow LAN access.
#>
param(
    [string]$InstallDir  = "C:\Services\PocketServer",
    [string]$Urls        = "http://127.0.0.1:5200",
    [string]$ServiceName = "PocketServer"
)

$ErrorActionPreference = 'Stop'

$project = (Resolve-Path (Join-Path $PSScriptRoot '..\Pocket.Server.csproj')).Path
$staging = Join-Path $env:TEMP 'PocketServer-publish'

Write-Host "==> Publishing $project" -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained false -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
    Write-Host "==> Stopping existing service" -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force
    $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
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
    Cluster = @{ NodeName = 'pc'; Role = 'primary'; Secret = $secret }
} | ConvertTo-Json -Depth 3 | Out-File -FilePath $prodSettings -Encoding utf8

$exe = Join-Path $InstallDir 'Pocket.Server.exe'
if (-not $svc) {
    Write-Host "==> Creating service $ServiceName" -ForegroundColor Cyan
    New-Service -Name $ServiceName `
        -BinaryPathName "`"$exe`"" `
        -DisplayName 'Pocket Relay Server' `
        -Description 'Pocket transient E2EE relay (SignalR). Exposed via Cloudflare Tunnel.' `
        -StartupType Automatic | Out-Null
}

# Restart automatically on crash: 5s, 5s, then 30s; reset failure counter daily.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

Write-Host "==> Starting service" -ForegroundColor Cyan
Start-Service -Name $ServiceName

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
