#!/bin/bash
# Pocket two-way watchdog (macOS). Runs under launchd (see install.sh).
# The PC runs the same logic in deploy/windows/watchdog.ps1; each node watches the other.
#
# ROLE=primary (default for the Mac):
#   - keeps the local server running; restarts it if it exits.
#   - when it starts while the standby is serving, the standby's watchdog sees this node
#     healthy and stops its server, which hands its undelivered messages over to here.
#   - on a planned stop (update, uninstall, logout) the server drains: /health answers 503,
#     the standby's watchdog starts the standby at once, and this server hands its
#     undelivered messages to it before exiting (memory to memory, BR-201).
#
# ROLE=standby:
#   - peer healthy                    -> local server stays stopped
#   - peer draining (503)             -> start the local server now (peer is handing off to us)
#   - peer down N checks in a row     -> start the local server
#   - peer back M checks in a row     -> stop the local server (SIGTERM): it refuses new
#     connections, waits for the peer, disconnects clients and hands off undelivered messages.
#   - while the local server is stopped, the identity registry (public keys + usernames) is
#     synced with the peer every few checks, so this node knows every user if it takes over.

set -u

DIR="$(cd "$(dirname "$0")" && pwd)"
ROLE="${ROLE:-standby}"
PEER_URL="${PEER_URL:-${PRIMARY_URL:-https://yunuscakir.tail4d1102.ts.net}}"
DOTNET="${DOTNET_PATH:-dotnet}"
CHECK_INTERVAL="${CHECK_INTERVAL:-15}"   # seconds between checks
DOWN_THRESHOLD="${DOWN_THRESHOLD:-3}"    # failed checks before taking over (~45s)
UP_THRESHOLD="${UP_THRESHOLD:-2}"        # healthy checks before handing back (~30s)
SYNC_EVERY="${SYNC_EVERY:-4}"            # healthy checks between registry syncs (~60s)
STOP_TIMEOUT=150                         # server may wait for the peer and hand off large media

APP_DIR="$DIR/app"
REGISTRY="$APP_DIR/data/identities.json"

SERVER_PID=""
mkdir -p "$DIR/logs"

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') [$ROLE] $*"; }

cluster_secret() {
    sed -n 's/.*"Secret"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$APP_DIR/appsettings.Production.json" 2>/dev/null | head -n 1
}

# Sends this node's registry to the peer, which merges it and returns the combined one.
# Only while the local server is stopped: a running server owns the file.
sync_identities() {
    local secret body hdr tmp
    secret="$(cluster_secret)"
    [ -z "$secret" ] && return
    mkdir -p "$APP_DIR/data"
    body="$REGISTRY"
    if [ ! -s "$body" ]; then
        body="$APP_DIR/data/.empty.json"
        echo '{}' >"$body"
    fi
    hdr="$DIR/.sync-header"   # secret passed via a file so it doesn't show up in `ps`
    ( umask 077; printf 'X-Pocket-Cluster-Secret: %s\n' "$secret" >"$hdr" )
    tmp="$REGISTRY.sync"
    if curl -fsS --max-time 15 -X POST -H @"$hdr" -H 'Content-Type: application/json' \
            --data-binary @"$body" -o "$tmp" "$PEER_URL/internal/identities/sync" 2>/dev/null \
        && [ "$(head -c 1 "$tmp")" = "{" ]; then
        if cmp -s "$tmp" "$REGISTRY"; then
            rm -f "$tmp"
        else
            mv "$tmp" "$REGISTRY"
            log "Identity registry synced with peer"
        fi
    else
        rm -f "$tmp"
        log "Identity registry sync with peer failed"
    fi
    rm -f "$hdr"
}

# Prints "up", "draining" (the peer is shutting down and handing off) or "down".
peer_state() {
    local code
    code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 8 "$PEER_URL/health" 2>/dev/null)"
    case "$code" in
        200) echo up ;;
        503) echo draining ;;
        *) echo down ;;
    esac
}

server_running() {
    [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null
}

start_server() {
    log "$1 -> starting local server"
    (
        cd "$APP_DIR" || exit 1
        ASPNETCORE_ENVIRONMENT=Production exec "$DOTNET" "$APP_DIR/Pocket.Server.dll"
    ) >>"$DIR/logs/server.log" 2>&1 &
    SERVER_PID=$!
    log "Local server started (pid $SERVER_PID)"
}

stop_server() {
    log "$1 -> stopping local server (pid $SERVER_PID), handing off pending messages"
    kill -TERM "$SERVER_PID" 2>/dev/null
    local waited=0
    while server_running && [ "$waited" -lt "$STOP_TIMEOUT" ]; do
        sleep 1
        waited=$((waited + 1))
    done
    if server_running; then
        log "Server did not exit in ${STOP_TIMEOUT}s, killing"
        kill -KILL "$SERVER_PID" 2>/dev/null
    fi
    wait "$SERVER_PID" 2>/dev/null
    SERVER_PID=""
    log "Local server stopped"
}

on_exit() {
    if server_running; then stop_server "Watchdog exiting"; fi
    exit 0
}
trap on_exit TERM INT

log "Watchdog started. Peer: $PEER_URL"
down=0
up=0
last_peer=""
restarts=0
while true; do
    state="$(peer_state)"
    if [ "$state" != "$last_peer" ]; then
        log "Peer is $state"
        last_peer="$state"
    fi
    case "$state" in
        up) up=$((up + 1)); down=0 ;;
        *) down=$((down + 1)); up=0 ;;
    esac

    if [ -n "$SERVER_PID" ] && ! server_running; then
        log "Local server exited unexpectedly (see logs/server.log)"
        wait "$SERVER_PID" 2>/dev/null
        SERVER_PID=""
    fi

    if [ "$ROLE" = "primary" ]; then
        # Primary: always serve. Back off a little if the server keeps crashing.
        if server_running; then
            restarts=0
        else
            if [ "$restarts" -gt 0 ]; then
                sleep $(( restarts < 6 ? restarts * 10 : 60 )) &
                wait $!
            fi
            start_server "Primary node"
            restarts=$((restarts + 1))
        fi
    else
        if server_running; then
            if [ "$up" -ge "$UP_THRESHOLD" ]; then
                stop_server "Peer is back"
                sync_identities
            fi
        else
            if [ "$state" = "draining" ]; then
                start_server "Peer is shutting down and handing off"
                down=0
            elif [ "$down" -ge "$DOWN_THRESHOLD" ]; then
                start_server "Peer unreachable for $DOWN_THRESHOLD checks"
                down=0
            elif [ "$up" -gt 0 ] && [ $(( (up - 1) % SYNC_EVERY )) -eq 0 ]; then
                sync_identities
            fi
        fi
    fi

    # Background sleep so SIGTERM from launchd is handled immediately.
    sleep "$CHECK_INTERVAL" &
    wait $!
done
