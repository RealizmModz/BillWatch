#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Web/BFF smoke test failed: $1" >&2
    exit 1
}

smoke_script="$root_dir/deploy/smoke-web-bff.sh"
[ -f "$smoke_script" ] || fail "Web/BFF smoke harness is missing."
sh -n "$smoke_script" || fail "Web/BFF smoke harness has invalid POSIX shell syntax."

grep -Fq 'BILLWATCH_WEB_SMOKE_PASSWORD_FILE' "$smoke_script" ||
    fail "Web/BFF smoke harness must support protected password-file input."
grep -Fq '/bff/antiforgery' "$smoke_script" ||
    fail "Web/BFF smoke harness must verify authenticated antiforgery token issuance."
grep -Fq '/auth/logout' "$smoke_script" ||
    fail "Web/BFF smoke harness must prove logout invalidates the cookie session."
grep -Fq 'BFF account export contained a forbidden secret or internal-storage field.' "$smoke_script" ||
    fail "Web/BFF smoke harness must inspect account export boundaries."

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
curl_log="$temp_dir/curl.log"
logged_out="$temp_dir/logged-out"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu

output=""
headers=""
request="GET"
url=""
has_two_factor=false

printf '%s\n' "$*" >> "$FAKE_CURL_LOG"

while [ "$#" -gt 0 ]
do
    case "$1" in
        --output)
            output="$2"
            shift 2
            ;;
        --dump-header)
            headers="$2"
            shift 2
            ;;
        --request)
            request="$2"
            shift 2
            ;;
        --data-urlencode)
            case "$2" in
                twoFactor=true) has_two_factor=true ;;
            esac
            shift 2
            ;;
        --write-out|--cookie|--cookie-jar|--header)
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
location=''

case "$url" in
    */auth/login)
        [ "$request" = "POST" ] || exit 91
        code=302
        if [ "${FAKE_REQUIRE_2FA:-false}" = "true" ] && [ "$has_two_factor" = "false" ]; then
            location='/login?twoFactor=true'
        else
            location='/app'
        fi
        ;;
    */login|*/login\?twoFactor=true)
        body='<form><input type="hidden" name="__RequestVerificationToken" value="FAKE-CSRF-TOKEN" /></form>'
        ;;
    */app)
        code=200
        ;;
    */bff/account/export)
        if [ "${FAKE_EXPORT_SECRET:-false}" = "true" ]; then
            body='{"protectedAccessToken":"must-never-export"}'
        else
            body='{"email":"web-smoke@example.test","billStreams":[]}'
        fi
        ;;
    */bff/antiforgery)
        body='{"requestToken":"FAKE-BFF-CSRF-TOKEN"}'
        ;;
    */auth/logout)
        [ "$request" = "POST" ] || exit 92
        code=302
        location='/'
        : > "$FAKE_LOGGED_OUT"
        ;;
    */bff/subscription)
        if [ -f "$FAKE_LOGGED_OUT" ]; then
            code=302
            location='/login'
        fi
        ;;
    */bff/bill-streams/*/statement-uploads/*)
        code=404
        ;;
    */bff/bill-streams/*)
        code=404
        ;;
esac

if [ -n "$output" ] && [ "$output" != "/dev/null" ]; then
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
chmod 700 "$fake_bin/curl"

password_file="$temp_dir/password"
printf '%s\n' 'WebSmokePassword!123456' > "$password_file"
chmod 600 "$password_file"

two_factor_file="$temp_dir/two-factor"
printf '%s\n' '123456' > "$two_factor_file"
chmod 600 "$two_factor_file"

run_smoke()
{
    rm -f "$logged_out"
    env \
        PATH="$fake_bin:$PATH" \
        FAKE_CURL_LOG="$curl_log" \
        FAKE_LOGGED_OUT="$logged_out" \
        BILLWATCH_WEB_SMOKE_EMAIL='web-smoke@example.test' \
        BILLWATCH_WEB_SMOKE_PASSWORD_FILE="$password_file" \
        "$@" \
        sh "$smoke_script" \
            'https://web.example.test'
}

: > "$curl_log"
run_smoke \
    BILLWATCH_WEB_SMOKE_FOREIGN_BILL_STREAM_ID='11111111-1111-1111-1111-111111111111' \
    BILLWATCH_WEB_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID='22222222-2222-2222-2222-222222222222' \
    > "$temp_dir/safe.out"

grep -Fq 'PASS Web form login and encrypted cookie session' "$temp_dir/safe.out" ||
    fail "Web login path did not complete."
grep -Fq 'PASS authenticated BFF antiforgery token issuance' "$temp_dir/safe.out" ||
    fail "BFF antiforgery verification did not run."
grep -Fq 'PASS antiforgery-protected logout invalidated the Web session' "$temp_dir/safe.out" ||
    fail "logout invalidation proof did not run."
grep -Fq 'BillWatch authenticated Web/BFF smoke harness passed.' "$temp_dir/safe.out" ||
    fail "safe Web/BFF smoke path did not complete."

for secret in \
    'WebSmokePassword!123456' \
    'FAKE-CSRF-TOKEN' \
    'FAKE-BFF-CSRF-TOKEN'
do
    if grep -Fq "$secret" "$curl_log"; then
        fail "secret or antiforgery token appeared in curl process arguments: $secret"
    fi
done

: > "$curl_log"
run_smoke \
    FAKE_REQUIRE_2FA=true \
    BILLWATCH_WEB_SMOKE_TWO_FACTOR_CODE_FILE="$two_factor_file" \
    > "$temp_dir/two-factor.out"
grep -Fq 'BillWatch authenticated Web/BFF smoke harness passed.' "$temp_dir/two-factor.out" ||
    fail "two-factor Web/BFF smoke path did not complete."
if grep -Fq '123456' "$curl_log"; then
    fail "authenticator code appeared in curl process arguments."
fi

if run_smoke FAKE_REQUIRE_2FA=true > /dev/null 2>&1; then
    fail "two-factor-required login succeeded without a supplied second factor."
fi

if run_smoke FAKE_EXPORT_SECRET=true > /dev/null 2>&1; then
    fail "Web/BFF smoke harness accepted an export containing a protected credential field."
fi

chmod 644 "$password_file"
if run_smoke > /dev/null 2>&1; then
    fail "Web/BFF smoke harness accepted an insecure password-file mode."
fi
chmod 600 "$password_file"

if env \
    PATH="$fake_bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    FAKE_LOGGED_OUT="$logged_out" \
    BILLWATCH_WEB_SMOKE_EMAIL='web-smoke@example.test' \
    BILLWATCH_WEB_SMOKE_PASSWORD_FILE="$password_file" \
    sh "$smoke_script" \
        'http://web.example.test' \
        > /dev/null 2>&1; then
    fail "Web/BFF smoke harness accepted an HTTP Web URL."
fi

printf '%s\n' 'Authenticated Web/BFF smoke harness tests passed.'
