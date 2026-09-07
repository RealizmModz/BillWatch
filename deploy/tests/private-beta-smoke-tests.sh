#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Private beta smoke test failed: $1" >&2
    exit 1
}

smoke_script="$root_dir/deploy/smoke-private-beta.sh"
[ -f "$smoke_script" ] || fail "smoke harness is missing."
sh -n "$smoke_script" || fail "smoke harness has invalid POSIX shell syntax."

grep -Fq 'BILLWATCH_SMOKE_PASSWORD_FILE' "$smoke_script" ||
    fail "smoke harness must support protected password-file input."
grep -Fq 'chmod 600 "$auth_config"' "$smoke_script" ||
    fail "smoke harness must protect bearer-token curl configuration."
grep -Fq 'BILLWATCH_SMOKE_ALLOW_MUTATIONS:-false' "$smoke_script" ||
    fail "smoke harness mutations must be disabled by default."
grep -Fq 'Account export contained a forbidden secret or internal-storage field.' "$smoke_script" ||
    fail "smoke harness must inspect account export for forbidden fields."

if grep -E -- '--header[ =]+["'\'']?Authorization: Bearer' "$smoke_script" >/dev/null; then
    fail "smoke harness must not place bearer tokens directly in curl argv."
fi

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
curl_log="$temp_dir/curl.log"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu

output=""
url=""
request="GET"

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
        body='{"accessToken":"SECRET-ACCESS-TOKEN","refreshToken":"SECRET-REFRESH-TOKEN"}'
        ;;
    */api/auth/refresh)
        body='{"accessToken":"SECRET-REFRESHED-ACCESS-TOKEN","refreshToken":"SECRET-ROTATED-REFRESH-TOKEN"}'
        ;;
    */api/account/export)
        if [ "${FAKE_EXPORT_SECRET:-false}" = "true" ]; then
            body='{"email":"smoke@example.test","protectedAccessToken":"must-never-export"}'
        else
            body='{"email":"smoke@example.test","billStreams":[]}'
        fi
        ;;
    */api/admin/access-keys*)
        code="${FAKE_ADMIN_CODE:-200}"
        ;;
    */api/alerts/*/read|*/api/alerts/*/dismiss)
        [ "$request" = "POST" ] || exit 91
        code=204
        ;;
    */api/bill-streams/*/statement-uploads/*)
        code=404
        ;;
    */api/bill-streams/*)
        code=404
        ;;
esac

if [ -n "$output" ] && [ "$output" != "/dev/null" ]; then
    printf '%s' "$body" > "$output"
fi

printf '%s' "$code"
EOF
chmod 700 "$fake_bin/curl"

password_file="$temp_dir/password"
printf '%s\n' 'SmokePassword!123456' > "$password_file"
chmod 600 "$password_file"

run_smoke()
{
    env \
        PATH="$fake_bin:$PATH" \
        FAKE_CURL_LOG="$curl_log" \
        BILLWATCH_SMOKE_EMAIL='smoke@example.test' \
        BILLWATCH_SMOKE_PASSWORD_FILE="$password_file" \
        "$@" \
        sh "$smoke_script" \
            'https://api.example.test' \
            'https://web.example.test'
}

: > "$curl_log"
run_smoke \
    BILLWATCH_SMOKE_ADMIN_EXPECTATION=allow \
    BILLWATCH_SMOKE_FOREIGN_BILL_STREAM_ID='11111111-1111-1111-1111-111111111111' \
    BILLWATCH_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID='22222222-2222-2222-2222-222222222222' \
    > "$temp_dir/safe.out"

grep -Fq 'BillWatch private-beta smoke harness passed.' "$temp_dir/safe.out" ||
    fail "safe smoke path did not complete."
grep -Fq 'PASS authentication and access-token refresh' "$temp_dir/safe.out" ||
    fail "refresh-token verification did not run."
grep -Fq 'PASS account export secret/storage boundary' "$temp_dir/safe.out" ||
    fail "account export verification did not run."
grep -Fq 'SKIP mutation probes (safe default)' "$temp_dir/safe.out" ||
    fail "mutation probes were not safely skipped by default."

for secret in \
    'SmokePassword!123456' \
    'SECRET-ACCESS-TOKEN' \
    'SECRET-REFRESH-TOKEN' \
    'SECRET-REFRESHED-ACCESS-TOKEN'
do
    if grep -Fq "$secret" "$curl_log"; then
        fail "secret appeared in curl process arguments: $secret"
    fi
done

: > "$curl_log"
run_smoke \
    FAKE_ADMIN_CODE=403 \
    BILLWATCH_SMOKE_ADMIN_EXPECTATION=deny \
    > "$temp_dir/deny.out"
grep -Fq '/api/admin/access-keys?skip=0&take=1 (403)' "$temp_dir/deny.out" ||
    fail "non-staff admin-denial expectation was not verified."

if run_smoke \
    BILLWATCH_SMOKE_ALERT_READ_ID='33333333-3333-3333-3333-333333333333' \
    > /dev/null 2>&1; then
    fail "controlled mutation ID was accepted without explicit mutation opt-in."
fi

run_smoke \
    BILLWATCH_SMOKE_ALLOW_MUTATIONS=true \
    BILLWATCH_SMOKE_ALERT_READ_ID='33333333-3333-3333-3333-333333333333' \
    BILLWATCH_SMOKE_ALERT_DISMISS_ID='44444444-4444-4444-4444-444444444444' \
    > "$temp_dir/mutation.out"
grep -Fq 'PASS controlled mutation /api/alerts/33333333-3333-3333-3333-333333333333/read (204)' \
    "$temp_dir/mutation.out" ||
    fail "explicit controlled mark-read mutation did not run."
grep -Fq 'PASS controlled mutation /api/alerts/44444444-4444-4444-4444-444444444444/dismiss (204)' \
    "$temp_dir/mutation.out" ||
    fail "explicit controlled dismiss mutation did not run."

if run_smoke \
    FAKE_EXPORT_SECRET=true \
    > /dev/null 2>&1; then
    fail "smoke harness accepted an export containing a protected credential field."
fi

chmod 644 "$password_file"
if run_smoke > /dev/null 2>&1; then
    fail "smoke harness accepted an insecure password-file mode."
fi
chmod 600 "$password_file"

if env \
    PATH="$fake_bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    BILLWATCH_SMOKE_EMAIL='smoke@example.test' \
    BILLWATCH_SMOKE_PASSWORD_FILE="$password_file" \
    sh "$smoke_script" \
        'http://api.example.test' \
        'https://web.example.test' \
        > /dev/null 2>&1; then
    fail "smoke harness accepted an HTTP API URL."
fi

printf '%s\n' 'Private beta smoke harness tests passed.'
