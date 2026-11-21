#!/bin/bash
# Removes the Pocket watchdog (stops the local server gracefully; a primary hands off to the
# standby first) and disables Funnel.
set -u
for LABEL in com.pocket.watchdog com.pocket.standby-watchdog; do
    launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null && echo "Watchdog $LABEL stopped."
    rm -f "$HOME/Library/LaunchAgents/$LABEL.plist"
done

TS="$(command -v tailscale || echo /Applications/Tailscale.app/Contents/MacOS/Tailscale)"
[ -x "$TS" ] && "$TS" funnel reset && echo "Funnel disabled."

echo "Files left in ~/PocketServer (delete manually if no longer needed)."
