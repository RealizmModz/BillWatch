#!/bin/sh

set -eu

api_base_url="${1:-}"
email="${BILLWATCH_SMOKE_EMAIL:-}"
password_file="${BILLWATCH_SMOKE_PASSWORD_FILE:-}"
two_factor_code_file="${BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE:-}"
recovery_code_file="${BILLWATCH_SMOKE_RECOVERY_CODE_FILE:-}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

require_secret_file()
{
    path="$1"
    label="$2"

    [ -f "$path" ] || fail "$label must reference a regular file." 64
    [ ! -L "$path" ] || fail "$label must not be a symbolic link." 64

    mode="$(stat -c '%a' "$path" 2>/dev/null || true)"
    [ "$mode" = "600" ] || fail "$label must have mode 600." 64
}

json_escape_file()
{
    sed 's/\\/\\\\/g; s/"/\\"/g' "$1"
}

if [ -z "$api_base_url" ]; then
    fail "Usage: $0 <https-api-base-url>" 64
fi

case "$api_base_url" in
    https://*) ;;
    *) fail "The smoke-test API base URL must use HTTPS." 64 ;;
esac

case "$api_base_url" in
    *[[:space:]]*) fail "The smoke-test API base URL must not contain whitespace." 64 ;;
esac

api_base_url="${api_base_url%/}"

if [ -z "$email" ]; then
    if [ ! -t 0 ]; then
        fail "BILLWATCH_SMOKE_EMAIL is required for non-interactive smoke tests." 64
    fi

    printf 'BillWatch account email: ' >&2
    IFS= read -r email
fi

[ -n "$email" ] || fail "An account email is required." 64

if [ -n "$two_factor_code_file" ] && [ -n "$recovery_code_file" ]; then
    fail "Set only one of BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE or BILLWATCH_SMOKE_RECOVERY_CODE_FILE." 64
fi

if [ -n "$password_file" ]; then
    require_secret_file "$password_file" "BILLWATCH_SMOKE_PASSWORD_FILE"
else
    if [ ! -t 0 ]; then
        fail "BILLWATCH_SMOKE_PASSWORD_FILE is required for non-interactive smoke tests." 64
    fi

    password_file="$(mktemp)"
    chmod 600 "$password_file"
    temporary_password_file=true

    printf 'BillWatch password: ' >&2
    stty -echo
    trap 'stty echo 2>/dev/null || true; rm -f "${password_file:-}"' EXIT HUP INT TERM
    IFS= read -r password
    stty echo
    trap - EXIT HUP INT TERM
    printf '\n' >&2

    [ -n "$password" ] || fail "A password is required." 64
    printf '%s' "$password" > "$password_file"
    unset password
fi

if [ -n "$two_factor_code_file" ]; then
    require_secret_file "$two_factor_code_file" "BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE"
fi

if [ -n "$recovery_code_file" ]; then
    require_secret_file "$recovery_code_file" "BILLWATCH_SMOKE_RECOVERY_CODE_FILE"
fi

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"

cleanup()
{
    rm -rf "$work_directory"

    if [ "${temporary_password_file:-false}" = "true" ]; then
        rm -f "$password_file"
    fi
}

trap cleanup EXIT HUP INT TERM

login_payload="$work_directory/login.json"
login_response="$work_directory/login-response.json"
auth_config="$work_directory/auth.curl"

escaped_email="$(printf '%s' "$email" | sed 's/\\/\\\\/g; s/"/\\"/g')"
escaped_password="$(json_escape_file "$password_file")"

printf '{"email":"%s","password":"%s"' "$escaped_email" "$escaped_password" > "$login_payload"
unset escaped_password

if [ -n "$two_factor_code_file" ]; then
    escaped_two_factor="$(json_escape_file "$two_factor_code_file")"
    printf ',"twoFactorCode":"%s"' "$escaped_two_factor" >> "$login_payload"
    unset escaped_two_factor
elif [ -n "$recovery_code_file" ]; then
    escaped_recovery_code="$(json_escape_file "$recovery_code_file")"
    printf ',"twoFactorRecoveryCode":"%s"' "$escaped_recovery_code" >> "$login_payload"
    unset escaped_recovery_code
fi

printf '}' >> "$login_payload"
chmod 600 "$login_payload"

http_code="$(
    curl \
        --silent \
        --show-error \
        --output "$login_response" \
        --write-out '%{http_code}' \
        --request POST \
        --header 'Content-Type: application/json' \
        --data-binary "@$login_payload" \
        "$api_base_url/api/auth/login"
)"

rm -f "$login_payload"

if [ "$http_code" != "200" ]; then
    if [ "$http_code" = "401" ]; then
        if [ -n "$two_factor_code_file" ] || [ -n "$recovery_code_file" ]; then
            fail "Authentication rejected the supplied second factor. Verify the current authenticator or recovery code and the account's sign-in state before retrying." 69
        fi

        if grep -Eq '"detail"[[:space:]]*:[[:space:]]*"RequiresTwoFactor"' "$login_response"; then
            fail "The account requires two-factor authentication. Supply a current mode-600 authenticator-code file with BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE or a recovery-code file with BILLWATCH_SMOKE_RECOVERY_CODE_FILE." 65
        fi

        fail "Authentication was rejected. Verify the account credentials and lockout state before retrying." 69
    fi

    fail "Authentication smoke test failed with HTTP $http_code." 69
fi

access_token="$(
    sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$login_response"
)"
rm -f "$login_response"

if [ -z "$access_token" ]; then
    fail "Authentication response did not contain an access token." 69
fi

printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

probe()
{
    path="$1"
    expected="$2"

    code="$(
        curl \
            --silent \
            --show-error \
            --output /dev/null \
            --write-out '%{http_code}' \
            --config "$auth_config" \
            "$api_base_url$path"
    )"

    if [ "$code" != "$expected" ]; then
        fail "Authenticated probe failed for $path: expected HTTP $expected, received $code." 69
    fi

    printf 'PASS %s (%s)\n' "$path" "$code"
}

probe "/api/subscription" "200"
probe "/api/bank-connections" "200"
probe "/api/bank-accounts" "200"
probe "/api/bank-transactions" "200"
probe "/api/bill-streams" "200"
probe "/api/alerts" "200"

printf '%s\n' "BillWatch authenticated API smoke test passed."