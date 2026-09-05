#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Cross-user Web/BFF smoke test failed: $1" >&2
    exit 1
}

source_script="$root_dir/deploy/smoke-web-bff-cross-user.sh"
[ -f "$source_script" ] || fail "cross-user Web/BFF smoke harness is missing."
sh -n "$source_script" || fail "cross-user Web/BFF smoke harness has invalid POSIX shell syntax."

grep -Fq 'BILLWATCH_WEB_OWNERSHIP_FOREIGN_PASSWORD_FILE' "$source_script" ||
    fail "ownership harness must require a protected foreign-account password file."
grep -Fq '/bff/account/export' "$source_script" ||
    fail "ownership harness must derive ownership from the foreign account export."
grep -Fq 'BILLWATCH_WEB_SMOKE_FOREIGN_BILL_STREAM_ID' "$source_script" ||
    fail "ownership harness must pass the proven foreign bill-stream ID to the primary smoke."
grep -Fq 'BILLWATCH_WEB_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID' "$source_script" ||
    fail "ownership harness must pass the proven foreign statement-upload ID to the primary smoke."

fixture_root="$temp_dir/fixture"
mkdir -p "$fixture_root/deploy" "$fixture_root/bin"
cp "$source_script" "$fixture_root/deploy/smoke-web-bff-cross-user.sh"
chmod 700 "$fixture_root/deploy/smoke-web-bff-cross-user.sh"

foreign_stream_id='11111111-1111-4111-8111-111111111111'
foreign_upload_id='22222222-2222-4222-8222-222222222222'
delegated_log="$temp_dir/delegated.log"
curl_log="$temp_dir/curl.log"

cat > "$fixture_root/deploy/smoke-web-bff.sh" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_WEB_SMOKE_EMAIL:?}"
: "${BILLWATCH_WEB_SMOKE_PASSWORD_FILE:?}"
: "${BILLWATCH_WEB_SMOKE_FOREIGN_BILL_STREAM_ID:?}"
: "${BILLWATCH_WEB_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID:?}"
printf '%s|%s|%s\n' \
    "$BILLWATCH_WEB_SMOKE_FOREIGN_BILL_STREAM_ID" \
    "$BILLWATCH_WEB_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID" \
    "$1" > "$FAKE_DELEGATED_LOG"
printf '%s\n' 'BillWatch authenticated Web/BFF smoke harness passed.'
EOF
chmod 700 "$fixture_root/deploy/smoke-web-bff.sh"

cat > "$fixture_root/bin/curl" <<'EOF'
#!/bin/sh
set -eu

output=''
headers=''
request='GET'
url=''
has_two_factor=false
printf '%s\n' "$*" >> "$FAKE_CURL_LOG"

while [ "$#" -gt 0 ]
do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        --dump-header) headers="$2"; shift 2 ;;
        --request) request="$2"; shift 2 ;;
        --data-urlencode)
            case "$2" in twoFactor=true) has_two_factor=true ;; esac
            shift 2
            ;;
        --write-out|--cookie|--cookie-jar|--header) shift 2 ;;
        --silent|--show-error) shift ;;
        https://*) url="$1"; shift ;;
        *) shift ;;
    esac
done

[ -n "$url" ] || exit 90
code=200
body='{}'
location=''

case "$url" in
    */auth/login)
        [ "$request" = 'POST' ] || exit 91
        code=302
        if [ "${FAKE_REQUIRE_2FA:-false}" = 'true' ] && [ "$has_two_factor" = 'false' ]; then
            location='/login?twoFactor=true'
        else
            location='/app'
        fi
        ;;
    */login|*/login\?twoFactor=true)
        body='<form><input type="hidden" name="__RequestVerificationToken" value="FOREIGN-CSRF" /></form>'
        ;;
    */bff/account/export)
        if [ "${FAKE_EXPORT_SECRET:-false}" = 'true' ]; then
            body='{"protectedAccessToken":"never-export","statementUploads":[{"id":"22222222-2222-4222-8222-222222222222","billStreamId":"11111111-1111-4111-8111-111111111111"}]}'
        elif [ "${FAKE_NO_STATEMENT:-false}" = 'true' ]; then
            body='{"statementUploads":[]}'
        else
            body='{"schemaVersion":"1.1","statementUploads":[{"id":"22222222-2222-4222-8222-222222222222","billStreamId":"11111111-1111-4111-8111-111111111111","status":"Completed"}]}'
        fi
        ;;
esac

if [ -n "$output" ] && [ "$output" != '/dev/null' ]; then
    printf '%s' "$body" > "$output"
fi
if [ -n "$headers" ]; then
    : > "$headers"
    if [ -n "$location" ]; then
        printf 'Location: %s\r\n' "$location" > "$headers"
    fi
fi
printf '%s' "$code"
EOF
chmod 700 "$fixture_root/bin/curl"

primary_password="$temp_dir/primary-password"
foreign_password="$temp_dir/foreign-password"
foreign_two_factor="$temp_dir/foreign-two-factor"
printf '%s\n' 'PrimaryPassword!123456' > "$primary_password"
printf '%s\n' 'ForeignPassword!123456' > "$foreign_password"
printf '%s\n' '123456' > "$foreign_two_factor"
chmod 600 "$primary_password" "$foreign_password" "$foreign_two_factor"

run_ownership()
{
    rm -f "$delegated_log"
    env \
        PATH="$fixture_root/bin:$PATH" \
        FAKE_CURL_LOG="$curl_log" \
        FAKE_DELEGATED_LOG="$delegated_log" \
        BILLWATCH_WEB_SMOKE_EMAIL="${PRIMARY_EMAIL_VALUE:-primary@example.test}" \
        BILLWATCH_WEB_SMOKE_PASSWORD_FILE="$primary_password" \
        BILLWATCH_WEB_OWNERSHIP_FOREIGN_EMAIL="${FOREIGN_EMAIL_VALUE:-foreign@example.test}" \
        BILLWATCH_WEB_OWNERSHIP_FOREIGN_PASSWORD_FILE="$foreign_password" \
        "$@" \
        sh "$fixture_root/deploy/smoke-web-bff-cross-user.sh" 'https://web.example.test'
}

: > "$curl_log"
run_ownership > "$temp_dir/happy.out"
grep -Fq 'PASS foreign account authenticated and supplied an objectively owned statement fixture' "$temp_dir/happy.out" ||
    fail "foreign ownership fixture was not proven."
grep -Fq 'PASS primary Web/BFF identity received 404 for foreign bill-stream and statement-upload resources' "$temp_dir/happy.out" ||
    fail "primary isolation proof did not complete."
grep -Fq 'BillWatch cross-user Web/BFF ownership smoke harness passed.' "$temp_dir/happy.out" ||
    fail "happy ownership path did not complete."
[ "$(cat "$delegated_log")" = "$foreign_stream_id|$foreign_upload_id|https://web.example.test" ] ||
    fail "ownership IDs were not delegated exactly to the primary Web/BFF smoke."

for secret in 'ForeignPassword!123456' 'FOREIGN-CSRF'; do
    if grep -Fq "$secret" "$curl_log"; then
        fail "foreign secret or antiforgery token appeared in curl process arguments: $secret"
    fi
done

if PRIMARY_EMAIL_VALUE='same@example.test' FOREIGN_EMAIL_VALUE='SAME@example.test' run_ownership >/dev/null 2>&1; then
    fail "ownership harness accepted the same identity as primary and foreign accounts."
fi

if run_ownership FAKE_NO_STATEMENT=true >/dev/null 2>&1; then
    fail "ownership harness accepted a foreign account without an objectively owned statement fixture."
fi

if run_ownership FAKE_EXPORT_SECRET=true >/dev/null 2>&1; then
    fail "ownership harness accepted a foreign export containing a protected credential field."
fi

if run_ownership FAKE_REQUIRE_2FA=true >/dev/null 2>&1; then
    fail "ownership harness accepted a foreign account requiring 2FA without a second factor."
fi

: > "$curl_log"
run_ownership \
    FAKE_REQUIRE_2FA=true \
    BILLWATCH_WEB_OWNERSHIP_FOREIGN_TWO_FACTOR_CODE_FILE="$foreign_two_factor" \
    > "$temp_dir/two-factor.out"
grep -Fq 'BillWatch cross-user Web/BFF ownership smoke harness passed.' "$temp_dir/two-factor.out" ||
    fail "foreign two-factor ownership path did not complete."
if grep -Fq '123456' "$curl_log"; then
    fail "foreign authenticator code appeared in curl process arguments."
fi

chmod 644 "$foreign_password"
if run_ownership >/dev/null 2>&1; then
    fail "ownership harness accepted an insecure foreign password-file mode."
fi
chmod 600 "$foreign_password"

if env \
    PATH="$fixture_root/bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    FAKE_DELEGATED_LOG="$delegated_log" \
    BILLWATCH_WEB_SMOKE_EMAIL='primary@example.test' \
    BILLWATCH_WEB_SMOKE_PASSWORD_FILE="$primary_password" \
    BILLWATCH_WEB_OWNERSHIP_FOREIGN_EMAIL='foreign@example.test' \
    BILLWATCH_WEB_OWNERSHIP_FOREIGN_PASSWORD_FILE="$foreign_password" \
    sh "$fixture_root/deploy/smoke-web-bff-cross-user.sh" 'http://web.example.test' \
        >/dev/null 2>&1; then
    fail "ownership harness accepted an HTTP Web URL."
fi

printf '%s\n' 'Cross-user Web/BFF ownership smoke harness tests passed.'
