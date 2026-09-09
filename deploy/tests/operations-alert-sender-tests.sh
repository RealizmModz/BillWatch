#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Operations alert sender test failed: $1" >&2
    exit 1
}

deployment="$temp_dir/deployment"
fake_bin="$temp_dir/bin"
mkdir -p "$deployment" "$fake_bin"

cat > "$fake_bin/curl" <<'SCRIPT'
#!/bin/sh
set -eu
payload=
config=
while [ "$#" -gt 0 ]; do
    case "$1" in
        --data)
            shift
            payload=$1
            ;;
        --config)
            shift
            config=$1
            ;;
    esac
    shift
done
[ -n "$config" ] || exit 2
[ -f "$config" ] || exit 3
printf '%s' "$payload" > "$BILLWATCH_TEST_PAYLOAD_FILE"
SCRIPT
chmod 755 "$fake_bin/curl"

write_env()
{
    webhook_url=$1
    cat > "$deployment/.env.production" <<EOF
BILLWATCH_OPERATIONS_ALERTING_ENABLED=true
BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL=$webhook_url
EOF
    chmod 600 "$deployment/.env.production"
}

generic_payload="$temp_dir/generic-payload.json"
write_env 'https://alerts.billwatch.test/hooks/private-token'
PATH="$fake_bin:$PATH" \
BILLWATCH_TEST_PAYLOAD_FILE="$generic_payload" \
    sh "$root_dir/deploy/send-operations-alert.sh" "$deployment" readiness-test manual >/dev/null

grep -Fq '"source":"billwatch-production"' "$generic_payload" || fail "generic webhook payload lost the BillWatch source metadata."
grep -Fq '"event":"readiness-test"' "$generic_payload" || fail "generic webhook payload lost the event metadata."
grep -Fq '"unit":"manual"' "$generic_payload" || fail "generic webhook payload lost the unit metadata."
if grep -Fq '"text":' "$generic_payload"; then
    fail "generic webhook payload was unexpectedly converted to Slack format."
fi

slack_payload="$temp_dir/slack-payload.json"
write_env 'https://hooks.slack.com/services/T00000000/B00000000/private-token'
PATH="$fake_bin:$PATH" \
BILLWATCH_TEST_PAYLOAD_FILE="$slack_payload" \
    sh "$root_dir/deploy/send-operations-alert.sh" "$deployment" readiness-test manual >/dev/null

grep -Fq '"text":"BillWatch production alert\nSource: billwatch-production\nEvent: readiness-test\nUnit: manual\nHost:' "$slack_payload" ||
    fail "Slack incoming webhook payload was not rendered as a readable Slack message."
grep -Fq '\nOccurred at UTC:' "$slack_payload" || fail "Slack payload omitted the UTC timestamp label."
if grep -Fq '"source":"billwatch-production"' "$slack_payload"; then
    fail "Slack incoming webhook payload retained the incompatible generic top-level schema."
fi

printf '%s\n' 'Operations alert sender tests passed.'
