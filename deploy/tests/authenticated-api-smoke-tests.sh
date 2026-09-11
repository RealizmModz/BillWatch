#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Authenticated API smoke test failed: $1" >&2
    exit 1
}

smoke="$root_dir/deploy/smoke-authenticated-api.sh"
[ -f "$smoke" ] || fail "smoke script is missing."
sh -n "$smoke" || fail "smoke script has invalid POSIX shell syntax."

grep -Fq "Authentication rejected the supplied second factor." "$smoke" ||
    fail "smoke must distinguish rejected second-factor authentication."
grep -Fq "Verify the account credentials and lockout state before retrying." "$smoke" ||
    fail "smoke must distinguish rejected credentials/account state."
grep -Fq 'RequiresTwoFactor' "$smoke" ||
    fail "smoke must recognize the API two-factor-required response."

password_file="$temp_dir/password"
two_factor_file="$temp_dir/two-factor"
recovery_file="$temp_dir/recovery"
printf '%s' 'Password!123' > "$password_file"
printf '%s' '123456' > "$two_factor_file"
printf '%s' 'recovery-code' > "$recovery_file"
chmod 600 "$password_file" "$two_factor_file" "$recovery_file"

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_CURL_COUNT:?}"
: "${BILLWATCH_TEST_PAYLOAD_COPY:?}"
count=0
[ ! -f "$BILLWATCH_TEST_CURL_COUNT" ] || count=$(cat "$BILLWATCH_TEST_CURL_COUNT")
count=$((count + 1))
printf '%s\n' "$count" > "$BILLWATCH_TEST_CURL_COUNT"

output=''
data_file=''
config_file=''
while [ "$#" -gt 0 ]; do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        --data-binary)
            case "$2" in
                @*) data_file=${2#@} ;;
            esac
            shift 2
            ;;
        --config) config_file="$2"; shift 2 ;;
        *) shift ;;
    esac
done

case "$count" in
    1)
        [ -n "$data_file" ] || exit 3
        cp "$data_file" "$BILLWATCH_TEST_PAYLOAD_COPY"
        login_status="${BILLWATCH_TEST_LOGIN_STATUS:-200}"
        login_body="${BILLWATCH_TEST_LOGIN_BODY:-{\"accessToken\":\"secret-token\"}}"
        [ -n "$output" ] && printf '%s' "$login_body" > "$output"
        printf '%s' "$login_status"
        ;;
    2|3|4|5|6|7)
        [ -n "$config_file" ] || exit 4
        grep -q 'Authorization: Bearer secret-token' "$config_file" || exit 5
        printf '200'
        ;;
    *) exit 2 ;;
esac
EOF
chmod 700 "$fake_bin/curl"

run_smoke()
{
    : > "$temp_dir/curl.count"
    rm -f "$temp_dir/payload.json"
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_CURL_COUNT="$temp_dir/curl.count" \
    BILLWATCH_TEST_PAYLOAD_COPY="$temp_dir/payload.json" \
    BILLWATCH_TEST_LOGIN_STATUS="${BILLWATCH_TEST_LOGIN_STATUS:-200}" \
    BILLWATCH_TEST_LOGIN_BODY="${BILLWATCH_TEST_LOGIN_BODY:-{\"accessToken\":\"secret-token\"}}" \
    BILLWATCH_SMOKE_EMAIL='owner@example.test' \
    BILLWATCH_SMOKE_PASSWORD_FILE="$password_file" \
    BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE="${BILLWATCH_TEST_TWO_FACTOR_FILE:-}" \
    BILLWATCH_SMOKE_RECOVERY_CODE_FILE="${BILLWATCH_TEST_RECOVERY_FILE:-}" \
    sh "$smoke" https://api.example.test
}

BILLWATCH_TEST_LOGIN_STATUS=200
BILLWATCH_TEST_LOGIN_BODY='{"accessToken":"secret-token"}'
BILLWATCH_TEST_TWO_FACTOR_FILE="$two_factor_file"
BILLWATCH_TEST_RECOVERY_FILE=''
run_smoke > "$temp_dir/authenticator.out"
grep -q 'authenticated API smoke test passed' "$temp_dir/authenticator.out" || fail "authenticator-code run did not report success."
grep -q '"twoFactorCode":"123456"' "$temp_dir/payload.json" || fail "authenticator code was not forwarded in the login payload."
if grep -q 'twoFactorRecoveryCode' "$temp_dir/payload.json"; then
    fail "authenticator-code run unexpectedly forwarded a recovery code field."
fi
[ "$(cat "$temp_dir/curl.count")" = '7' ] || fail "expected one login and six authenticated probes."

BILLWATCH_TEST_TWO_FACTOR_FILE=''
BILLWATCH_TEST_RECOVERY_FILE="$recovery_file"
run_smoke > "$temp_dir/recovery.out"
grep -q '"twoFactorRecoveryCode":"recovery-code"' "$temp_dir/payload.json" || fail "recovery code was not forwarded in the login payload."
if grep -q '"twoFactorCode"' "$temp_dir/payload.json"; then
    fail "recovery-code run unexpectedly forwarded an authenticator field."
fi

BILLWATCH_TEST_LOGIN_STATUS=401
BILLWATCH_TEST_LOGIN_BODY='{"detail":"RequiresTwoFactor"}'
BILLWATCH_TEST_TWO_FACTOR_FILE=''
BILLWATCH_TEST_RECOVERY_FILE=''
if run_smoke > /dev/null 2> "$temp_dir/requires-two-factor.err"; then
    fail "smoke accepted a two-factor-required 401 without a second factor."
fi
grep -Fq 'The account requires two-factor authentication.' "$temp_dir/requires-two-factor.err" ||
    fail "two-factor-required response did not produce actionable diagnostics."

BILLWATCH_TEST_LOGIN_STATUS=401
BILLWATCH_TEST_LOGIN_BODY='{"title":"Unauthorized"}'
BILLWATCH_TEST_TWO_FACTOR_FILE=''
BILLWATCH_TEST_RECOVERY_FILE=''
if run_smoke > /dev/null 2> "$temp_dir/credentials-rejected.err"; then
    fail "smoke accepted a rejected password login."
fi
grep -Fq 'Authentication was rejected. Verify the account credentials and lockout state before retrying.' "$temp_dir/credentials-rejected.err" ||
    fail "rejected credentials did not produce actionable diagnostics."

BILLWATCH_TEST_LOGIN_STATUS=401
BILLWATCH_TEST_LOGIN_BODY='{"title":"Unauthorized"}'
BILLWATCH_TEST_TWO_FACTOR_FILE="$two_factor_file"
BILLWATCH_TEST_RECOVERY_FILE=''
if run_smoke > /dev/null 2> "$temp_dir/second-factor-rejected.err"; then
    fail "smoke accepted a rejected second factor."
fi
grep -Fq "Authentication rejected the supplied second factor. Verify the current authenticator or recovery code and the account's sign-in state before retrying." "$temp_dir/second-factor-rejected.err" ||
    fail "rejected second factor did not produce actionable diagnostics."

BILLWATCH_TEST_LOGIN_STATUS=200
BILLWATCH_TEST_LOGIN_BODY='{"accessToken":"secret-token"}'
BILLWATCH_TEST_TWO_FACTOR_FILE="$two_factor_file"
BILLWATCH_TEST_RECOVERY_FILE="$recovery_file"
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted both authenticator and recovery code files at once."
fi

BILLWATCH_TEST_TWO_FACTOR_FILE=''
BILLWATCH_TEST_RECOVERY_FILE=''
chmod 644 "$password_file"
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted a weakly-permissioned password file."
fi
chmod 600 "$password_file"

chmod 644 "$two_factor_file"
BILLWATCH_TEST_TWO_FACTOR_FILE="$two_factor_file"
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted a weakly-permissioned authenticator-code file."
fi
chmod 600 "$two_factor_file"

ln -s "$two_factor_file" "$temp_dir/two-factor-link"
BILLWATCH_TEST_TWO_FACTOR_FILE="$temp_dir/two-factor-link"
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted a symbolic-link authenticator-code file."
fi

if BILLWATCH_SMOKE_EMAIL=owner@example.test \
   BILLWATCH_SMOKE_PASSWORD_FILE="$password_file" \
   sh "$smoke" http://api.example.test >/dev/null 2>&1; then
    fail "smoke accepted a non-HTTPS API URL."
fi

printf '%s\n' 'Authenticated API smoke tests passed.'