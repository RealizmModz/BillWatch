#!/bin/sh

set -eu

api_base_url="${1:-}"
email="${BILLWATCH_PLAID_SMOKE_EMAIL:-}"
password_file="${BILLWATCH_PLAID_SMOKE_PASSWORD_FILE:-}"
connection_id="${BILLWATCH_PLAID_SMOKE_CONNECTION_ID:-}"
foreign_connection_id="${BILLWATCH_PLAID_SMOKE_FOREIGN_CONNECTION_ID:-}"
allow_disconnect="${BILLWATCH_PLAID_SMOKE_ALLOW_DISCONNECT:-false}"
disconnect_connection_id="${BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID:-}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

require_https_url()
{
    value="$1"
    label="$2"

    case "$value" in
        https://*) ;;
        *) fail "$label must use HTTPS." 64 ;;
    esac

    case "$value" in
        *[[:space:]]*) fail "$label must not contain whitespace." 64 ;;
    esac
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

require_guid()
{
    value="$1"
    label="$2"

    printf '%s\n' "$value" |
        grep -Eq '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$' ||
        fail "$label must be a GUID." 64
}

if [ -z "$api_base_url" ]; then
    fail "Usage: $0 <https-api-base-url>" 64
fi

require_https_url "$api_base_url" "The Plaid smoke-test API base URL"
api_base_url="${api_base_url%/}"

[ -n "$email" ] || fail "BILLWATCH_PLAID_SMOKE_EMAIL is required." 64
[ -n "$password_file" ] || fail "BILLWATCH_PLAID_SMOKE_PASSWORD_FILE is required." 64
[ -n "$connection_id" ] || fail "BILLWATCH_PLAID_SMOKE_CONNECTION_ID is required." 64

require_secret_file "$password_file" "BILLWATCH_PLAID_SMOKE_PASSWORD_FILE"
require_guid "$connection_id" "BILLWATCH_PLAID_SMOKE_CONNECTION_ID"

if [ -n "$foreign_connection_id" ]; then
    require_guid "$foreign_connection_id" "BILLWATCH_PLAID_SMOKE_FOREIGN_CONNECTION_ID"

    [ "$foreign_connection_id" != "$connection_id" ] ||
        fail "The foreign connection ID must differ from the owned connection ID." 64
fi

case "$allow_disconnect" in
    true|false) ;;
    *) fail "BILLWATCH_PLAID_SMOKE_ALLOW_DISCONNECT must be true or false." 64 ;;
esac

if [ "$allow_disconnect" = "true" ]; then
    [ -n "$disconnect_connection_id" ] ||
        fail "BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID is required when disconnect mutation is enabled." 64
    require_guid "$disconnect_connection_id" "BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID"
else
    [ -z "$disconnect_connection_id" ] ||
        fail "Do not set BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID unless disconnect mutation is explicitly enabled." 64
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
connections_response="$work_directory/connections.json"
update_response="$work_directory/update-link-session.json"
foreign_response="$work_directory/foreign-update.json"
disconnect_probe_response="$work_directory/disconnect-update.json"

password="$(cat "$password_file")"
[ -n "$password" ] || fail "The Plaid smoke-test password file is empty." 64

printf '{"email":"%s","password":"%s"}' \
    "$(printf '%s' "$email" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
    "$(printf '%s' "$password" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
    > "$login_payload"
chmod 600 "$login_payload"
unset password

login_code="$(
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

[ "$login_code" = "200" ] ||
    fail "Plaid lifecycle smoke authentication failed with HTTP $login_code." 69

access_token="$(
    sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$login_response"
)"
rm -f "$login_response"

[ -n "$access_token" ] ||
    fail "Plaid lifecycle smoke authentication did not return an access token." 69

printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

connections_code="$(
    curl \
        --silent \
        --show-error \
        --output "$connections_response" \
        --write-out '%{http_code}' \
        --config "$auth_config" \
        "$api_base_url/api/bank-connections"
)"
[ "$connections_code" = "200" ] ||
    fail "Unable to list owned bank connections; received HTTP $connections_code." 69

if ! grep -Fiq "\"id\":\"$connection_id\"" "$connections_response"; then
    fail "The configured Plaid smoke connection is not owned by the authenticated account." 65
fi

if [ "$allow_disconnect" = "true" ] &&
   ! grep -Fiq "\"id\":\"$disconnect_connection_id\"" "$connections_response"; then
    fail "The configured disposable disconnect connection is not owned by the authenticated account." 65
fi

update_code="$(
    curl \
        --silent \
        --show-error \
        --output "$update_response" \
        --write-out '%{http_code}' \
        --request POST \
        --config "$auth_config" \
        "$api_base_url/api/plaid/connections/$connection_id/update-link-token"
)"
[ "$update_code" = "200" ] ||
    fail "Plaid update-mode Hosted Link creation failed with HTTP $update_code." 69

if grep -Eiq \
    '"(accessToken|refreshToken|linkToken|protectedLinkToken|protectedPlaidAccessToken|plaidAccessToken|storagePath|storedFilePath)"[[:space:]]*:' \
    "$update_response"; then
    fail "Plaid update-mode response exposed a forbidden credential or internal-storage field." 70
fi

session_id="$(
    sed -n 's/.*"sessionId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$update_response"
)"
require_guid "$session_id" "Plaid update-mode session ID"

hosted_link_url="$(
    sed -n 's/.*"hostedLinkUrl"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$update_response"
)"
[ -n "$hosted_link_url" ] ||
    fail "Plaid update-mode response did not contain a Hosted Link URL." 70

case "$hosted_link_url" in
    https://*) ;;
    *) fail "Plaid update-mode response returned a non-HTTPS Hosted Link URL." 70 ;;
esac

hosted_link_host="$(
    printf '%s\n' "$hosted_link_url" |
        sed -n 's#^https://\([^/?#]*\).*#\1#p'
)"

case "$hosted_link_host" in
    plaid.com|*.plaid.com) ;;
    *) fail "Plaid update-mode response returned a Hosted Link URL outside plaid.com." 70 ;;
esac

case "$hosted_link_host" in
    *@*) fail "Plaid update-mode response returned a Hosted Link URL containing user information." 70 ;;
esac

unset hosted_link_url hosted_link_host session_id
rm -f "$update_response"
printf '%s\n' 'PASS Plaid update-mode Hosted Link session boundary'

if [ -n "$foreign_connection_id" ]; then
    foreign_code="$(
        curl \
            --silent \
            --show-error \
            --output "$foreign_response" \
            --write-out '%{http_code}' \
            --request POST \
            --config "$auth_config" \
            "$api_base_url/api/plaid/connections/$foreign_connection_id/update-link-token"
    )"

    [ "$foreign_code" = "404" ] ||
        fail "Cross-user Plaid update-mode probe expected HTTP 404 and received $foreign_code." 70
    rm -f "$foreign_response"
    printf '%s\n' 'PASS Plaid update-mode cross-user isolation (404)'
fi

if [ "$allow_disconnect" = "true" ]; then
    disconnect_code="$(
        curl \
            --silent \
            --show-error \
            --output /dev/null \
            --write-out '%{http_code}' \
            --request DELETE \
            --config "$auth_config" \
            "$api_base_url/api/bank-connections/$disconnect_connection_id"
    )"

    [ "$disconnect_code" = "204" ] ||
        fail "Explicit Plaid disconnect mutation failed with HTTP $disconnect_code." 69

    post_disconnect_update_code="$(
        curl \
            --silent \
            --show-error \
            --output "$disconnect_probe_response" \
            --write-out '%{http_code}' \
            --request POST \
            --config "$auth_config" \
            "$api_base_url/api/plaid/connections/$disconnect_connection_id/update-link-token"
    )"

    [ "$post_disconnect_update_code" = "409" ] ||
        fail "Disconnected Plaid connection remained eligible for update mode (HTTP $post_disconnect_update_code)." 70

    rm -f "$disconnect_probe_response"
    printf '%s\n' 'PASS explicit Plaid disconnect and post-disconnect update-mode rejection (204/409)'
fi

printf '%s\n' 'BillWatch guarded Plaid lifecycle smoke harness passed.'
