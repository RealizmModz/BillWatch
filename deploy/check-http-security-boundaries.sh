#!/bin/sh

set -eu

api_base_url=${1:-}
web_base_url=${2:-}
allow_insecure=${BILLWATCH_HTTP_SECURITY_ALLOW_INSECURE:-false}

temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "HTTP security boundary check failed: $1" >&2
    exit 1
}

phase()
{
    printf '%s\n' "HTTP security boundary: $1"
}

case "$api_base_url" in
    https://*) ;;
    *) fail "API base URL must use HTTPS." ;;
esac

case "$web_base_url" in
    https://*) ;;
    *) fail "Web base URL must use HTTPS." ;;
esac

case "$allow_insecure" in
    true|false) ;;
    *) fail "BILLWATCH_HTTP_SECURITY_ALLOW_INSECURE must be true or false." ;;
esac

curl_request()
{
    if [ "$allow_insecure" = true ]; then
        curl --insecure "$@"
    else
        curl "$@"
    fi
}

assert_header_contains()
{
    headers=$1
    header_name=$2
    expected=$3

    grep -i "^${header_name}:" "$headers" |
        grep -iF "$expected" >/dev/null ||
        fail "${header_name} did not contain required value '${expected}'."
}

assert_header_absent()
{
    headers=$1
    header_name=$2

    if grep -i "^${header_name}:" "$headers" >/dev/null; then
        fail "${header_name} must not be exposed at the public edge."
    fi
}

api_headers="$temp_dir/api.headers"
api_body="$temp_dir/api.body"

api_status=$(curl_request \
    --silent \
    --show-error \
    --dump-header "$api_headers" \
    --output "$api_body" \
    --write-out '%{http_code}' \
    "$api_base_url/api/account/export")

[ "$api_status" = 401 ] ||
    fail "anonymous protected API probe returned HTTP ${api_status}, expected 401."

assert_header_absent "$api_headers" 'Server'
assert_header_contains "$api_headers" 'Strict-Transport-Security' 'max-age='
assert_header_contains "$api_headers" 'Cache-Control' 'no-store'
assert_header_contains "$api_headers" 'Pragma' 'no-cache'
assert_header_contains "$api_headers" 'X-Content-Type-Options' 'nosniff'
assert_header_contains "$api_headers" 'X-Frame-Options' 'DENY'
assert_header_contains "$api_headers" 'Referrer-Policy' 'no-referrer'
phase 'protected API headers passed.'

register_headers="$temp_dir/register-get.headers"
register_page="$temp_dir/register.html"
cookie_jar="$temp_dir/cookies.txt"

register_get_status=$(curl_request \
    --silent \
    --show-error \
    --cookie-jar "$cookie_jar" \
    --dump-header "$register_headers" \
    --output "$register_page" \
    --write-out '%{http_code}' \
    "$web_base_url/register")

[ "$register_get_status" = 200 ] ||
    fail "Web registration page returned HTTP ${register_get_status}, expected 200."

assert_header_absent "$register_headers" 'Server'
assert_header_contains "$register_headers" 'Strict-Transport-Security' 'max-age='
assert_header_contains "$register_headers" 'X-Content-Type-Options' 'nosniff'
assert_header_contains "$register_headers" 'X-Frame-Options' 'DENY'
assert_header_contains "$register_headers" 'Referrer-Policy' 'no-referrer'
assert_header_contains "$register_headers" 'Content-Security-Policy' "frame-ancestors 'none'"
phase 'public Web headers passed.'

antiforgery_token=$(grep '__RequestVerificationToken' "$register_page" |
    sed -n 's/.*value="\([^"]*\)".*/\1/p' |
    head -n 1)

[ -n "$antiforgery_token" ] ||
    fail "registration page did not emit an antiforgery request token."
phase 'antiforgery issuance passed.'

logout_headers="$temp_dir/logout.headers"
logout_body="$temp_dir/logout.body"

logout_status=$(curl_request \
    --silent \
    --show-error \
    --cookie "$cookie_jar" \
    --cookie-jar "$cookie_jar" \
    --dump-header "$logout_headers" \
    --output "$logout_body" \
    --write-out '%{http_code}' \
    --request POST \
    --data-urlencode "__RequestVerificationToken=$antiforgery_token" \
    "$web_base_url/auth/logout")

case "$logout_status" in
    302|303) ;;
    *) fail "antiforgery-protected Web logout probe returned HTTP ${logout_status}, expected redirect." ;;
esac

assert_header_absent "$logout_headers" 'Server'
assert_header_contains "$logout_headers" 'Cache-Control' 'no-store'
phase 'antiforgery-protected logout passed.'

printf '%s\n' 'HTTP security boundary checks passed.'
