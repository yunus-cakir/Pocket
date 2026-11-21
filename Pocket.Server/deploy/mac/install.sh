#!/bin/bash
# Installs the Pocket relay node on macOS (primary by default; the PC is the standby).
#
#   ./install.sh --secret "<Cluster secret printed by install-service.ps1 on the PC>"
#   ./install.sh            (update: keeps the secret of the existing install)
#   ./install.sh --role standby   (make the Mac the standby again)
#
# - Publishes Pocket.Server into ~/PocketServer/app (needs the .NET 10 SDK)
# - Writes its config: loopback 127.0.0.1:5200, role, hand off to / sync with the PC
# - Renames this machine in Tailscale to "yunuscakir-mac" and enables Funnel on 5200
# - Installs a LaunchAgent that runs watchdog.sh at login and keeps it alive
# Safe to re-run (updates the build and restarts the watchdog).

set -euo pipefail

SECRET=""
ROLE="primary"
PEER_URL="https://yunuscakir.tail4d1102.ts.net"
TS_HOSTNAME="yunuscakir-mac"
INSTALL_DIR="$HOME/PocketServer"
LABEL="com.pocket.watchdog"
OLD_LABELS="com.pocket.standby-watchdog"

while [ $# -gt 0 ]; do
    case "$1" in
        --secret) SECRET="$2"; shift 2 ;;
        --role) ROLE="$2"; shift 2 ;;
        --peer|--primary) PEER_URL="$2"; shift 2 ;;
        --hostname) TS_HOSTNAME="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

# Updating an existing install: keep its secret unless a new one is given.
EXISTING_CONFIG="$HOME/PocketServer/app/appsettings.Production.json"
if [ -z "$SECRET" ] && [ -f "$EXISTING_CONFIG" ]; then
    SECRET="$(sed -n 's/.*"Secret"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$EXISTING_CONFIG" | head -n 1)"
fi

case "$ROLE" in primary|standby) ;; *) echo "--role must be primary or standby"; exit 1 ;; esac

if [ -z "$SECRET" ]; then
    echo "Usage: $0 --secret \"<secret from the PC's install-service.ps1 output>\""
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$SCRIPT_DIR/../../Pocket.Server.csproj"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

step() { printf '\n==> %s\n' "$*"; }

# --- .NET ---------------------------------------------------------------------
DOTNET="$(command -v dotnet || true)"
[ -z "$DOTNET" ] && [ -x /usr/local/share/dotnet/dotnet ] && DOTNET=/usr/local/share/dotnet/dotnet
if [ -z "$DOTNET" ] || ! "$DOTNET" --list-sdks | grep -q '^10\.'; then
    echo "The .NET 10 SDK is required: https://dotnet.microsoft.com/download/dotnet/10.0"
    echo "(or: brew install --cask dotnet-sdk), then re-run this script."
    exit 1
fi

# --- Tailscale ----------------------------------------------------------------
TS="$(command -v tailscale || true)"
[ -z "$TS" ] && [ -x /Applications/Tailscale.app/Contents/MacOS/Tailscale ] && TS=/Applications/Tailscale.app/Contents/MacOS/Tailscale
if [ -z "$TS" ]; then
    echo "Tailscale CLI not found. Install Tailscale and log in first."
    exit 1
fi

# --- Stop running watchdog (it stops the server gracefully) --------------------
# A primary's server waits for the standby to come up and hands off to it, so this can take
# up to a minute or two when there are undelivered messages.
for l in $LABEL $OLD_LABELS; do
    if launchctl print "gui/$(id -u)/$l" >/dev/null 2>&1; then
        step "Stopping existing watchdog ($l)"
        launchctl bootout "gui/$(id -u)/$l" || true
        sleep 2
    fi
    [ "$l" != "$LABEL" ] && rm -f "$HOME/Library/LaunchAgents/$l.plist"
done

# --- Publish ------------------------------------------------------------------
step "Publishing Pocket.Server"
mkdir -p "$INSTALL_DIR/app" "$INSTALL_DIR/logs"
"$DOTNET" publish "$PROJECT" -c Release -o "$INSTALL_DIR/app" --nologo -v q

step "Writing $ROLE configuration"
cat > "$INSTALL_DIR/app/appsettings.Production.json" <<EOF
{
  "Urls": "http://127.0.0.1:5200",
  "Cluster": {
    "NodeName": "mac",
    "Role": "$ROLE",
    "Secret": "$SECRET",
    "HandoffUrl": "$PEER_URL",
    "PeerUrl": "$PEER_URL"
  }
}
EOF
chmod 600 "$INSTALL_DIR/app/appsettings.Production.json"

cp "$SCRIPT_DIR/watchdog.sh" "$INSTALL_DIR/watchdog.sh"
chmod +x "$INSTALL_DIR/watchdog.sh"

# --- Tailscale name + Funnel (Funnel stays on; it 502s while the server is stopped) ---
step "Configuring Tailscale ($TS_HOSTNAME + Funnel on 5200)"
"$TS" set --hostname="$TS_HOSTNAME"
sleep 3
"$TS" funnel reset >/dev/null 2>&1 || true
if ! "$TS" funnel --bg --yes 5200; then
    echo "Funnel could not be enabled. If a link was printed, open it, approve, and re-run."
    exit 1
fi

# --- LaunchAgent --------------------------------------------------------------
step "Installing LaunchAgent $LABEL"
mkdir -p "$HOME/Library/LaunchAgents"
cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>$LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>/bin/bash</string>
        <string>$INSTALL_DIR/watchdog.sh</string>
    </array>
    <key>EnvironmentVariables</key>
    <dict>
        <key>DOTNET_PATH</key><string>$DOTNET</string>
        <key>ROLE</key><string>$ROLE</string>
        <key>PEER_URL</key><string>$PEER_URL</string>
        <key>PATH</key><string>/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin</string>
    </dict>
    <key>WorkingDirectory</key><string>$INSTALL_DIR</string>
    <key>RunAtLoad</key><true/>
    <key>KeepAlive</key><true/>
    <key>ExitTimeOut</key><integer>180</integer>
    <key>StandardOutPath</key><string>$INSTALL_DIR/logs/watchdog.log</string>
    <key>StandardErrorPath</key><string>$INSTALL_DIR/logs/watchdog.log</string>
</dict>
</plist>
EOF
launchctl bootstrap "gui/$(id -u)" "$PLIST"

step "Done"
DNS="$("$TS" status --json 2>/dev/null | grep -m1 '"DNSName"' | sed -E 's/.*"DNSName": *"([^"]+)\.".*/\1/')"
echo "This node   : https://$DNS ($ROLE)"
echo "Peer        : $PEER_URL"
echo "Logs        : $INSTALL_DIR/logs/watchdog.log, $INSTALL_DIR/logs/server.log"
echo
tail -n 3 "$INSTALL_DIR/logs/watchdog.log" 2>/dev/null || true
