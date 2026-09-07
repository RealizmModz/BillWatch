#!/bin/sh

set -eu

umask 077

api_base_url="${1:-}"
admin_email="${BILLWATCH_ADMIN_SMOKE_EMAIL:-}"
admin_password_file="${BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE:-}"
nonstaff_email="${BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL:-}"
nonstaff_password_file="${BILLWATCH_ADMIN_SMOKE_NONSTAFF_PASSWORD_FILE:-}"

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
    [ -n "$path" ] || fail "$label is required." 64
    [ -f "$path" ] || fail "$label must reference a regular file." 64
    [ ! -L "$path" ] || fail "$label must not be a symbolic link." 64
    mode="$(stat -c '%a' "$path" 2>/dev/null || true)"
    [ "$mode" = "600" ] || fail "$label must have mode 600." 64
}

json_escape()
{
    printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

login()
{
    email="$1"
    password_file="$2"
    token_file="$3"
    file_key="$4"
    label="$5"

    IFS= read -r password < "$password_file" || true
    [ -n "${password:-}" ] || fail "$label password file is empty." 64

    payload="$work_directory/${file_key}-login.json"
    response="$work_directory/${file_key}-login-response.json"
    auth_config="$work_directory/${file_key}-auth.curl"

    printf '{"email":"%s","password":"%s"}' \
        "$(json_escape "$email")" \
        "$(json_escape "$password")" > "$payload"
    chmod 600 "$payload"
    unset password

    code="$(curl --silent --show-error --output "$response" --write-out '%{http_code}' \
        --request POST --header 'Content-Type: application/json' --data-binary "@$payload" \
        "$api_base_url/api/auth/login")"
    rm -f "$payload"
    [ "$code" = "200" ] || fail "$label authentication failed with HTTP $code." 69

    access_token="$(sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$response" | head -n 1)"
    rm -f "$response"
    [ -n "$access_token" ] || fail "$label authentication response did not contain an access token." 69

    printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
    chmod 600 "$auth_config"
    unset access_token
    printf '%s\n' "$auth_config" > "$token_file"
}

[ -n "$api_base_url" ] || fail "Usage: $0 <https-api-base-url>" 64
require_https_url "$api_base_url" "The admin smoke-test API base URL"
api_base_url="${api_base_url%/}"

[ -n "$admin_email" ] || fail "BILLWATCH_ADMIN_SMOKE_EMAIL is required." 64
[ -n "$nonstaff_email" ] || fail "BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL is required." 64
[ "$admin_email" != "$nonstaff_email" ] || fail "Admin and non-staff smoke accounts must be different." 64
require_secret_file "$admin_password_file" "BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE"
require_secret_file "$nonstaff_password_file" "BILLWATCH_ADMIN_SMOKE_NONSTAFF_PASSWORD_FILE"

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"
trap 'rm -rf "$work_directory"' EXIT HUP INT TERM

admin_auth_pointer="$work_directory/admin-auth.path"
nonstaff_auth_pointer="$work_directory/nonstaff-auth.path"
login "$admin_email" "$admin_password_file" "$admin_auth_pointer" "admin" "Owner/Admin"
login "$nonstaff_email" "$nonstaff_password_file" "$nonstaff_auth_pointer" "nonstaff" "Non-staff"
admin_auth_config="$(cat "$admin_auth_pointer")"
nonstaff_auth_config="$(cat "$nonstaff_auth_pointer")"

admin_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --config "$admin_auth_config" "$api_base_url/api/admin/access-keys?skip=0&take=1")"
[ "$admin_code" = "200" ] || fail "Owner/Admin authorization probe failed: expected HTTP 200, received $admin_code." 69

nonstaff_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --config "$nonstaff_auth_config" "$api_base_url/api/admin/access-keys?skip=0&take=1")"
[ "$nonstaff_code" = "403" ] || fail "Non-staff authorization probe failed: expected HTTP 403, received $nonstaff_code." 69

printf '%s\n' 'BillWatch Owner/Admin authorization and non-staff denial smoke test passed.'
