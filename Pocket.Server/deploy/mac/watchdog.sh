#!/bin/bash
# Pocket standby watchdog (macOS). Runs under launchd (see install.sh).
#
# Keeps the rule "only one relay is active at a time":
#   - primary (PC) healthy        -> standby server stays stopped
#   - primary down N checks in a row -> start the standby server here
#   - primary back M checks in a row -> stop the standby server (SIGTERM). On shutdown
#     the server refuses new connections, disconnects clients so they fail back to the
#     primary, and hands its undelivered messages to the primary (memory to memory).

set -u

DIR="$(cd "$(dirname "$0")" && pwd)"
PRIMARY_URL="${PRIMARY_URL:-https://yunuscakir.tail4d1102.ts.net}"
DOTNET="${DOTNET_PATH:-dotnet}"
CHECK_INTERVAL="${CHECK_INTERVAL:-15}"   # seconds between checks
DOWN_THRESHOLD="${DOWN_THRESHOLD:-3}"    # failed checks before taking over (~45s)
UP_THRESHOLD="${UP_THRESHOLD:-2}"        # healthy checks before handing back (~30s)
STOP_TIMEOUT=150                         # server needs time to hand off large media

SERVER_PID=""
mkdir -p "$DIR/logs"

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

primary_healthy() {
    curl -fsS --max-time 8 "$PRIMARY_URL/health" >/dev/null 2>&1
}

server_running() {
    [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null
}

start_server() {
    log "Primary unreachable for $DOWN_THRESHOLD checks -> starting standby server"
    (
        cd "$DIR/app" || exit 1
        ASPNETCORE_ENVIRONMENT=Production exec "$DOTNET" "$DIR/app/Pocket.Server.dll"
    ) >>"$DIR/logs/server.log" 2>&1 &
    SERVER_PID=$!
    log "Standby server started (pid $SERVER_PID)"
}

stop_server() {
    local reason="$1"
    log "$reason -> stopping standby server (pid $SERVER_PID), handing off pending messages"
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
    log "Standby server stopped"
}

on_exit() {
    if server_running; then stop_server "Watchdog exiting"; fi
    exit 0
}
trap on_exit TERM INT

log "Watchdog started. Primary: $PRIMARY_URL"
down=0
up=0
while true; do
    if primary_healthy; then
        up=$((up + 1)); down=0
    else
        down=$((down + 1)); up=0
    fi

    if server_running; then
        if [ "$up" -ge "$UP_THRESHOLD" ]; then
            stop_server "Primary is back"
        fi
    else
        if [ -n "$SERVER_PID" ]; then
            log "Standby server exited unexpectedly (see logs/server.log)"
            SERVER_PID=""
        fi
        if [ "$down" -ge "$DOWN_THRESHOLD" ]; then
            start_server
            down=0
        fi
    fi

    # Background sleep so SIGTERM from launchd is handled immediately.
    sleep "$CHECK_INTERVAL" &
    wait $!
done
