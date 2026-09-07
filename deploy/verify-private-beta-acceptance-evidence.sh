#!/bin/sh

set -eu
umask 077

deployment_directory=${1:-}
technical=${BILLWATCH_TECHNICAL_EVIDENCE_FILE:-}
alerts=${BILLWATCH_ALERT_PROOF_EVIDENCE_FILE:-}
plaid=${BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE:-}
output=${BILLWATCH_ACCEPTANCE_EVIDENCE_FILE:-}

fail(){ printf '%s\n' "Private-beta acceptance evidence failed: $1" >&2; exit "${2:-64}"; }
[ -d "$deployment_directory" ] || fail "usage: $0 <deployment-directory>" 66
deployment_directory=$(cd "$deployment_directory" && pwd -P)
release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] && [ ! -L "$release_file" ] || fail "verified release marker is missing or unsafe." 66
release_sha=$(cat "$release_file")
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' || fail "verified release marker is malformed." 65
[ "$(git -C "$deployment_directory" rev-parse HEAD)" = "$release_sha" ] || fail "checkout HEAD does not match verified release." 65
[ -z "$(git -C "$deployment_directory" status --porcelain --untracked-files=no)" ] || fail "deployment checkout has tracked modifications." 65

read_value(){ count=$(grep -c "^${2}=" "$1" || true); [ "$count" = 1 ] || fail "evidence must contain exactly one $2 field: $1" 65; sed -n "s/^${2}=//p" "$1"; }
verify(){
    file=$1; phases=$2; label=$3
    [ -n "$file" ] || fail "$label evidence path is required."
    case "$file" in /*) ;; *) fail "$label evidence path must be absolute." ;; esac
    case "$file" in "$deployment_directory"|"$deployment_directory"/*) fail "$label evidence must live outside the checkout." ;; esac
    [ -f "$file" ] && [ ! -L "$file" ] || fail "$label evidence is missing or unsafe." 66
    [ "$(stat -c '%a' "$file" 2>/dev/null || true)" = 600 ] || fail "$label evidence must have mode 600." 77
    [ "$(read_value "$file" VERSION)" = 1 ] || fail "$label evidence version is unsupported." 65
    [ "$(read_value "$file" RESULT)" = complete ] || fail "$label evidence is incomplete." 65
    [ "$(read_value "$file" RELEASE_SHA)" = "$release_sha" ] || fail "$label evidence belongs to another release." 65
    [ "$(read_value "$file" PASSED_PHASES)" = "$phases" ] || fail "$label evidence does not prove the required phases." 65
}
verify "$technical" 'internal-beta0,clean-host-recovery,controlled-reboot-recovery' 'technical'
verify "$alerts" 'operations-alert-observed,external-readiness-alert-observed' 'alert observation'
verify "$plaid" 'plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active' 'Plaid observation'

if [ -n "$output" ]; then
    case "$output" in /*) ;; *) fail "acceptance evidence output must be absolute." ;; esac
    case "$output" in "$deployment_directory"|"$deployment_directory"/*) fail "acceptance evidence output must live outside the checkout." ;; esac
    [ ! -L "$output" ] && [ ! -e "$output" ] || fail "acceptance evidence output already exists or is unsafe." 73
    [ -d "$(dirname "$output")" ] || fail "acceptance evidence output directory does not exist." 66
    temporary=$(mktemp "${output}.tmp.XXXXXX")
    trap 'rm -f "${temporary:-}"' EXIT HUP INT TERM
    {
        printf 'VERSION=1\nRESULT=complete\nRELEASE_SHA=%s\n' "$release_sha"
        printf 'COMPLETED_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
        printf 'PASSED_PHASES=machine-technical,alert-observation,plaid-observation\n'
    } > "$temporary"
    chmod 600 "$temporary"
    ln "$temporary" "$output" || fail "could not publish acceptance evidence without overwrite." 73
    rm -f "$temporary"
    trap - EXIT HUP INT TERM
fi
printf 'Same-release private-beta acceptance evidence verified for %s.\n' "$release_sha"
printf '%s\n' 'Provider-enforced immutable backup protection and qualified Terms/Privacy review remain separate launch gates and are not claimed by this evidence.'
