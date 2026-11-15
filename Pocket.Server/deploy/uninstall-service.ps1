#Requires -RunAsAdministrator
<# Stops and removes the PocketServer Windows Service. Installed files are left in place. #>
param([string]$ServiceName = "PocketServer")

$ErrorActionPreference = 'Stop'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "Service '$ServiceName' is not installed."; return }

if ($svc.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force
    $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
sc.exe delete $ServiceName | Out-Null
Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
