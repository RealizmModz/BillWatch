#!/bin/sh

set -eu

umask 077

deployment_directory="${1:-}"
api_base_url="${2:-}"
allow_run="${BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW:-false}"
confirmation="${BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM:-}"
email="${BILLWATCH_ACCOUNT_DELETE_SMOKE_EMAIL:-}"
confirm_email="${BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL:-}"
password_file="${BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE:-}"
evidence_file="${BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE:-}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

usage()
{
    fail "Usage: BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW=true $0 <deployment-directory> <https-api-base-url>" 64
}

require_boolean()
{
    case "$1" in
        true|false) ;;
        *) fail "$2 must be true or false." 64 ;;
    esac
}

require_https_url()
{
    case "$1" in
        https://*) ;;
        *) fail "$2 must use HTTPS." 64 ;;
    esac
    case "$1" in
        *[[:space:]]*) fail "$2 must not contain whitespace." 64 ;;
    esac
}

[ -n "$deployment_directory" ] || usage
[ -n "$api_base_url" ] || usage
require_boolean "$allow_run" "BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW"
[ "$allow_run" = "true" ] ||
    fail "Account deletion proof requires explicit BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW=true opt-in." 77
[ "$confirmation" = "DELETE-THROWAWAY-ACCOUNT" ] ||
    fail "Set BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM=DELETE-THROWAWAY-ACCOUNT for the dedicated disposable identity." 77
[ -n "$email" ] || fail "BILLWATCH_ACCOUNT_DELETE_SMOKE_EMAIL is required." 64
[ "$confirm_email" = "$email" ] ||
    fail "BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL must exactly match the disposable account email." 77
[ -n "$password_file" ] || fail "BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE is required." 64
[ -n "$evidence_file" ] || fail "BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE is required." 64

require_https_url "$api_base_url" "The account deletion proof API URL"
api_base_url="${api_base_url%/}"

[ -d "$deployment_directory" ] || fail "Deployment directory does not exist: $deployment_directory" 66
deployment_directory="$(cd "$deployment_directory" && pwd -P)"
command -v git >/dev/null 2>&1 || fail "git is required for account deletion release verification." 69

release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] || fail "Verified release marker is missing: $release_file" 66
[ ! -L "$release_file" ] || fail "Verified release marker must not be a symbolic link." 73
release_sha="$(cat "$release_file")"
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' ||
    fail "Verified release marker must contain one full lowercase Git SHA." 65
head_sha="$(git -C "$deployment_directory" rev-parse HEAD)"
[ "$head_sha" = "$release_sha" ] ||
    fail "Deployment checkout HEAD does not match the verified release marker." 65
worktree_changes="$(git -C "$deployment_directory" status --porcelain --untracked-files=no)"
[ -z "$worktree_changes" ] ||
    fail "Deployment checkout has tracked modifications; account deletion proof requires the exact deployed release." 65

case "$password_file" in
    /*) ;;
    *) fail "BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE must be an absolute path outside the deployment checkout." 64 ;;
esac
case "$password_file" in
    "$deployment_directory"|"$deployment_directory"/*)
        fail "Account deletion credentials must live outside the deployment checkout." 64
        ;;
esac
[ -f "$password_file" ] || fail "Account deletion password file must be a regular file." 64
[ ! -L "$password_file" ] || fail "Account deletion password file must not be a symbolic link." 73
password_mode="$(stat -c '%a' "$password_file" 2>/dev/null || true)"
[ "$password_mode" = "600" ] || fail "Account deletion password file must have mode 600." 64
IFS= read -r password < "$password_file" || true
[ -n "${password:-}" ] || fail "Account deletion password file is empty." 64

case "$evidence_file" in
    /*) ;;
    *) fail "BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE must be an absolute path outside the deployment checkout." 64 ;;
esac
case "$evidence_file" in
    "$deployment_directory"|"$deployment_directory"/*)
        fail "Account deletion evidence must live outside the deployment checkout." 64
        ;;
esac
[ ! -e "$evidence_file" ] ||
    fail "Refusing to overwrite existing account deletion evidence: $evidence_file" 73
evidence_directory="$(dirname "$evidence_file")"
[ -d "$evidence_directory" ] ||
    fail "Account deletion evidence directory does not exist: $evidence_directory" 66

for protected_identity in \
    "${BILLWATCH_SMOKE_EMAIL:-}" \
    "${BILLWATCH_WEB_SMOKE_EMAIL:-}" \
    "${BILLWATCH_ADMIN_SMOKE_EMAIL:-}" \
    "${BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL:-}" \
    "${BILLWATCH_ACCESS_KEY_SMOKE_EMAIL:-}" \
    "${BILLWATCH_PLAID_SMOKE_EMAIL:-}" \
    "${BILLWATCH_STATEMENT_SMOKE_EMAIL:-}" \
    "${BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL:-}"
do
    if [ -n "$protected_identity" ] && [ "$email" = "$protected_identity" ]; then
        fail "Refusing to delete an identity also configured for another BillWatch acceptance phase." 77
    fi
done

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"
cleanup()
{
    rm -rf "$work_directory"
}
trap cleanup EXIT HUP INT TERM

login_payload="$work_directory/login.json"
login_response="$work_directory/login-response.json"
delete_payload="$work_directory/delete.json"
auth_config="$work_directory/auth.curl"

json_escape()
{
    printf '%s' "$1" |
        sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/\\t/g'
}

started_at="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

printf '{"email":"%s","password":"%s"}' \
    "$(json_escape "$email")" \
    "$(json_escape "$password")" \
    > "$login_payload"
chmod 600 "$login_payload"

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
    fail "Disposable account authentication failed with HTTP $login_code." 69

access_token="$(
    sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$login_response"
)"
rm -f "$login_response"
[ -n "$access_token" ] ||
    fail "Disposable account login did not return an access token." 69
printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

predelete_export_code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --write-out '%{http_code}' \
        --config "$auth_config" \
        "$api_base_url/api/account/export"
)"
[ "$predelete_export_code" = "200" ] ||
    fail "Disposable account export preflight failed with HTTP $predelete_export_code." 69

printf '{"confirmation":"DELETE","currentPassword":"%s"}' \
    "$(json_escape "$password")" \
    > "$delete_payload"
chmod 600 "$delete_payload"
unset password

delete_code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --write-out '%{http_code}' \
        --request DELETE \
        --header 'Content-Type: application/json' \
        --config "$auth_config" \
        --data-binary "@$delete_payload" \
        "$api_base_url/api/account"
)"
rm -f "$delete_payload"
[ "$delete_code" = "204" ] ||
    fail "Disposable account deletion failed with HTTP $delete_code. The account may require 2FA input, hold a staff role, or have an external revocation/storage failure; do not weaken those protections." 69

postdelete_export_code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --write-out '%{http_code}' \
        --config "$auth_config" \
        "$api_base_url/api/account/export"
)"
[ "$postdelete_export_code" = "404" ] ||
    fail "Deleted identity still resolved through the account export surface (HTTP $postdelete_export_code)." 70

IFS= read -r password < "$password_file" || true
printf '{"email":"%s","password":"%s"}' \
    "$(json_escape "$email")" \
    "$(json_escape "$password")" \
    > "$login_payload"
chmod 600 "$login_payload"
unset password

relogin_code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --write-out '%{http_code}' \
        --request POST \
        --header 'Content-Type: application/json' \
        --data-binary "@$login_payload" \
        "$api_base_url/api/auth/login"
)"
rm -f "$login_payload"
[ "$relogin_code" = "401" ] ||
    fail "Deleted disposable credentials were not rejected with HTTP 401 (received $relogin_code)." 70

completed_at="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
temporary_evidence="$(mktemp "${evidence_file}.tmp.XXXXXX")"
trap 'rm -f "${temporary_evidence:-}"; cleanup' EXIT HUP INT TERM
{
    printf 'VERSION=1\n'
    printf 'RESULT=complete\n'
    printf 'RELEASE_SHA=%s\n' "$release_sha"
    printf 'STARTED_AT_UTC=%s\n' "$started_at"
    printf 'COMPLETED_AT_UTC=%s\n' "$completed_at"
    printf 'PASSED_PHASES=account-deletion\n'
} > "$temporary_evidence"
chmod 600 "$temporary_evidence"
mv "$temporary_evidence" "$evidence_file"
trap cleanup EXIT HUP INT TERM

printf 'BillWatch disposable account deletion proof passed for release %s.\n' "$release_sha"
printf '%s\n' 'The proof confirms the dedicated account existed, was deleted through the production API, no longer resolved through account export, and could no longer authenticate.'
