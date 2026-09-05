#!/bin/sh

set -eu

deployment_directory="${1:-}"

fail()
{
    echo "$1" >&2
    exit "${2:-1}"
}

if [ -z "$deployment_directory" ] ||
   [ ! -f "$deployment_directory/.env.production" ]; then
    fail "A BillWatch deployment directory with .env.production is required." 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
environment_file="$deployment_directory/.env.production"

read_optional_value()
{
    key="$1"
    count="$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$environment_file")"

    if [ "$count" -gt 1 ]; then
        fail "$key must not appear more than once." 64
    fi

    if [ "$count" -eq 0 ]; then
        printf '%s' ""
        return
    fi

    awk -v prefix="$key=" 'index($0, prefix) == 1 { print substr($0, length(prefix) + 1); exit }' "$environment_file"
}

require_failure_route()
{
    unit="$1"

    if ! systemctl cat "$unit" 2>/dev/null |
        grep -Fq 'OnFailure=billwatch-operations-alert@%n.service'; then
        fail "$unit is not wired to the operations alert service." 69
    fi
}

alerting_enabled="$(read_optional_value BILLWATCH_OPERATIONS_ALERTING_ENABLED)"
webhook_url="$(read_optional_value BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL)"

if [ "$alerting_enabled" != true ]; then
    fail "BillWatch operations alerting is not enabled." 69
fi

case "$webhook_url" in
    https://*) ;;
    *) fail "BillWatch operations alert webhook must be configured as HTTPS." 69 ;;
esac

require_failure_route billwatch-backup.service
require_failure_route billwatch-runtime-readiness.service

if ! systemctl cat 'billwatch-operations-alert@.service' >/dev/null 2>&1; then
    fail "billwatch-operations-alert@.service is not installed." 69
fi

if [ ! -x "$deployment_directory/deploy/send-operations-alert.sh" ]; then
    fail "The BillWatch operations alert sender is missing or not executable." 69
fi

echo "BillWatch backup and runtime failure alerting are configured."
echo "Run deploy/send-operations-alert.sh manually with event 'readiness-test' to prove external delivery before beta invitations."
