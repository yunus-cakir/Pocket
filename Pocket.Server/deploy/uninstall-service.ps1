#Requires -RunAsAdministrator
<# Removes the watchdog task, stops and removes the PocketServer Windows Service. Installed files are left in place. #>
param([string]$ServiceName = "PocketServer")

$ErrorActionPreference = 'Stop'

$taskName = "${ServiceName}Watchdog"
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Watchdog task '$taskName' removed." -ForegroundColor Green
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "Service '$ServiceName' is not installed."; return }

if ($svc.Status -ne 'Stopped') {
    # A running node drains, waits for the peer and hands off undelivered messages first.
    Stop-Service -Name $ServiceName -Force -NoWait
    $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(210))
}
sc.exe delete $ServiceName | Out-Null
Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
