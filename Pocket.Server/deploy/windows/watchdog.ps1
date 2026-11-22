<#
.SYNOPSIS
    Pocket two-way watchdog (Windows). Runs as the SYSTEM scheduled task "PocketServerWatchdog"
    (installed by ..\install-service.ps1) and controls the PocketServer service.
.DESCRIPTION
    Same logic as deploy/mac/watchdog.sh; each node watches the other.

    Role "standby" (default for the PC; read from Cluster.Role in appsettings.Production.json):
      - peer healthy                 -> PocketServer stays stopped
      - peer draining (503)          -> start PocketServer now (the peer is handing off to us)
      - peer down N checks in a row  -> start PocketServer
      - peer back M checks in a row  -> stop PocketServer: it refuses new connections, waits for
        the peer, disconnects clients and hands off undelivered messages (memory to memory).
      - while PocketServer is stopped, the identity registry is synced with the peer every few checks.

    Role "primary":
      - keeps PocketServer running (starts it if it is stopped). On a planned stop the server
        drains (/health 503), the standby's watchdog starts the standby at once and the server
        hands its undelivered messages over to it.
#>
param(
    [string]$InstallDir    = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$ServiceName   = 'PocketServer',
    [int]$CheckInterval    = 15,   # seconds between checks
    [int]$DownThreshold    = 3,    # failed checks before taking over (~45s)
    [int]$UpThreshold      = 2,    # healthy checks before handing back (~30s)
    [int]$SyncEvery        = 4,    # healthy checks between registry syncs (~60s)
    [int]$StopTimeout      = 210,  # server may wait 90s for the peer, then hand off large media
    [switch]$CheckOnly             # print the peer state as this watchdog sees it, then exit
)

$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$logDir   = Join-Path $InstallDir 'logs'
$logFile  = Join-Path $logDir 'watchdog.log'
$registry = Join-Path $InstallDir 'data\identities.json'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$config = $null
try { $config = (Get-Content (Join-Path $InstallDir 'appsettings.Production.json') -Raw | ConvertFrom-Json).Cluster } catch { }
$role    = if ($config -and $config.Role) { "$($config.Role)".ToLowerInvariant() } else { 'standby' }
$peerUrl = if ($config -and $config.PeerUrl) { "$($config.PeerUrl)" } elseif ($config -and $config.HandoffUrl) { "$($config.HandoffUrl)" } else { '' }
$peerUrl = $peerUrl.TrimEnd('/')
$secret  = if ($config) { "$($config.Secret)" } else { '' }

function Write-Log([string]$message) {
    try {
        if ((Test-Path $logFile) -and (Get-Item $logFile).Length -gt 5MB) {
            Move-Item $logFile "$logFile.1" -Force
        }
        Add-Content -Path $logFile -Value ("{0} [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $role, $message) -Encoding UTF8
    } catch { }
}

# Clients reach the peer through its public URL (Tailscale Funnel), but on this machine the
# *.ts.net name resolves through MagicDNS to the peer's tailnet address. Those paths fail
# separately (e.g. a VPN on the peer breaks Funnel while the tailnet link stays up), so the peer
# is checked the way clients see it: the host is resolved with public DNS (DNS-over-HTTPS) and
# curl is pinned to those addresses. When public DNS can't be reached, the plain URL is used.
$curl = Join-Path $env:SystemRoot 'System32\curl.exe'
$peerUri = $null
try { $peerUri = [Uri]$peerUrl } catch { }
$publicCheck = $peerUri -and $peerUri.Scheme -eq 'https' -and $peerUri.HostNameType -eq 'Dns' -and (Test-Path $curl)
$publicIps = @(); $publicIpsAt = [DateTime]::MinValue

function Resolve-PublicIps {
    if (((Get-Date) - $script:publicIpsAt).TotalMinutes -lt 5 -and $script:publicIps.Count -gt 0) { return $script:publicIps }
    try {
        $r = Invoke-RestMethod -Uri "https://dns.google/resolve?name=$($peerUri.Host)&type=A" -TimeoutSec 8
        $ips = @($r.Answer | Where-Object { $_.type -eq 1 } | ForEach-Object { "$($_.data)" })
        if ($ips.Count -gt 0) { $script:publicIps = $ips; $script:publicIpsAt = Get-Date }
    } catch { }
    return $script:publicIps
}

# HTTP status of GET /health ("000" = no answer). $ips pins the host to those addresses.
function Get-HealthCode([string[]]$ips) {
    $curlArgs = @('-s', '-o', 'NUL', '-w', '%{http_code}', '-m', '8')
    if ($ips) { $curlArgs += @('--resolve', "$($peerUri.Host):$($peerUri.Port):$($ips -join ',')") }
    $code = & $curl @curlArgs "$peerUrl/health" 2>$null
    if ($code) { return "$code".Trim() } else { return '000' }
}

# "up", "draining" (the peer is shutting down and handing off), "down", or "down-public"
# (answers on the tailnet but not on its public URL, so clients can't reach it).
function Get-PeerState {
    if (-not $peerUrl) { return 'down' }
    if ($publicCheck) {
        $ips = Resolve-PublicIps
        if ($ips.Count -gt 0) {
            $code = Get-HealthCode $ips
            if ($code -eq '200') { return 'up' }
            if ($code -eq '503') { return 'draining' }
            $script:publicIpsAt = [DateTime]::MinValue   # re-resolve next time
            if ((Get-HealthCode $null) -eq '200') { return 'down-public' }
            return 'down'
        }
    }
    try {
        $r = Invoke-WebRequest -Uri "$peerUrl/health" -UseBasicParsing -TimeoutSec 8
        if ($r.StatusCode -eq 200) { return 'up' }
        return 'down'
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp -and [int]$resp.StatusCode -eq 503) { return 'draining' }
        return 'down'
    } catch {
        return 'down'
    }
}

function Test-ServerRunning {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    return $svc -and ($svc.Status -eq 'Running' -or $svc.Status -eq 'StartPending')
}

function Start-Server([string]$reason) {
    Write-Log "$reason -> starting $ServiceName"
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
        Write-Log "$ServiceName started"
    } catch {
        Write-Log "Starting $ServiceName failed: $($_.Exception.Message)"
    }
}

function Stop-Server([string]$reason) {
    Write-Log "$reason -> stopping $ServiceName, handing off pending messages"
    try { Stop-Service -Name $ServiceName -NoWait -Force -ErrorAction Stop } catch {
        Write-Log "Stop request failed: $($_.Exception.Message)"
    }
    $deadline = (Get-Date).AddSeconds($StopTimeout)
    while ((Get-Date) -lt $deadline) {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $svc -or $svc.Status -eq 'Stopped') { Write-Log "$ServiceName stopped"; return }
        Start-Sleep -Seconds 1
    }
    $procId = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").ProcessId
    Write-Log "$ServiceName did not stop in ${StopTimeout}s, killing pid $procId"
    if ($procId) { Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue }
}

# Sends this node's registry to the peer, which merges it and returns the combined one.
# Only while PocketServer is stopped: a running server owns the file.
function Sync-Identities {
    if (-not $secret -or -not $peerUrl) { return }
    $body = '{}'
    if ((Test-Path $registry) -and (Get-Item $registry).Length -gt 0) {
        $body = [IO.File]::ReadAllText($registry)
    }
    try {
        $r = Invoke-WebRequest -Uri "$peerUrl/internal/identities/sync" -Method Post -UseBasicParsing -TimeoutSec 15 `
            -Headers @{ 'X-Pocket-Cluster-Secret' = $secret } -ContentType 'application/json' `
            -Body ([Text.Encoding]::UTF8.GetBytes($body))
        $merged = $r.Content
        if ($merged -is [byte[]]) { $merged = [Text.Encoding]::UTF8.GetString($merged) }
        if (-not $merged -or -not $merged.TrimStart().StartsWith('{')) { throw "unexpected response" }
        if ($merged -ne $body) {
            New-Item -ItemType Directory -Force -Path (Split-Path $registry) | Out-Null
            [IO.File]::WriteAllText($registry, $merged, (New-Object Text.UTF8Encoding($false)))
            Write-Log "Identity registry synced with peer"
        }
    } catch {
        Write-Log "Identity registry sync with peer failed: $($_.Exception.Message)"
    }
}

if ($CheckOnly) {
    $ips = if ($publicCheck) { (Resolve-PublicIps) -join ', ' } else { '(public check off)' }
    "Peer $peerUrl ($ips): $(Get-PeerState)"
    return
}

Write-Log "Watchdog started. Peer: $peerUrl$(if ($publicCheck) { ' (checked via its public URL)' })"
if ($role -ne 'primary' -and $role -ne 'standby') { Write-Log "Unknown role '$role', acting as standby"; $role = 'standby' }

$down = 0; $up = 0; $lastPeer = ''
$weStarted = $false
while ($true) {
    $state = Get-PeerState
    if ($state -ne $lastPeer) {
        if ($state -eq 'down-public') { Write-Log "Peer is down-public (answers on the tailnet, but its public URL does not: clients can't reach it)" }
        else { Write-Log "Peer is $state" }
        $lastPeer = $state
    }
    if ($state -eq 'up') { $up++; $down = 0 } else { $down++; $up = 0 }

    $running = Test-ServerRunning
    if ($weStarted -and -not $running) {
        Write-Log "$ServiceName exited unexpectedly (see Event Viewer > Application)"
        $weStarted = $false
    }

    if ($role -eq 'primary') {
        if (-not $running) { Start-Server 'Primary node'; $weStarted = $true }
    } elseif ($running) {
        if ($up -ge $UpThreshold) {
            Stop-Server 'Peer is back'
            $weStarted = $false
            Sync-Identities
        }
    } else {
        if ($state -eq 'draining') {
            Start-Server 'Peer is shutting down and handing off'; $weStarted = $true; $down = 0
        } elseif ($down -ge $DownThreshold) {
            Start-Server "Peer unreachable for $DownThreshold checks"; $weStarted = $true; $down = 0
        } elseif ($up -gt 0 -and (($up - 1) % $SyncEvery) -eq 0) {
            Sync-Identities
        }
    }

    Start-Sleep -Seconds $CheckInterval
}
