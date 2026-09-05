#!/bin/sh

set -eu
umask 077

phase=${1:-}
deployment_directory=${2:-}
api_base_url=${3:-}
pending_file=${BILLWATCH_PLAID_OBSERVATION_PENDING_FILE:-}
evidence_file=${BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE:-}
email=${BILLWATCH_PLAID_OBSERVATION_EMAIL:-}
password_file=${BILLWATCH_PLAID_OBSERVATION_PASSWORD_FILE:-}
connection_id=${BILLWATCH_PLAID_OBSERVATION_CONNECTION_ID:-}
confirmation='I completed the BillWatch Plaid update flow in Plaid Hosted Link'

fail()
{
    printf '%s\n' "Plaid observation proof refused: $1" >&2
    exit "${2:-64}"
}

require_guid()
{
    printf '%s\n' "$1" | grep -Eq '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$' || fail "$2 must be a GUID."
}

check_external_path()
{
    path=$1
    label=$2
    [ -n "$path" ] || fail "$label path is required."
    case "$path" in /*) ;; *) fail "$label path must be absolute." ;; esac
    case "$path" in "$deployment_directory"|"$deployment_directory"/*) fail "$label must live outside the deployment checkout." ;; esac
    [ ! -L "$path" ] || fail "$label must not be a symbolic link." 73
    [ -d "$(dirname "$path")" ] || fail "$label directory does not exist." 66
}

read_value()
{
    file=$1
    key=$2
    count=$(grep -c "^${key}=" "$file" || true)
    [ "$count" = 1 ] || fail "pending proof must contain exactly one $key field." 65
    sed -n "s/^${key}=//p" "$file"
}

[ "$phase" = prepare ] || [ "$phase" = confirm ] || fail "usage: $0 <prepare|confirm> <deployment-directory> <https-api-base-url>"
[ -d "$deployment_directory" ] || fail "deployment directory does not exist." 66
deployment_directory=$(cd "$deployment_directory" && pwd -P)
case "$api_base_url" in https://*) ;; *) fail "API base URL must use HTTPS." ;; esac
api_base_url=${api_base_url%/}
[ -n "$email" ] || fail "BILLWATCH_PLAID_OBSERVATION_EMAIL is required."
[ -n "$password_file" ] || fail "BILLWATCH_PLAID_OBSERVATION_PASSWORD_FILE is required."
[ -f "$password_file" ] || fail "Plaid observation password must be a regular file."
[ ! -L "$password_file" ] || fail "Plaid observation password must not be a symbolic link." 73
[ "$(stat -c '%a' "$password_file" 2>/dev/null || true)" = 600 ] || fail "Plaid observation password file must have mode 600." 77
require_guid "$connection_id" "BILLWATCH_PLAID_OBSERVATION_CONNECTION_ID"
check_external_path "$pending_file" "pending proof"
check_external_path "$evidence_file" "Plaid observation evidence"
[ "$pending_file" != "$evidence_file" ] || fail "pending and evidence paths must differ."

release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] || fail "verified release marker is missing." 66
[ ! -L "$release_file" ] || fail "verified release marker must not be a symbolic link." 73
release_sha=$(cat "$release_file")
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' || fail "verified release marker is malformed." 65
[ "$(git -C "$deployment_directory" rev-parse HEAD)" = "$release_sha" ] || fail "checkout HEAD does not match the verified release." 65
[ -z "$(git -C "$deployment_directory" status --porcelain --untracked-files=no)" ] || fail "deployment checkout has tracked modifications." 65

work_directory=$(mktemp -d)
chmod 700 "$work_directory"
trap 'rm -rf "$work_directory"; rm -f "${temporary:-}"' EXIT HUP INT TERM
login_payload="$work_directory/login.json"
login_response="$work_directory/login-response.json"
auth_config="$work_directory/auth.curl"
password=$(cat "$password_file")
[ -n "$password" ] || fail "Plaid observation password file is empty."
printf '{"email":"%s","password":"%s"}' \
    "$(printf '%s' "$email" | sed 's/\\/\\\\/g; s/"/\\"/g')" \
    "$(printf '%s' "$password" | sed 's/\\/\\\\/g; s/"/\\"/g')" > "$login_payload"
chmod 600 "$login_payload"
unset password
login_code=$(curl --silent --show-error --output "$login_response" --write-out '%{http_code}' --request POST --header 'Content-Type: application/json' --data-binary "@$login_payload" "$api_base_url/api/auth/login")
rm -f "$login_payload"
[ "$login_code" = 200 ] || fail "authentication failed with HTTP $login_code." 69
access_token=$(sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$login_response")
rm -f "$login_response"
[ -n "$access_token" ] || fail "authentication did not return an access token." 69
printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

connections="$work_directory/connections.json"
connections_code=$(curl --silent --show-error --output "$connections" --write-out '%{http_code}' --config "$auth_config" "$api_base_url/api/bank-connections")
[ "$connections_code" = 200 ] || fail "bank connection lookup failed with HTTP $connections_code." 69
connection_record=$(tr '}' '\n' < "$connections" | grep -Fi "\"id\":\"$connection_id\"" | head -n 1 || true)
[ -n "$connection_record" ] || fail "configured bank connection is not owned by the authenticated account." 65

if [ "$phase" = prepare ]; then
    [ "${BILLWATCH_PLAID_OBSERVATION_ALLOW_PREPARE:-false}" = true ] || fail "set BILLWATCH_PLAID_OBSERVATION_ALLOW_PREPARE=true to create a live update-mode session." 77
    [ ! -e "$pending_file" ] || fail "pending proof already exists; refusing overwrite." 73
    [ ! -e "$evidence_file" ] || fail "evidence already exists; refusing overwrite." 73
    update_response="$work_directory/update.json"
    update_code=$(curl --silent --show-error --output "$update_response" --write-out '%{http_code}' --request POST --config "$auth_config" "$api_base_url/api/plaid/connections/$connection_id/update-link-token")
    [ "$update_code" = 200 ] || fail "Plaid update-mode session creation failed with HTTP $update_code." 69
    if grep -Eiq '"(accessToken|refreshToken|linkToken|protectedLinkToken|plaidAccessToken|storagePath|storedFilePath)"[[:space:]]*:' "$update_response"; then fail "update-mode response exposed a forbidden credential or storage field." 70; fi
    session_id=$(sed -n 's/.*"sessionId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$update_response")
    require_guid "$session_id" "Plaid update-mode session ID"
    hosted_link_url=$(sed -n 's/.*"hostedLinkUrl"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$update_response")
    case "$hosted_link_url" in https://plaid.com/*|https://*.plaid.com/*) ;; *) fail "Hosted Link URL must be HTTPS on plaid.com." 70 ;; esac
    temporary=$(mktemp "${pending_file}.tmp.XXXXXX")
    {
        printf 'VERSION=1\nRESULT=pending-observation\nRELEASE_SHA=%s\n' "$release_sha"
        printf 'CONNECTION_ID=%s\nSESSION_ID=%s\n' "$connection_id" "$session_id"
        printf 'CREATED_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    } > "$temporary"
    chmod 600 "$temporary"
    ln "$temporary" "$pending_file" || fail "could not publish pending proof without overwrite." 73
    rm -f "$temporary"
    printf 'Open this Plaid Hosted Link URL and complete update mode, then run confirm:\n%s\n' "$hosted_link_url"
    exit 0
fi

[ -f "$pending_file" ] || fail "pending proof does not exist; run prepare first." 66
[ ! -L "$pending_file" ] || fail "pending proof must not be a symbolic link." 73
[ "$(stat -c '%a' "$pending_file" 2>/dev/null || true)" = 600 ] || fail "pending proof must have mode 600." 77
[ ! -e "$evidence_file" ] || fail "evidence already exists; refusing overwrite." 73
[ "$(read_value "$pending_file" VERSION)" = 1 ] || fail "pending proof version is unsupported." 65
[ "$(read_value "$pending_file" RESULT)" = pending-observation ] || fail "pending proof is not awaiting observation." 65
[ "$(read_value "$pending_file" RELEASE_SHA)" = "$release_sha" ] || fail "pending proof belongs to a different release." 65
[ "$(read_value "$pending_file" CONNECTION_ID)" = "$connection_id" ] || fail "pending proof belongs to a different bank connection." 65
[ "${BILLWATCH_PLAID_OBSERVATION_CONFIRMATION:-}" = "$confirmation" ] || fail "exact Plaid observation confirmation phrase is required." 77
session_id=$(read_value "$pending_file" SESSION_ID)
require_guid "$session_id" "pending Plaid session ID"

complete_response="$work_directory/complete.json"
complete_code=$(curl --silent --show-error --output "$complete_response" --write-out '%{http_code}' --request POST --config "$auth_config" "$api_base_url/api/plaid/link-session/$session_id/complete")
[ "$complete_code" = 200 ] || fail "Plaid Hosted Link completion check failed with HTTP $complete_code." 69
grep -Eq '"status"[[:space:]]*:[[:space:]]*"Completed"' "$complete_response" || fail "Plaid Hosted Link session is not completed." 65
for sync_path in accounts transactions
do
    sync_code=$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --request POST --config "$auth_config" "$api_base_url/api/plaid/connections/$connection_id/$sync_path/sync")
    [ "$sync_code" = 200 ] || fail "post-update $sync_path sync failed with HTTP $sync_code." 69
done
connections_code=$(curl --silent --show-error --output "$connections" --write-out '%{http_code}' --config "$auth_config" "$api_base_url/api/bank-connections")
[ "$connections_code" = 200 ] || fail "post-update bank connection lookup failed." 69
connection_record=$(tr '}' '\n' < "$connections" | grep -Fi "\"id\":\"$connection_id\"" | head -n 1 || true)
printf '%s\n' "$connection_record" | grep -Eq '"status"[[:space:]]*:[[:space:]]*0([,]|$)' || fail "bank connection is not Active after update-mode sync." 65
printf '%s\n' "$connection_record" | grep -Eq '"lastSuccessfulSyncAtUtc"[[:space:]]*:[[:space:]]*"[^\"]+"' || fail "bank connection lacks a successful post-update sync timestamp." 65

temporary=$(mktemp "${evidence_file}.tmp.XXXXXX")
{
    printf 'VERSION=1\nRESULT=complete\nRELEASE_SHA=%s\n' "$release_sha"
    printf 'COMPLETED_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    printf 'PASSED_PHASES=plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active\n'
} > "$temporary"
chmod 600 "$temporary"
ln "$temporary" "$evidence_file" || fail "could not publish Plaid observation evidence without overwrite." 73
rm -f "$temporary" "$pending_file"
printf 'Release-pinned Plaid Hosted Link observation evidence recorded for %s.\n' "$release_sha"
