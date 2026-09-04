#!/bin/sh

set -eu

api_base_url="${1:-}"
allow_mutations="${BILLWATCH_ACCESS_KEY_SMOKE_ALLOW_MUTATIONS:-false}"
admin_email="${BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_EMAIL:-}"
admin_password_file="${BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_PASSWORD_FILE:-}"
redeemer_email="${BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_EMAIL:-}"
redeemer_password_file="${BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_PASSWORD_FILE:-}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

[ "$allow_mutations" = "true" ] ||
    fail "Access-key lifecycle smoke is mutation-bearing and requires BILLWATCH_ACCESS_KEY_SMOKE_ALLOW_MUTATIONS=true." 64

case "$api_base_url" in
    https://*) ;;
    *) fail "Usage: $0 <https-api-base-url>; HTTPS is required." 64 ;;
esac

case "$api_base_url" in
    *[[:space:]]*) fail "The API base URL must not contain whitespace." 64 ;;
esac
api_base_url="${api_base_url%/}"

[ -n "$admin_email" ] || fail "BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_EMAIL is required." 64
[ -n "$redeemer_email" ] || fail "BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_EMAIL is required." 64
[ "$admin_email" != "$redeemer_email" ] || fail "Admin and redeemer accounts must be different." 64

validate_password_file()
{
    file="$1"
    label="$2"

    [ -n "$file" ] || fail "$label is required." 64
    [ -f "$file" ] || fail "$label must reference a regular file." 64
    [ ! -L "$file" ] || fail "$label must not be a symbolic link." 64

    mode="$(stat -c '%a' "$file" 2>/dev/null || true)"
    [ "$mode" = "600" ] || fail "$label must have mode 600." 64
}

validate_password_file "$admin_password_file" "BILLWATCH_ACCESS_KEY_SMOKE_ADMIN_PASSWORD_FILE"
validate_password_file "$redeemer_password_file" "BILLWATCH_ACCESS_KEY_SMOKE_REDEEMER_PASSWORD_FILE"

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"
trap 'rm -rf "$work_directory"' EXIT HUP INT TERM

json_escape()
{
    printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/\\t/g'
}

login()
{
    email="$1"
    password_file="$2"
    auth_config="$3"
    prefix="$4"
    payload="$work_directory/$prefix-login.json"
    response="$work_directory/$prefix-login-response.json"

    IFS= read -r password < "$password_file" || true
    [ -n "${password:-}" ] || fail "Password file for $prefix is empty." 64

    printf '{"email":"%s","password":"%s"}' \
        "$(json_escape "$email")" \
        "$(json_escape "$password")" > "$payload"
    chmod 600 "$payload"
    unset password

    code="$(curl --silent --show-error --output "$response" --write-out '%{http_code}' \
        --request POST --header 'Content-Type: application/json' --data-binary "@$payload" \
        "$api_base_url/api/auth/login")"
    rm -f "$payload"
    [ "$code" = "200" ] || fail "$prefix login failed with HTTP $code." 69

    token="$(sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$response")"
    rm -f "$response"
    [ -n "$token" ] || fail "$prefix login response did not contain an access token." 69

    printf 'header = "Authorization: Bearer %s"\n' "$token" > "$auth_config"
    chmod 600 "$auth_config"
    unset token
}

admin_auth="$work_directory/admin.curl"
redeemer_auth="$work_directory/redeemer.curl"
login "$admin_email" "$admin_password_file" "$admin_auth" admin
login "$redeemer_email" "$redeemer_password_file" "$redeemer_auth" redeemer

admin_probe_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --config "$admin_auth" "$api_base_url/api/admin/access-keys?skip=0&take=1")"
[ "$admin_probe_code" = "200" ] || fail "Admin authorization preflight failed with HTTP $admin_probe_code." 69

create_payload="$work_directory/create-key.json"
create_response="$work_directory/create-key-response.json"
printf '%s' '{"purpose":"Beta","tier":"Beta","durationDays":1,"grantsLifetimeAccess":false,"maxRedemptions":2,"expiresAtUtc":null,"label":"private-beta-lifecycle-smoke"}' > "$create_payload"
chmod 600 "$create_payload"

create_code="$(curl --silent --show-error --output "$create_response" --write-out '%{http_code}' \
    --request POST --config "$admin_auth" --header 'Content-Type: application/json' \
    --data-binary "@$create_payload" "$api_base_url/api/admin/subscription/access-keys")"
rm -f "$create_payload"
[ "$create_code" = "201" ] || fail "Access-key creation failed with HTTP $create_code." 69
chmod 600 "$create_response"

access_key_id="$(sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([0-9A-Fa-f-]*\)".*/\1/p' "$create_response")"
plaintext_key="$(sed -n 's/.*"plaintextKey"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$create_response")"
[ -n "$access_key_id" ] || fail "Create response did not contain an access-key id." 69
[ -n "$plaintext_key" ] || fail "Create response did not contain the one-time plaintext access key." 69

list_response="$work_directory/access-key-list.json"
list_code="$(curl --silent --show-error --output "$list_response" --write-out '%{http_code}' \
    --config "$admin_auth" "$api_base_url/api/admin/access-keys?skip=0&take=100")"
[ "$list_code" = "200" ] || fail "Access-key listing failed with HTTP $list_code." 69
if grep -Fq "$plaintext_key" "$list_response"; then
    fail "Access-key listing exposed plaintext key material." 70
fi
rm -f "$list_response"
printf '%s\n' 'PASS plaintext access key is one-time only'

redeem_payload="$work_directory/redeem-key.json"
redeem_response="$work_directory/redeem-key-response.json"
printf '{"accessKey":"%s"}' "$(json_escape "$plaintext_key")" > "$redeem_payload"
chmod 600 "$redeem_payload"
unset plaintext_key

redeem_code="$(curl --silent --show-error --output "$redeem_response" --write-out '%{http_code}' \
    --request POST --config "$redeemer_auth" --header 'Content-Type: application/json' \
    --data-binary "@$redeem_payload" "$api_base_url/api/subscription/access-keys/redeem")"
[ "$redeem_code" = "200" ] || fail "Access-key redemption failed with HTTP $redeem_code." 69
if ! grep -Eq '"tier"[[:space:]]*:[[:space:]]*"Beta"' "$redeem_response"; then
    fail "Access-key redemption did not grant the expected Beta tier." 69
fi
rm -f "$redeem_response"
printf '%s\n' 'PASS access key redemption'

revoke_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --request POST --config "$admin_auth" \
    "$api_base_url/api/admin/subscription/access-keys/$access_key_id/revoke")"
[ "$revoke_code" = "204" ] || fail "Access-key revocation failed with HTTP $revoke_code." 69
printf '%s\n' 'PASS access key revocation'

post_revoke_response="$work_directory/post-revoke-response.json"
post_revoke_code="$(curl --silent --show-error --output "$post_revoke_response" --write-out '%{http_code}' \
    --request POST --config "$redeemer_auth" --header 'Content-Type: application/json' \
    --data-binary "@$redeem_payload" "$api_base_url/api/subscription/access-keys/redeem")"
rm -f "$redeem_payload" "$post_revoke_response" "$create_response"
[ "$post_revoke_code" = "400" ] || fail "Revoked access key was not rejected; received HTTP $post_revoke_code." 70
printf '%s\n' 'PASS revoked access key rejected'

printf '%s\n' 'BillWatch access-key lifecycle smoke passed.'
