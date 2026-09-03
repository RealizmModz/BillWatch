#!/bin/sh

set -eu

unit="billwatch-backup.timer"

if ! systemctl is-enabled --quiet "$unit"; then
    echo "$unit is not enabled." >&2
    exit 69
fi

if ! systemctl is-active --quiet "$unit"; then
    echo "$unit is not active." >&2
    exit 69
fi

next_run="$(
    systemctl list-timers "$unit" --all --no-legend |
    awk '{print $1, $2, $3, $4}'
)"

if [ -z "$next_run" ]; then
    echo "Could not resolve the next BillWatch backup timer run." >&2
    exit 69
fi

echo "BillWatch backup timer is enabled and active."
echo "Next run: $next_run"
