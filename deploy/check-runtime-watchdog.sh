#!/bin/sh

set -eu

service_unit="billwatch-runtime-readiness.service"
timer_unit="billwatch-runtime-readiness.timer"

fail()
{
    echo "$1" >&2
    exit 69
}

if ! systemctl cat "$service_unit" >/dev/null 2>&1; then
    fail "$service_unit is not installed."
fi

if ! systemctl cat "$timer_unit" >/dev/null 2>&1; then
    fail "$timer_unit is not installed."
fi

if ! systemctl is-enabled --quiet "$timer_unit"; then
    fail "$timer_unit is not enabled."
fi

if ! systemctl is-active --quiet "$timer_unit"; then
    fail "$timer_unit is not active."
fi

if ! systemctl cat "$service_unit" |
    grep -Fq 'OnFailure=billwatch-operations-alert@%n.service'; then
    fail "$service_unit is not wired to the operations alert service."
fi

if ! systemctl cat "$service_unit" |
    grep -Fq 'ExecStart=/bin/sh /opt/billwatch/deploy/verify-production.sh /opt/billwatch'; then
    fail "$service_unit does not execute the guarded production verifier."
fi

if ! systemctl cat "$service_unit" |
    grep -Fq 'User=deploy'; then
    fail "$service_unit must run as the deploy account."
fi

if ! systemctl cat "$timer_unit" |
    grep -Fq 'OnBootSec=2min'; then
    fail "$timer_unit does not perform a post-boot verification."
fi

if ! systemctl cat "$timer_unit" |
    grep -Fq 'OnUnitInactiveSec=5min'; then
    fail "$timer_unit does not continuously verify the runtime after each completed check."
fi

next_run="$(
    systemctl list-timers "$timer_unit" --all --no-legend |
    awk '{print $1, $2, $3, $4}'
)"

if [ -z "$next_run" ]; then
    fail "Could not resolve the next BillWatch runtime verification."
fi

echo "BillWatch runtime readiness watchdog is installed, enabled, and active."
echo "Next run: $next_run"
