#!/bin/sh

set -eu

api_base_url="${1:-}"
email="${BILLWATCH_SMOKE_EMAIL:-}"

fail()
{
    echo "$1" >&2
    exit "${2:-1}"
}

if [ -z "$api_base_url" ]; then
    fail "Usage: $0 <https-api-base-url>" 64
fi

case "$api_base_url" in
    https://*) ;;
    *) fail "The smoke-test API base URL must use HTTPS." 64 ;;
esac

api_base_url="${api_base_url%/}"

if [ -z "$email" ]; then
    printf 'BillWatch Owner/Admin email: ' >&2
    IFS= read -r email
fi

if [ -z "$email" ]; then
    fail "An account email is required." 64
fi

if [ ! -t 0 ]; then
    fail "Interactive terminal input is required for the password." 64
fi

printf 'BillWatch password: ' >&2
stty -echo
trap 'stty echo 2>/dev/null || true' EXIT HUP INT TERM
IFS= read -r password
stty echo
trap - EXIT HUP INT TERM
printf '\n' >&2

if [ -z "$password" ]; then
    fail "A password is required." 64
fi

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"

cleanup()
{
    rm -rf "$work_directory"
}

trap cleanup EXIT HUP INT TERM

login_payload="$work_directory/login.json"
login_response="$work_directory/login-response.json"
auth_config="$work_directory/auth.curl"

printf '{"email":"%s","password":"%s"}' \
    "$(printf '%s' "$email" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
    "$(printf '%s' "$password" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
    > "$login_payload"
chmod 600 "$login_payload"
unset password

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
    fail "Authentication failed with HTTP $http_code." 69
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

code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --write-out '%{http_code}' \
        --config "$auth_config" \
        "$api_base_url/api/admin/access-keys?skip=0&take=1"
)"

if [ "$code" != "200" ]; then
    fail "Admin authorization probe failed: expected HTTP 200, received $code." 69
fi

echo "BillWatch Owner/Admin authorization smoke test passed."
