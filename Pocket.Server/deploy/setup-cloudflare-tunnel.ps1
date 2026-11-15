#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs cloudflared and registers it as a Windows Service using a dashboard-managed tunnel token.
.DESCRIPTION
    Create the tunnel and its public hostname in the Cloudflare dashboard first
    (Zero Trust > Networks > Tunnels), then pass the token shown there:
        .\setup-cloudflare-tunnel.ps1 -Token "eyJh..."
    Re-running replaces the existing cloudflared service (e.g. after rotating the token).
#>
param(
    [Parameter(Mandatory = $true)][string]$Token
)

$ErrorActionPreference = 'Stop'

function Find-Cloudflared {
    $cmd = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @("${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe",
                     "$env:ProgramFiles\cloudflared\cloudflared.exe")) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

$cf = Find-Cloudflared
if (-not $cf) {
    Write-Host "==> Installing cloudflared via winget" -ForegroundColor Cyan
    winget install --id Cloudflare.cloudflared -e --accept-source-agreements --accept-package-agreements
    $cf = Find-Cloudflared
    if (-not $cf) { throw "cloudflared installed but not found. Open a new terminal and re-run this script." }
}
Write-Host "cloudflared: $cf"
& $cf --version

if (Get-Service -Name 'cloudflared' -ErrorAction SilentlyContinue) {
    Write-Host "==> Removing existing cloudflared service" -ForegroundColor Cyan
    & $cf service uninstall
    Start-Sleep -Seconds 2
}

Write-Host "==> Installing cloudflared service" -ForegroundColor Cyan
& $cf service install $Token
if ($LASTEXITCODE -ne 0) { throw "cloudflared service install failed (exit $LASTEXITCODE)." }

Start-Sleep -Seconds 3
$svc = Get-Service -Name 'cloudflared'
Write-Host "cloudflared service: $($svc.Status) / $($svc.StartType)" -ForegroundColor Green
Write-Host "Check the dashboard: the tunnel should show as HEALTHY within a few seconds."
