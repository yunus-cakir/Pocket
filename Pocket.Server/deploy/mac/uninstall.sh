#!/bin/bash
# Removes the Pocket standby watchdog (stops the standby server gracefully) and disables Funnel.
set -u
LABEL="com.pocket.standby-watchdog"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null && echo "Watchdog stopped."
rm -f "$PLIST"

TS="$(command -v tailscale || echo /Applications/Tailscale.app/Contents/MacOS/Tailscale)"
[ -x "$TS" ] && "$TS" funnel reset && echo "Funnel disabled."

echo "Files left in ~/PocketServer (delete manually if no longer needed)."
