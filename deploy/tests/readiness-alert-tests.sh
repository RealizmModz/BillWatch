#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Readiness alert test failed: $1" >&2
    exit 1
}

sender="$root_dir/deploy/send-readiness-alert.sh"
[ -f "$sender" ] || fail "sender script is missing."
sh -n "$sender" || fail "sender script has invalid POSIX shell syntax."

if BILLWATCH_READINESS_ALERT_WEBHOOK_URL='' sh "$sender" test API 1 >/dev/null 2>&1; then
    fail "sender accepted a missing webhook URL."
fi

if BILLWATCH_READINESS_ALERT_WEBHOOK_URL='http://alerts.example.test/hook' sh "$sender" test API 1 >/dev/null 2>&1; then
    fail "sender accepted a non-HTTPS webhook URL."
fi

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_CURL_ARGV:?}"
: "${BILLWATCH_TEST_CURL_CONFIG_COPY:?}"
config_path=""
: > "$BILLWATCH_TEST_CURL_ARGV"
while [ "$#" -gt 0 ]
do
    printf '%s\n' "$1" >> "$BILLWATCH_TEST_CURL_ARGV"
    if [ "$1" = "--config" ]; then
        shift
        [ "$#" -gt 0 ] || exit 97
        config_path="$1"
        printf '%s\n' "$1" >> "$BILLWATCH_TEST_CURL_ARGV"
    fi
    shift
done
[ -n "$config_path" ] || exit 98
cp "$config_path" "$BILLWATCH_TEST_CURL_CONFIG_COPY"
stat -c '%a' "$config_path" > "${BILLWATCH_TEST_CURL_CONFIG_COPY}.mode"
exit 0
EOF
chmod 700 "$fake_bin/curl"

argv_capture="$temp_dir/curl-argv"
config_capture="$temp_dir/curl-config"
secret_url='https://alerts.example.test/private-readiness-token'

PATH="$fake_bin:$PATH" \
BILLWATCH_TEST_CURL_ARGV="$argv_capture" \
BILLWATCH_TEST_CURL_CONFIG_COPY="$config_capture" \
BILLWATCH_READINESS_ALERT_WEBHOOK_URL="$secret_url" \
sh "$sender" readiness-forced-failure API 123456 >/dev/null

[ -f "$config_capture" ] || fail "curl config was not captured."
grep -Fq "$secret_url" "$config_capture" || fail "private webhook was not written to the protected curl config."
[ "$(cat "${config_capture}.mode")" = "600" ] || fail "curl config was not mode 600."
if grep -Fq "$secret_url" "$argv_capture"; then
    fail "private webhook leaked into curl process arguments."
fi
grep -Fq -- '--proto' "$argv_capture" || fail "sender must constrain curl protocol."
grep -Fq '=https' "$argv_capture" || fail "sender must require HTTPS in curl protocol policy."
grep -Fq -- '--max-redirs' "$argv_capture" || fail "sender must disable redirects."
grep -Fq 'billwatch-external-readiness' "$argv_capture" || fail "sender payload did not identify the external readiness source."
grep -Fq 'readiness-forced-failure' "$argv_capture" || fail "sender payload omitted the controlled event name."
grep -Fq '"target":"API"' "$argv_capture" || fail "sender payload omitted the target metadata."
grep -Fq '"runId":"123456"' "$argv_capture" || fail "sender payload omitted the workflow run identifier."

if grep -Eq 'Authorization:|Bearer |password|secret|token' "$argv_capture"; then
    fail "sender curl arguments contain credential-like data."
fi

printf '%s\n' 'External readiness alert regression tests passed.'
