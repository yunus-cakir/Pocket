#!/bin/bash
# Installs the Pocket standby relay on macOS.
#
#   ./install.sh --secret "<Cluster secret printed by install-service.ps1 on the PC>"
#
# - Publishes Pocket.Server into ~/PocketServer/app (needs the .NET 10 SDK)
# - Writes its config: loopback 127.0.0.1:5200, role=standby, hand off to the PC
# - Renames this machine in Tailscale to "yunuscakir-mac" and enables Funnel on 5200
# - Installs a LaunchAgent that runs watchdog.sh at login and keeps it alive
# Safe to re-run (updates the build and restarts the watchdog).

set -euo pipefail

SECRET=""
PRIMARY_URL="https://yunuscakir.tail4d1102.ts.net"
TS_HOSTNAME="yunuscakir-mac"
INSTALL_DIR="$HOME/PocketServer"
LABEL="com.pocket.standby-watchdog"

while [ $# -gt 0 ]; do
    case "$1" in
        --secret) SECRET="$2"; shift 2 ;;
        --primary) PRIMARY_URL="$2"; shift 2 ;;
        --hostname) TS_HOSTNAME="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

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
if launchctl print "gui/$(id -u)/$LABEL" >/dev/null 2>&1; then
    step "Stopping existing watchdog"
    launchctl bootout "gui/$(id -u)/$LABEL" || true
    sleep 2
fi

# --- Publish ------------------------------------------------------------------
step "Publishing Pocket.Server"
mkdir -p "$INSTALL_DIR/app" "$INSTALL_DIR/logs"
"$DOTNET" publish "$PROJECT" -c Release -o "$INSTALL_DIR/app" --nologo -v q

step "Writing standby configuration"
cat > "$INSTALL_DIR/app/appsettings.Production.json" <<EOF
{
  "Urls": "http://127.0.0.1:5200",
  "Cluster": {
    "NodeName": "mac",
    "Role": "standby",
    "Secret": "$SECRET",
    "HandoffUrl": "$PRIMARY_URL"
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
        <key>PRIMARY_URL</key><string>$PRIMARY_URL</string>
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
echo "Standby URL : https://$DNS"
echo "Primary URL : $PRIMARY_URL"
echo "Logs        : $INSTALL_DIR/logs/watchdog.log, $INSTALL_DIR/logs/server.log"
echo
tail -n 3 "$INSTALL_DIR/logs/watchdog.log" 2>/dev/null || true
