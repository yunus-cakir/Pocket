<#
.SYNOPSIS
    Exposes the local PocketServer (127.0.0.1:5200) to the internet via Tailscale Funnel.
.DESCRIPTION
    Free alternative to Cloudflare Tunnel: gives a stable public HTTPS URL
    (https://<machine>.<tailnet>.ts.net) without a domain or port forwarding.
    The funnel config is persistent (--bg) and survives reboots because the
    Tailscale Windows service runs it. Safe to re-run.
    Prerequisites: Tailscale installed and logged in (`tailscale up --unattended`),
    HTTPS + Funnel enabled for the tailnet (the CLI prints a link the first time).
#>
param(
    [int]$Port = 5200,
    [switch]$Off
)

$ErrorActionPreference = 'Stop'

$ts = (Get-Command tailscale -ErrorAction SilentlyContinue).Source
if (-not $ts) {
    foreach ($p in @("$env:ProgramFiles\Tailscale\tailscale.exe", "C:\Library\Apps\Tailscale\tailscale.exe")) {
        if (Test-Path $p) { $ts = $p; break }
    }
}
if (-not $ts) { throw "tailscale.exe not found. Install Tailscale first." }

if ($Off) {
    & $ts funnel reset
    Write-Host "Funnel disabled." -ForegroundColor Green
    return
}

$state = (& $ts status --json | ConvertFrom-Json)
if ($state.BackendState -ne 'Running') {
    throw "Tailscale is not logged in (state: $($state.BackendState)). Run: tailscale up --unattended"
}

Write-Host "==> Enabling funnel on port $Port" -ForegroundColor Cyan
& $ts funnel --bg --yes $Port
if ($LASTEXITCODE -ne 0) {
    throw "tailscale funnel failed. If it printed a link, open it to enable HTTPS/Funnel for your tailnet, then re-run."
}

$dns  = $state.Self.DNSName.TrimEnd('.')
$url  = "https://$dns"
Write-Host ""
& $ts funnel status
Write-Host ""
Write-Host "Public URL: $url" -ForegroundColor Green
Write-Host "In the app: Settings > Server > $url"

try {
    $resp = Invoke-RestMethod -Uri "$url/" -TimeoutSec 15
    Write-Host "Health check OK -> $resp" -ForegroundColor Green
} catch {
    Write-Warning "Health check via $url failed (DNS/cert can take a minute on first enable, or PocketServer is not running): $_"
}
