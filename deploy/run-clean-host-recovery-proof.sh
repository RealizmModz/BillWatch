#!/bin/sh

set -eu

umask 077

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
env_file=${1:-}
evidence_file=${BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE:-}

fail()
{
    printf '%s\n' "Recovery proof refused: $1" >&2
    exit "${2:-64}"
}

[ -n "$env_file" ] || fail "usage: BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE=/secure/path $0 <protected-recovery-env>"
[ -n "$evidence_file" ] || fail "BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE is required."
case "$evidence_file" in
    /*) ;;
    *) fail "recovery proof evidence path must be absolute." ;;
esac
case "$evidence_file" in
    "$root_dir"|"$root_dir"/*) fail "recovery proof evidence must live outside the checkout." ;;
esac
[ ! -L "$evidence_file" ] || fail "refusing a symbolic-link recovery proof destination." 73
[ ! -e "$evidence_file" ] || fail "recovery proof evidence already exists; refusing to overwrite it." 73
evidence_dir=$(dirname "$evidence_file")
[ -d "$evidence_dir" ] || fail "recovery proof evidence directory does not exist: $evidence_dir" 66

runner="$root_dir/deploy/run-clean-host-recovery-drill.sh"
[ -f "$runner" ] || fail "clean-host recovery drill runner is missing." 66

sh "$runner" "$env_file"

release_sha=$(git -C "$root_dir" rev-parse HEAD)
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' || fail "recovery proof release SHA is malformed." 65

completed_at=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
temporary=$(mktemp "${evidence_file}.tmp.XXXXXX")
trap 'rm -f "${temporary:-}"' EXIT HUP INT TERM
{
    printf 'VERSION=1\n'
    printf 'RESULT=complete\n'
    printf 'RELEASE_SHA=%s\n' "$release_sha"
    printf 'COMPLETED_AT_UTC=%s\n' "$completed_at"
    printf 'PASSED_PHASES=clean-host-recovery\n'
} > "$temporary"
chmod 600 "$temporary"
ln "$temporary" "$evidence_file" || fail "could not publish recovery proof evidence without overwriting an existing file." 73
rm -f "$temporary"
trap - EXIT HUP INT TERM

printf 'Release-pinned clean-host recovery proof recorded for %s.\n' "$release_sha"
