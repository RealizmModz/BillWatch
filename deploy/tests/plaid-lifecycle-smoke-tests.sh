#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Plaid lifecycle smoke test failed: $1" >&2
    exit 1
}

smoke_script="$root_dir/deploy/smoke-plaid-lifecycle.sh"
[ -f "$smoke_script" ] || fail "Plaid lifecycle smoke harness is missing."
sh -n "$smoke_script" || fail "Plaid lifecycle smoke harness has invalid POSIX shell syntax."

grep -Fq 'BILLWATCH_PLAID_SMOKE_PASSWORD_FILE' "$smoke_script" ||
    fail "Plaid lifecycle smoke harness must support protected password-file input."
grep -Fq 'BILLWATCH_PLAID_SMOKE_ALLOW_DISCONNECT' "$smoke_script" ||
    fail "Plaid lifecycle smoke harness must require explicit disconnect opt-in."
grep -Fq 'update-link-token' "$smoke_script" ||
    fail "Plaid lifecycle smoke harness must exercise update mode."
grep -Fq 'post-disconnect update-mode rejection' "$smoke_script" ||
    fail "Plaid lifecycle smoke harness must prove disconnected connections cannot re-enter update mode."

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
curl_log="$temp_dir/curl.log"
disconnected_state="$temp_dir/disconnected"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu

output=""
request="GET"
url=""

printf '%s\n' "$*" >> "$FAKE_CURL_LOG"

while [ "$#" -gt 0 ]
do
    case "$1" in
        --output)
            output="$2"
            shift 2
            ;;
        --request)
            request="$2"
            shift 2
            ;;
        --write-out|--header|--data-binary|--config)
            shift 2
            ;;
        --silent|--show-error)
            shift
            ;;
        https://*)
            url="$1"
            shift
            ;;
        *)
            shift
            ;;
    esac
done

[ -n "$url" ] || exit 90

code=200
body='{}'

case "$url" in
    */api/auth/login)
        [ "$request" = "POST" ] || exit 91
        body='{"accessToken":"FAKE-BEARER-TOKEN","refreshToken":"FAKE-REFRESH-TOKEN"}'
        ;;
    */api/bank-connections)
        [ "$request" = "GET" ] || exit 92
        body="[{\"id\":\"$FAKE_OWNED_CONNECTION_ID\",\"institutionName\":\"Owned Test Bank\",\"status\":0},{\"id\":\"$FAKE_DISCONNECT_CONNECTION_ID\",\"institutionName\":\"Disposable Test Bank\",\"status\":0}]"
        ;;
    */api/bank-connections/*)
        [ "$request" = "DELETE" ] || exit 93
        connection_id="${url##*/}"
        [ "$connection_id" = "$FAKE_DISCONNECT_CONNECTION_ID" ] || exit 94
        : > "$FAKE_DISCONNECTED_STATE"
        code=204
        body=''
        ;;
    */api/plaid/connections/*/update-link-token)
        [ "$request" = "POST" ] || exit 95
        connection_id="$(printf '%s\n' "$url" | sed 's#^.*/connections/\([^/]*\)/update-link-token$#\1#')"

        if [ "$connection_id" = "$FAKE_FOREIGN_CONNECTION_ID" ]; then
            code=404
            body='{"message":"Bank connection was not found."}'
        elif [ "$connection_id" = "$FAKE_DISCONNECT_CONNECTION_ID" ] && [ -f "$FAKE_DISCONNECTED_STATE" ]; then
            code=409
            body='{"message":"Bank connection cannot be updated."}'
        elif [ "$connection_id" = "$FAKE_OWNED_CONNECTION_ID" ] || [ "$connection_id" = "$FAKE_DISCONNECT_CONNECTION_ID" ]; then
            if [ "${FAKE_EXPOSE_SECRET:-false}" = "true" ]; then
                body='{"sessionId":"44444444-4444-4444-4444-444444444444","hostedLinkUrl":"https://secure.plaid.com/link/session","protectedPlaidAccessToken":"must-never-return"}'
            elif [ "${FAKE_BAD_HOST:-false}" = "true" ]; then
                body='{"sessionId":"44444444-4444-4444-4444-444444444444","hostedLinkUrl":"https://evil.example.test/link/session"}'
            else
                body='{"sessionId":"44444444-4444-4444-4444-444444444444","hostedLinkUrl":"https://secure.plaid.com/link/session"}'
            fi
        else
            code=404
            body='{"message":"Bank connection was not found."}'
        fi
        ;;
esac

if [ -n "$output" ] && [ "$output" != "/dev/null" ]; then
    printf '%s' "$body" > "$output"
fi

printf '%s' "$code"
EOF
chmod 700 "$fake_bin/curl"

password_file="$temp_dir/password"
printf '%s\n' 'PlaidSmokePassword!123456' > "$password_file"
chmod 600 "$password_file"

owned_connection_id='11111111-1111-1111-1111-111111111111'
foreign_connection_id='22222222-2222-2222-2222-222222222222'
disconnect_connection_id='33333333-3333-3333-3333-333333333333'

run_smoke()
{
    rm -f "$disconnected_state"
    env \
        PATH="$fake_bin:$PATH" \
        FAKE_CURL_LOG="$curl_log" \
        FAKE_DISCONNECTED_STATE="$disconnected_state" \
        FAKE_OWNED_CONNECTION_ID="$owned_connection_id" \
        FAKE_FOREIGN_CONNECTION_ID="$foreign_connection_id" \
        FAKE_DISCONNECT_CONNECTION_ID="$disconnect_connection_id" \
        BILLWATCH_PLAID_SMOKE_EMAIL='plaid-smoke@example.test' \
        BILLWATCH_PLAID_SMOKE_PASSWORD_FILE="$password_file" \
        BILLWATCH_PLAID_SMOKE_CONNECTION_ID="$owned_connection_id" \
        BILLWATCH_PLAID_SMOKE_FOREIGN_CONNECTION_ID="$foreign_connection_id" \
        "$@" \
        sh "$smoke_script" \
            'https://api.example.test'
}

: > "$curl_log"
run_smoke > "$temp_dir/safe.out"

grep -Fq 'PASS Plaid update-mode Hosted Link session boundary' "$temp_dir/safe.out" ||
    fail "update-mode Hosted Link proof did not run."
grep -Fq 'PASS Plaid update-mode cross-user isolation (404)' "$temp_dir/safe.out" ||
    fail "cross-user update-mode proof did not run."
grep -Fq 'BillWatch guarded Plaid lifecycle smoke harness passed.' "$temp_dir/safe.out" ||
    fail "safe Plaid lifecycle smoke path did not complete."

if grep -Fq -- '--request DELETE' "$curl_log"; then
    fail "Plaid lifecycle smoke harness performed a disconnect without explicit mutation opt-in."
fi

for secret in \
    'PlaidSmokePassword!123456' \
    'FAKE-BEARER-TOKEN' \
    'FAKE-REFRESH-TOKEN'
do
    if grep -Fq "$secret" "$curl_log"; then
        fail "credential appeared in curl process arguments: $secret"
    fi
done

: > "$curl_log"
run_smoke \
    BILLWATCH_PLAID_SMOKE_ALLOW_DISCONNECT=true \
    BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID="$disconnect_connection_id" \
    > "$temp_dir/disconnect.out"

grep -Fq 'PASS explicit Plaid disconnect and post-disconnect update-mode rejection (204/409)' "$temp_dir/disconnect.out" ||
    fail "explicit disconnect lifecycle proof did not complete."
grep -Fq -- '--request DELETE' "$curl_log" ||
    fail "explicitly enabled disconnect path did not issue a DELETE."

if run_smoke \
    BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID="$disconnect_connection_id" \
    > /dev/null 2>&1; then
    fail "Plaid lifecycle smoke harness accepted a disconnect ID without mutation opt-in."
fi

if run_smoke \
    BILLWATCH_PLAID_SMOKE_ALLOW_DISCONNECT=true \
    > /dev/null 2>&1; then
    fail "Plaid lifecycle smoke harness enabled disconnect without an explicit disposable connection ID."
fi

if run_smoke FAKE_EXPOSE_SECRET=true > /dev/null 2>&1; then
    fail "Plaid lifecycle smoke harness accepted a response containing a protected provider credential."
fi

if run_smoke FAKE_BAD_HOST=true > /dev/null 2>&1; then
    fail "Plaid lifecycle smoke harness accepted a Hosted Link URL outside plaid.com."
fi

chmod 644 "$password_file"
if run_smoke > /dev/null 2>&1; then
    fail "Plaid lifecycle smoke harness accepted an insecure password-file mode."
fi
chmod 600 "$password_file"

if env \
    PATH="$fake_bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    FAKE_DISCONNECTED_STATE="$disconnected_state" \
    FAKE_OWNED_CONNECTION_ID="$owned_connection_id" \
    FAKE_FOREIGN_CONNECTION_ID="$foreign_connection_id" \
    FAKE_DISCONNECT_CONNECTION_ID="$disconnect_connection_id" \
    BILLWATCH_PLAID_SMOKE_EMAIL='plaid-smoke@example.test' \
    BILLWATCH_PLAID_SMOKE_PASSWORD_FILE="$password_file" \
    BILLWATCH_PLAID_SMOKE_CONNECTION_ID="$owned_connection_id" \
    sh "$smoke_script" \
        'http://api.example.test' \
        > /dev/null 2>&1; then
    fail "Plaid lifecycle smoke harness accepted an HTTP API URL."
fi

printf '%s\n' 'Guarded Plaid lifecycle smoke harness tests passed.'
