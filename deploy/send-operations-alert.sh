#!/bin/sh

set -eu

umask 077

deployment_directory="${1:-}"
event_name="${2:-}"
unit_name="${3:-unknown}"
curl_config=""

fail()
{
    echo "$1" >&2
    exit "${2:-1}"
}

cleanup()
{
    if [ -n "$curl_config" ]; then
        rm -f "$curl_config"
    fi
}

trap cleanup EXIT HUP INT TERM

if [ -z "$deployment_directory" ] ||
   [ -z "$event_name" ] ||
   [ ! -f "$deployment_directory/.env.production" ]; then
    fail "Usage: $0 <deployment-directory> <event-name> [unit-name]" 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
environment_file="$deployment_directory/.env.production"

environment_owner="$(stat -c '%u' "$environment_file")"
environment_permissions="$(stat -c '%a' "$environment_file")"

if [ "$environment_owner" -ne "$(id -u)" ] ||
   [ "$((environment_permissions % 100))" -ne 0 ]; then
    fail ".env.production must be owned by the deployment account and inaccessible to group/other users." 77
fi

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

alerting_enabled="$(read_optional_value BILLWATCH_OPERATIONS_ALERTING_ENABLED)"
[ -n "$alerting_enabled" ] || alerting_enabled=false

if [ "$alerting_enabled" != true ]; then
    fail "BillWatch operations alerting is not enabled." 78
fi

webhook_url="$(read_optional_value BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL)"

case "$webhook_url" in
    https://*) ;;
    *) fail "BillWatch operations alert webhook must be an HTTPS URL." 78 ;;
esac

case "$webhook_url" in
    *[[:space:]]*|*\"*|*\'*|*\`*|*\\*)
        fail "BillWatch operations alert webhook contains unsupported characters." 78
        ;;
esac

command -v curl >/dev/null 2>&1 ||
    fail "curl is required to deliver BillWatch operations alerts." 69

sanitize_metadata()
{
    printf '%s' "$1" |
        tr -cd 'A-Za-z0-9._:@/-'
}

event_safe="$(sanitize_metadata "$event_name")"
unit_safe="$(sanitize_metadata "$unit_name")"
host_safe="$(sanitize_metadata "$(hostname -f 2>/dev/null || hostname)")"
timestamp="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

[ -n "$event_safe" ] || fail "Alert event name is invalid." 64
[ -n "$unit_safe" ] || unit_safe=unknown
[ -n "$host_safe" ] || host_safe=unknown

payload="{\"source\":\"billwatch-production\",\"event\":\"$event_safe\",\"unit\":\"$unit_safe\",\"host\":\"$host_safe\",\"occurredAtUtc\":\"$timestamp\"}"

curl_config="$(mktemp)"
printf 'url = "%s"\n' "$webhook_url" > "$curl_config"
chmod 600 "$curl_config"

if ! curl \
    --config "$curl_config" \
    --fail \
    --silent \
    --output /dev/null \
    --max-time 15 \
    --header 'Content-Type: application/json' \
    --data "$payload"
then
    fail "BillWatch operations alert delivery failed." 69
fi

echo "BillWatch operations alert delivered for $event_safe."
