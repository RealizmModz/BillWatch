#!/bin/sh

set -eu

umask 077

alert_event="${1:-readiness-failed}"
target_name="${2:-unknown}"
run_id="${3:-unknown}"
webhook_url="${BILLWATCH_READINESS_ALERT_WEBHOOK_URL:-}"
curl_config=""

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

cleanup()
{
    if [ -n "$curl_config" ]; then
        rm -f "$curl_config"
    fi
}

trap cleanup EXIT HUP INT TERM

case "$webhook_url" in
    https://*) ;;
    *) fail "BillWatch external readiness alert webhook must be an HTTPS URL." 78 ;;
esac

case "$webhook_url" in
    *[[:space:]]*|*\"*|*\'*|*\`*|*\\*)
        fail "BillWatch external readiness alert webhook contains unsupported characters." 78
        ;;
esac

command -v curl >/dev/null 2>&1 ||
    fail "curl is required to deliver BillWatch external readiness alerts." 69

sanitize_metadata()
{
    printf '%s' "$1" | tr -cd 'A-Za-z0-9._:@/-'
}

event_safe="$(sanitize_metadata "$alert_event")"
target_safe="$(sanitize_metadata "$target_name")"
run_safe="$(sanitize_metadata "$run_id")"
timestamp="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

[ -n "$event_safe" ] || fail "Readiness alert event name is invalid." 64
[ -n "$target_safe" ] || target_safe=unknown
[ -n "$run_safe" ] || run_safe=unknown

payload="{\"source\":\"billwatch-external-readiness\",\"event\":\"$event_safe\",\"target\":\"$target_safe\",\"runId\":\"$run_safe\",\"occurredAtUtc\":\"$timestamp\"}"

curl_config="$(mktemp)"
printf 'url = "%s"\n' "$webhook_url" > "$curl_config"
chmod 600 "$curl_config"

if ! curl \
    --config "$curl_config" \
    --fail \
    --silent \
    --show-error \
    --output /dev/null \
    --connect-timeout 5 \
    --max-time 15 \
    --max-redirs 0 \
    --proto '=https' \
    --tlsv1.2 \
    --header 'Content-Type: application/json' \
    --data "$payload"
then
    fail "BillWatch external readiness alert delivery failed." 69
fi

printf '%s\n' "BillWatch external readiness alert delivered for $event_safe/$target_safe."
