#!/bin/sh

set -eu

umask 077

phase=${1:-}
deployment_directory=${2:-}
evidence_file=${BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE:-}

fail()
{
    printf '%s\n' "Reboot proof refused: $1" >&2
    exit "${2:-64}"
}

[ "$phase" = preflight ] || [ "$phase" = postflight ] || fail "usage: BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE=/secure/path $0 <preflight|postflight> <deployment-directory>"
[ -n "$deployment_directory" ] || fail "deployment directory is required."
[ -d "$deployment_directory" ] || fail "deployment directory does not exist: $deployment_directory" 66
deployment_directory=$(cd "$deployment_directory" && pwd -P)
[ -n "$evidence_file" ] || fail "BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE is required."
case "$evidence_file" in
    /*) ;;
    *) fail "reboot proof evidence path must be absolute." ;;
esac
case "$evidence_file" in
    "$deployment_directory"|"$deployment_directory"/*) fail "reboot proof evidence must live outside the deployment checkout." ;;
esac
[ ! -L "$evidence_file" ] || fail "refusing a symbolic-link reboot proof destination." 73
[ ! -e "$evidence_file" ] || fail "reboot proof evidence already exists; refusing to overwrite it." 73
evidence_dir=$(dirname "$evidence_file")
[ -d "$evidence_dir" ] || fail "reboot proof evidence directory does not exist: $evidence_dir" 66

runner="$deployment_directory/deploy/run-controlled-reboot-drill.sh"
[ -f "$runner" ] || fail "controlled reboot drill runner is missing." 66

sh "$runner" "$phase" "$deployment_directory"

if [ "$phase" = preflight ]; then
    printf '%s\n' "Controlled reboot proof preflight passed; no completion evidence is written until postflight proves a distinct boot."
    exit 0
fi

release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] || fail "verified release marker is missing after reboot." 66
[ ! -L "$release_file" ] || fail "verified release marker must not be a symbolic link." 73
release_sha=$(cat "$release_file")
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' || fail "reboot proof release SHA is malformed." 65
head_sha=$(git -C "$deployment_directory" rev-parse HEAD)
[ "$head_sha" = "$release_sha" ] || fail "deployment checkout HEAD changed before reboot evidence publication." 65

completed_at=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
temporary=$(mktemp "${evidence_file}.tmp.XXXXXX")
trap 'rm -f "${temporary:-}"' EXIT HUP INT TERM
{
    printf 'VERSION=1\n'
    printf 'RESULT=complete\n'
    printf 'RELEASE_SHA=%s\n' "$release_sha"
    printf 'COMPLETED_AT_UTC=%s\n' "$completed_at"
    printf 'PASSED_PHASES=controlled-reboot-recovery\n'
} > "$temporary"
chmod 600 "$temporary"
ln "$temporary" "$evidence_file" || fail "could not publish reboot proof evidence without overwriting an existing file." 73
rm -f "$temporary"
trap - EXIT HUP INT TERM

printf 'Release-pinned controlled reboot recovery proof recorded for %s.\n' "$release_sha"
