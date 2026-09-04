#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Access-key smoke test failed: $1" >&2
    exit 1
}

smoke_script="$root_dir/deploy/smoke-access-key-lifecycle.sh"
[ -f "$smoke_script" ] || fail "lifecycle smoke harness is missing."
sh -n "$smoke_script" || fail "lifecycle smoke harness has invalid POSIX shell syntax."
grep -Fq 'BILLWATCH_ACCESS_KEY_SMOKE_ALLOW_MUTATIONS=true' "$smoke_script" ||
    fail "lifecycle smoke must require explicit mutation opt-in."
grep -Fq 'chmod 600 "$auth_config"' "$smoke_script" ||
    fail "lifecycle smoke must protect bearer curl configuration."
if grep -E -- '--header[ =]+["'\'']?Authorization: Bearer' "$smoke_script" >/dev/null; then
    fail "lifecycle smoke must not place bearer tokens in curl argv."
fi

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
curl_log="$temp_dir/curl.log"
state_file="$temp_dir/state"
printf 'created=0\nrevoked=0\n' > "$state_file"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu
output=""
url=""
request="GET"
config=""

printf '%s\n' "$*" >> "$FAKE_CURL_LOG"
while [ "$#" -gt 0 ]; do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        --request) request="$2"; shift 2 ;;
        --config) config="$2"; shift 2 ;;
        --write-out|--header|--data-binary) shift 2 ;;
        --silent|--show-error) shift ;;
        https://*) url="$1"; shift ;;
        *) shift ;;
    esac
done

code=200
body='{}'
case "$url" in
    */api/auth/login)
        case "$output" in
            *admin*) body='{"accessToken":"SECRET-ADMIN-TOKEN","refreshToken":"x"}' ;;
            *) body='{"accessToken":"SECRET-REDEEMER-TOKEN","refreshToken":"x"}' ;;
        esac
        ;;
    */api/admin/access-keys*)
        body='{"items":[{"id":"11111111-1111-1111-1111-111111111111","displayPrefix":"BW-TEST"}]}'
        ;;
    */api/admin/subscription/access-keys)
        [ "$request" = "POST" ] || exit 91
        body='{"id":"11111111-1111-1111-1111-111111111111","plaintextKey":"BW-SECRET-PLAINTEXT","displayPrefix":"BW-TEST","tier":"Beta"}'
        ;;
    */api/admin/subscription/access-keys/*/revoke)
        [ "$request" = "POST" ] || exit 92
        code=204
        printf 'revoked=1\n' > "$FAKE_STATE_FILE"
        ;;
    */api/subscription/access-keys/redeem)
        [ "$request" = "POST" ] || exit 93
        if grep -Fq 'revoked=1' "$FAKE_STATE_FILE"; then
            code=400
            body='{"title":"The access key could not be redeemed."}'
        else
            body='{"entitlementId":"22222222-2222-2222-2222-222222222222","tier":"Beta","endsAtUtc":null}'
        fi
        ;;
esac

if [ -n "$output" ] && [ "$output" != "/dev/null" ]; then
    printf '%s' "$body" > "$output"
fi
printf '%s' "$code"
EOF
chmod 700 "$fake_bin/curl"

admin_password="$temp_dir/admin-password"
redeemer_password="$temp_dir/redeemer-password"
printf '%s\n' 'AdminSmoke!123456' > "$admin_password"
printf '%s\n' 'RedeemerSmoke!123456' > "$redeemer_password"
chmod 600 "$admin_password" "$redeemer_password"

run_smoke()
{
    env PATH="$fake_bin:$PATH" \
        FAKE_CURL_LOG="$curl_log" \
        FAKE_STATE_FILE="$state_file" \
        BILLWATCH_ACCESS_KEY_SMOKE_ALLOW_MUTATIONS=true \
        BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_EMAIL='owner@example.test' \
        BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_PASSWORD_FILE="$admin_password" \
        BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_EMAIL='tester@example.test' \
        BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_PASSWORD_FILE="$redeemer_password" \
        sh "$smoke_script" 'https://api.example.test'
}

: > "$curl_log"
run_smoke > "$temp_dir/out"
grep -Fq 'BillWatch access-key lifecycle smoke passed.' "$temp_dir/out" ||
    fail "happy-path lifecycle did not complete."
grep -Fq 'PASS plaintext access key is one-time only' "$temp_dir/out" ||
    fail "plaintext one-time boundary was not checked."
grep -Fq 'PASS revoked access key rejected' "$temp_dir/out" ||
    fail "post-revocation rejection was not checked."

for secret in 'AdminSmoke!123456' 'RedeemerSmoke!123456' 'SECRET-ADMIN-TOKEN' 'SECRET-REDEEMER-TOKEN' 'BW-SECRET-PLAINTEXT'; do
    if grep -Fq "$secret" "$curl_log"; then
        fail "secret appeared in curl process arguments: $secret"
    fi
done

if env PATH="$fake_bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    FAKE_STATE_FILE="$state_file" \
    BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_EMAIL='owner@example.test' \
    BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_PASSWORD_FILE="$admin_password" \
    BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_EMAIL='tester@example.test' \
    BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_PASSWORD_FILE="$redeemer_password" \
    sh "$smoke_script" 'https://api.example.test' >/dev/null 2>&1; then
    fail "lifecycle mutations ran without explicit opt-in."
fi

chmod 644 "$redeemer_password"
if run_smoke >/dev/null 2>&1; then
    fail "lifecycle smoke accepted an insecure redeemer password file."
fi
chmod 600 "$redeemer_password"

if env PATH="$fake_bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    FAKE_STATE_FILE="$state_file" \
    BILLWATCH_ACCESS_KEY_SMOKE_ALLOW_MUTATIONS=true \
    BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_EMAIL='same@example.test' \
    BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_PASSWORD_FILE="$admin_password" \
    BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_EMAIL='same@example.test' \
    BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_PASSWORD_FILE="$redeemer_password" \
    sh "$smoke_script" 'https://api.example.test' >/dev/null 2>&1; then
    fail "lifecycle smoke accepted the same account for admin and redeemer."
fi

printf '%s\n' 'Access-key lifecycle smoke harness tests passed.'
