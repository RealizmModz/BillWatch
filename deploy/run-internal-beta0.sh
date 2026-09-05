#!/bin/sh

set -eu

umask 077

deployment_directory="${1:-}"
api_base_url="${2:-}"
web_base_url="${3:-}"
allow_run="${BILLWATCH_BETA0_ALLOW:-false}"
allow_partial="${BILLWATCH_BETA0_ALLOW_PARTIAL:-false}"
run_access_key="${BILLWATCH_BETA0_RUN_ACCESS_KEY:-true}"
run_plaid="${BILLWATCH_BETA0_RUN_PLAID:-true}"
run_statement="${BILLWATCH_BETA0_RUN_STATEMENT:-true}"
evidence_file="${BILLWATCH_BETA0_EVIDENCE_FILE:-}"
evidence_directory=""

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

usage()
{
    fail "Usage: BILLWATCH_BETA0_ALLOW=true $0 <deployment-directory> <https-api-base-url> <https-web-base-url>" 64
}

require_boolean()
{
    value="$1"
    label="$2"
    case "$value" in
        true|false) ;;
        *) fail "$label must be true or false." 64 ;;
    esac
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

[ -n "$deployment_directory" ] || usage
[ -n "$api_base_url" ] || usage
[ -n "$web_base_url" ] || usage
require_boolean "$allow_run" "BILLWATCH_BETA0_ALLOW"
require_boolean "$allow_partial" "BILLWATCH_BETA0_ALLOW_PARTIAL"
require_boolean "$run_access_key" "BILLWATCH_BETA0_RUN_ACCESS_KEY"
require_boolean "$run_plaid" "BILLWATCH_BETA0_RUN_PLAID"
require_boolean "$run_statement" "BILLWATCH_BETA0_RUN_STATEMENT"
[ "$allow_run" = "true" ] || fail "Internal Beta 0 requires explicit BILLWATCH_BETA0_ALLOW=true opt-in." 77
require_https_url "$api_base_url" "The Internal Beta 0 API URL"
require_https_url "$web_base_url" "The Internal Beta 0 Web URL"
api_base_url="${api_base_url%/}"
web_base_url="${web_base_url%/}"

[ -d "$deployment_directory" ] || fail "Deployment directory does not exist: $deployment_directory" 66
deployment_directory="$(cd "$deployment_directory" && pwd -P)"
command -v git >/dev/null 2>&1 || fail "git is required for Internal Beta 0 release verification." 69

release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] || fail "Verified release marker is missing: $release_file" 66
[ ! -L "$release_file" ] || fail "Verified release marker must not be a symbolic link." 73
release_sha="$(cat "$release_file")"
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' || fail "Verified release marker must contain one full lowercase Git SHA." 65
head_sha="$(git -C "$deployment_directory" rev-parse HEAD)"
[ "$head_sha" = "$release_sha" ] || fail "Deployment checkout HEAD does not match the verified release marker." 65
worktree_changes="$(git -C "$deployment_directory" status --porcelain --untracked-files=no)"
[ -z "$worktree_changes" ] || fail "Deployment checkout has tracked modifications; Internal Beta 0 requires the exact clean deployed release." 65

for script in \
    smoke-private-beta.sh \
    smoke-web-bff.sh \
    smoke-access-key-lifecycle.sh \
    smoke-plaid-lifecycle.sh \
    smoke-statement-lifecycle.sh
do
    [ -f "$deployment_directory/deploy/$script" ] || fail "Required Internal Beta 0 smoke gate is missing: deploy/$script" 66
done

if [ "$allow_partial" != "true" ] && { [ "$run_access_key" != "true" ] || [ "$run_plaid" != "true" ] || [ "$run_statement" != "true" ]; }; then
    fail "A complete Internal Beta 0 run requires access-key, Plaid, and statement phases. Set BILLWATCH_BETA0_ALLOW_PARTIAL=true only when intentionally collecting partial evidence." 77
fi

# Validate all evidence configuration before any network or mutation-bearing phase begins.
if [ -n "$evidence_file" ]; then
    case "$evidence_file" in
        /*) ;;
        *) fail "BILLWATCH_BETA0_EVIDENCE_FILE must be an absolute path outside the deployment checkout." 64 ;;
    esac
    case "$evidence_file" in
        "$deployment_directory"|"$deployment_directory"/*) fail "Internal Beta 0 evidence must live outside the deployment checkout." 64 ;;
    esac
    [ ! -L "$evidence_file" ] || fail "Refusing a symbolic-link Internal Beta 0 evidence file." 73
    evidence_directory="$(dirname "$evidence_file")"
    [ -d "$evidence_directory" ] || fail "Internal Beta 0 evidence directory does not exist: $evidence_directory" 66
fi

started_at="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
passed_phases=""

run_phase()
{
    phase_name="$1"
    shift
    printf 'RUN %s\n' "$phase_name"
    "$@"
    if [ -z "$passed_phases" ]; then
        passed_phases="$phase_name"
    else
        passed_phases="$passed_phases,$phase_name"
    fi
    printf 'PASS %s\n' "$phase_name"
}

run_phase direct-api sh "$deployment_directory/deploy/smoke-private-beta.sh" "$api_base_url" "$web_base_url"
run_phase web-bff sh "$deployment_directory/deploy/smoke-web-bff.sh" "$web_base_url"

if [ "$run_access_key" = "true" ]; then
    run_phase access-key sh "$deployment_directory/deploy/smoke-access-key-lifecycle.sh" "$api_base_url"
else
    printf '%s\n' 'SKIP access-key (partial run)'
fi

if [ "$run_plaid" = "true" ]; then
    run_phase plaid sh "$deployment_directory/deploy/smoke-plaid-lifecycle.sh" "$api_base_url"
else
    printf '%s\n' 'SKIP plaid (partial run)'
fi

if [ "$run_statement" = "true" ]; then
    run_phase statement sh "$deployment_directory/deploy/smoke-statement-lifecycle.sh" "$api_base_url"
else
    printf '%s\n' 'SKIP statement (partial run)'
fi

result="complete"
if [ "$run_access_key" != "true" ] || [ "$run_plaid" != "true" ] || [ "$run_statement" != "true" ]; then
    result="partial"
fi
completed_at="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

if [ -n "$evidence_file" ]; then
    temporary_evidence="$(mktemp "${evidence_file}.tmp.XXXXXX")"
    trap 'rm -f "${temporary_evidence:-}"' EXIT HUP INT TERM
    {
        printf 'VERSION=1\n'
        printf 'RESULT=%s\n' "$result"
        printf 'RELEASE_SHA=%s\n' "$release_sha"
        printf 'STARTED_AT_UTC=%s\n' "$started_at"
        printf 'COMPLETED_AT_UTC=%s\n' "$completed_at"
        printf 'PASSED_PHASES=%s\n' "$passed_phases"
    } > "$temporary_evidence"
    chmod 600 "$temporary_evidence"
    mv "$temporary_evidence" "$evidence_file"
    trap - EXIT HUP INT TERM
fi

if [ "$result" = "complete" ]; then
    printf 'BillWatch Internal Beta 0 automated acceptance passed for release %s.\n' "$release_sha"
    printf '%s\n' 'This proves the automated gates only; human Plaid authorization/provider behavior, external alert observation, recovery drills, and legal review remain separate evidence.'
else
    printf 'BillWatch Internal Beta 0 partial acceptance passed for release %s.\n' "$release_sha"
    printf '%s\n' 'Partial evidence must not be recorded as a completed Internal Beta 0.'
fi
