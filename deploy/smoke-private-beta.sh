#!/bin/sh

set -eu

api_base_url="${1:-}"
web_base_url="${2:-}"
email="${BILLWATCH_SMOKE_EMAIL:-}"
password_file="${BILLWATCH_SMOKE_PASSWORD_FILE:-}"
admin_expectation="${BILLWATCH_SMOKE_ADMIN_EXPECTATION:-skip}"
allow_mutations="${BILLWATCH_SMOKE_ALLOW_MUTATIONS:-false}"
alert_read_id="${BILLWATCH_SMOKE_ALERT_READ_ID:-}"
alert_dismiss_id="${BILLWATCH_SMOKE_ALERT_DISMISS_ID:-}"
foreign_bill_stream_id="${BILLWATCH_SMOKE_FOREIGN_BILL_STREAM_ID:-}"
foreign_statement_upload_id="${BILLWATCH_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID:-}"

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

case "$admin_expectation" in
    skip|allow|deny) ;;
    *) fail "BILLWATCH_SMOKE_ADMIN_EXPECTATION must be skip, allow, or deny." 64 ;;
esac

case "$allow_mutations" in
    true|false) ;;
    *) fail "BILLWATCH_SMOKE_ALLOW_MUTATIONS must be true or false." 64 ;;
esac

if [ -z "$api_base_url" ] || [ -z "$web_base_url" ]; then
    fail "Usage: $0 <https-api-base-url> <https-web-base-url>" 64
fi

require_https_url "$api_base_url" "The smoke-test API base URL"
require_https_url "$web_base_url" "The smoke-test Web base URL"
api_base_url="${api_base_url%/}"
web_base_url="${web_base_url%/}"

if [ -z "$email" ]; then
    if [ ! -t 0 ]; then
        fail "BILLWATCH_SMOKE_EMAIL is required for non-interactive smoke tests." 64
    fi

    printf 'BillWatch smoke-test account email: ' >&2
    IFS= read -r email
fi

if [ -z "$email" ]; then
    fail "An account email is required." 64
fi

if [ -n "$password_file" ]; then
    [ -f "$password_file" ] ||
        fail "BILLWATCH_SMOKE_PASSWORD_FILE must reference a regular file." 64

    if [ -L "$password_file" ]; then
        fail "BILLWATCH_SMOKE_PASSWORD_FILE must not be a symbolic link." 64
    fi

    password_mode="$(stat -c '%a' "$password_file" 2>/dev/null || true)"
    [ "$password_mode" = "600" ] ||
        fail "BILLWATCH_SMOKE_PASSWORD_FILE must have mode 600." 64

    IFS= read -r password < "$password_file" || true
else
    if [ ! -t 0 ]; then
        fail "BILLWATCH_SMOKE_PASSWORD_FILE is required for non-interactive smoke tests." 64
    fi

    printf 'BillWatch password: ' >&2
    stty -echo
    trap 'stty echo 2>/dev/null || true' EXIT HUP INT TERM
    IFS= read -r password
    stty echo
    trap - EXIT HUP INT TERM
    printf '\n' >&2
fi

if [ -z "${password:-}" ]; then
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
refresh_payload="$work_directory/refresh.json"
refresh_response="$work_directory/refresh-response.json"
auth_config="$work_directory/auth.curl"
export_response="$work_directory/account-export.json"

json_escape()
{
    printf '%s' "$1" |
        sed 's/\\/\\\\/g; s/"/\\"/g; s/	/\\t/g'
}

printf '{"email":"%s","password":"%s"}' \
    "$(json_escape "$email")" \
    "$(json_escape "$password")" \
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

[ "$http_code" = "200" ] ||
    fail "Authentication smoke test failed with HTTP $http_code." 69

access_token="$(
    sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$login_response"
)"
refresh_token="$(
    sed -n 's/.*"refreshToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$login_response"
)"
rm -f "$login_response"

[ -n "$access_token" ] ||
    fail "Authentication response did not contain an access token." 69
[ -n "$refresh_token" ] ||
    fail "Authentication response did not contain a refresh token." 69

printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

printf '{"refreshToken":"%s"}' \
    "$(json_escape "$refresh_token")" \
    > "$refresh_payload"
chmod 600 "$refresh_payload"
unset refresh_token

refresh_code="$(
    curl \
        --silent \
        --show-error \
        --output "$refresh_response" \
        --write-out '%{http_code}' \
        --request POST \
        --header 'Content-Type: application/json' \
        --data-binary "@$refresh_payload" \
        "$api_base_url/api/auth/refresh"
)"
rm -f "$refresh_payload"

[ "$refresh_code" = "200" ] ||
    fail "Access-token refresh smoke test failed with HTTP $refresh_code." 69

refreshed_access_token="$(
    sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$refresh_response"
)"
rm -f "$refresh_response"

[ -n "$refreshed_access_token" ] ||
    fail "Refresh response did not contain an access token." 69

printf 'header = "Authorization: Bearer %s"\n' "$refreshed_access_token" > "$auth_config"
chmod 600 "$auth_config"
unset refreshed_access_token
printf '%s\n' 'PASS authentication and access-token refresh'

probe_get()
{
    base_url="$1"
    path="$2"
    expected="$3"
    config_file="${4:-}"

    if [ -n "$config_file" ]; then
        code="$(
            curl \
                --silent \
                --show-error \
                --output /dev/null \
                --write-out '%{http_code}' \
                --config "$config_file" \
                "$base_url$path"
        )"
    else
        code="$(
            curl \
                --silent \
                --show-error \
                --output /dev/null \
                --write-out '%{http_code}' \
                "$base_url$path"
        )"
    fi

    [ "$code" = "$expected" ] ||
        fail "Smoke probe failed for $path: expected HTTP $expected, received $code." 69

    printf 'PASS %s (%s)\n' "$path" "$code"
}

for public_path in / /login /register /terms /privacy
do
    probe_get "$web_base_url" "$public_path" 200
done

for protected_path in \
    /api/subscription \
    /api/bank-connections \
    /api/bank-accounts \
    /api/bank-transactions \
    /api/bill-streams \
    /api/alerts
do
    probe_get "$api_base_url" "$protected_path" 200 "$auth_config"
done

export_code="$(
    curl \
        --silent \
        --show-error \
        --output "$export_response" \
        --write-out '%{http_code}' \
        --config "$auth_config" \
        "$api_base_url/api/account/export"
)"

[ "$export_code" = "200" ] ||
    fail "Account export smoke test failed with HTTP $export_code." 69

if grep -Eiq \
    '"(accessToken|refreshToken|protectedAccessToken|encryptedAccessToken|plaidAccessToken|storagePath|storedFilePath|passwordHash|securityStamp)"[[:space:]]*:' \
    "$export_response"; then
    fail "Account export contained a forbidden secret or internal-storage field." 70
fi
rm -f "$export_response"
printf '%s\n' 'PASS account export secret/storage boundary'

case "$admin_expectation" in
    allow)
        probe_get "$api_base_url" "/api/admin/access-keys?skip=0&take=1" 200 "$auth_config"
        ;;
    deny)
        probe_get "$api_base_url" "/api/admin/access-keys?skip=0&take=1" 403 "$auth_config"
        ;;
    skip)
        printf '%s\n' 'SKIP admin authorization expectation (set BILLWATCH_SMOKE_ADMIN_EXPECTATION=allow|deny)'
        ;;
esac

if [ -n "$foreign_bill_stream_id" ]; then
    probe_get \
        "$api_base_url" \
        "/api/bill-streams/$foreign_bill_stream_id" \
        404 \
        "$auth_config"
fi

if [ -n "$foreign_bill_stream_id" ] && [ -n "$foreign_statement_upload_id" ]; then
    probe_get \
        "$api_base_url" \
        "/api/bill-streams/$foreign_bill_stream_id/statement-uploads/$foreign_statement_upload_id" \
        404 \
        "$auth_config"
fi

mutation_probe()
{
    path="$1"

    code="$(
        curl \
            --silent \
            --show-error \
            --output /dev/null \
            --write-out '%{http_code}' \
            --request POST \
            --config "$auth_config" \
            "$api_base_url$path"
    )"

    [ "$code" = "204" ] ||
        fail "Controlled mutation smoke probe failed for $path: expected HTTP 204, received $code." 69

    printf 'PASS controlled mutation %s (%s)\n' "$path" "$code"
}

if [ "$allow_mutations" = "true" ]; then
    if [ -z "$alert_read_id" ] && [ -z "$alert_dismiss_id" ]; then
        fail "Mutation smoke testing was enabled but no controlled alert IDs were supplied." 64
    fi

    if [ -n "$alert_read_id" ]; then
        mutation_probe "/api/alerts/$alert_read_id/read"
    fi

    if [ -n "$alert_dismiss_id" ]; then
        mutation_probe "/api/alerts/$alert_dismiss_id/dismiss"
    fi
else
    if [ -n "$alert_read_id" ] || [ -n "$alert_dismiss_id" ]; then
        fail "Controlled alert IDs were supplied while BILLWATCH_SMOKE_ALLOW_MUTATIONS=false." 64
    fi

    printf '%s\n' 'SKIP mutation probes (safe default)'
fi

printf '%s\n' 'BillWatch private-beta smoke harness passed.'
