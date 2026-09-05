#!/bin/sh

set -eu

umask 077

deployment_directory=${1:-}
beta0_evidence=${BILLWATCH_BETA0_EVIDENCE_FILE:-}
recovery_evidence=${BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE:-}
reboot_evidence=${BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE:-}
output_evidence=${BILLWATCH_TECHNICAL_EVIDENCE_FILE:-}
required_beta0_phases='account-deletion,direct-api,web-bff,admin-authz,access-key,plaid,statement,statement-semantics,subscription'

fail()
{
    printf '%s\n' "Technical evidence verification failed: $1" >&2
    exit "${2:-64}"
}

[ -n "$deployment_directory" ] || fail "usage: $0 <deployment-directory>"
[ -d "$deployment_directory" ] || fail "deployment directory does not exist: $deployment_directory" 66
deployment_directory=$(cd "$deployment_directory" && pwd -P)
release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] || fail "verified release marker is missing." 66
[ ! -L "$release_file" ] || fail "verified release marker must not be a symbolic link." 73
release_sha=$(cat "$release_file")
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' || fail "verified release marker must contain one full lowercase Git SHA." 65
head_sha=$(git -C "$deployment_directory" rev-parse HEAD)
[ "$head_sha" = "$release_sha" ] || fail "deployment checkout HEAD does not match the verified release marker." 65
[ -z "$(git -C "$deployment_directory" status --porcelain --untracked-files=no)" ] || fail "deployment checkout has tracked modifications." 65

read_value()
{
    file=$1
    key=$2
    count=$(grep -c "^${key}=" "$file" || true)
    [ "$count" = 1 ] || fail "evidence file must contain exactly one $key field: $file" 65
    sed -n "s/^${key}=//p" "$file"
}

verify_evidence()
{
    file=$1
    expected_phases=$2
    label=$3
    [ -n "$file" ] || fail "$label evidence path is required."
    case "$file" in
        /*) ;;
        *) fail "$label evidence path must be absolute." ;;
    esac
    case "$file" in
        "$deployment_directory"|"$deployment_directory"/*) fail "$label evidence must live outside the deployment checkout." ;;
    esac
    [ -f "$file" ] || fail "$label evidence file does not exist: $file" 66
    [ ! -L "$file" ] || fail "$label evidence must not be a symbolic link." 73
    [ "$(stat -c '%a' "$file" 2>/dev/null || true)" = 600 ] || fail "$label evidence must have mode 600."
    [ "$(read_value "$file" VERSION)" = 1 ] || fail "$label evidence has an unsupported version." 65
    [ "$(read_value "$file" RESULT)" = complete ] || fail "$label evidence is not complete." 65
    [ "$(read_value "$file" RELEASE_SHA)" = "$release_sha" ] || fail "$label evidence belongs to a different release." 65
    [ "$(read_value "$file" PASSED_PHASES)" = "$expected_phases" ] || fail "$label evidence does not prove the required phases." 65
}

verify_evidence "$beta0_evidence" "$required_beta0_phases" "Internal Beta 0"
verify_evidence "$recovery_evidence" "clean-host-recovery" "clean-host recovery"
verify_evidence "$reboot_evidence" "controlled-reboot-recovery" "controlled reboot recovery"

if [ -n "$output_evidence" ]; then
    case "$output_evidence" in
        /*) ;;
        *) fail "BILLWATCH_TECHNICAL_EVIDENCE_FILE must be an absolute path." ;;
    esac
    case "$output_evidence" in
        "$deployment_directory"|"$deployment_directory"/*) fail "technical evidence output must live outside the deployment checkout." ;;
    esac
    [ ! -L "$output_evidence" ] || fail "refusing a symbolic-link technical evidence destination." 73
    [ ! -e "$output_evidence" ] || fail "technical evidence output already exists; refusing to overwrite it." 73
    output_dir=$(dirname "$output_evidence")
    [ -d "$output_dir" ] || fail "technical evidence output directory does not exist: $output_dir" 66
    temporary=$(mktemp "${output_evidence}.tmp.XXXXXX")
    trap 'rm -f "${temporary:-}"' EXIT HUP INT TERM
    {
        printf 'VERSION=1\n'
        printf 'RESULT=complete\n'
        printf 'RELEASE_SHA=%s\n' "$release_sha"
        printf 'COMPLETED_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
        printf 'PASSED_PHASES=internal-beta0,clean-host-recovery,controlled-reboot-recovery\n'
    } > "$temporary"
    chmod 600 "$temporary"
    ln "$temporary" "$output_evidence" || fail "could not publish technical evidence without overwriting an existing file." 73
    rm -f "$temporary"
    trap - EXIT HUP INT TERM
fi

printf 'Release-pinned private-beta technical evidence verified for %s.\n' "$release_sha"
printf '%s\n' 'This verifies only machine-verifiable Internal Beta 0, clean-host recovery, and controlled reboot evidence. Human Plaid/provider observation, external alert receipt, provider-enforced immutable backup protection, and qualified legal review remain separate launch gates.'
