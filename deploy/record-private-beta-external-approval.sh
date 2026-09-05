#!/bin/sh
set -eu
umask 077

approval_type=${1:-}
deployment_directory=${2:-}
output=${BILLWATCH_EXTERNAL_APPROVAL_EVIDENCE_FILE:-}

fail()
{
    printf '%s\n' "External approval evidence failed: $1" >&2
    exit 1
}

[ "$approval_type" = backup ] || [ "$approval_type" = legal ] ||
    fail "first argument must be 'backup' or 'legal'."

[ -n "$deployment_directory" ] ||
    fail "deployment directory is required."

[ -d "$deployment_directory" ] ||
    fail "deployment directory does not exist."

root_dir=$(CDPATH= cd -- "$deployment_directory" && pwd)
release_file="$root_dir/.billwatch-release"

[ -f "$release_file" ] && [ ! -L "$release_file" ] ||
    fail ".billwatch-release must be a regular non-symlink file."

release=$(tr -d '\r\n' < "$release_file")
printf '%s' "$release" | grep -Eq '^[0-9a-f]{40}$' ||
    fail ".billwatch-release must contain one 40-character lowercase Git SHA."

command -v git >/dev/null 2>&1 ||
    fail "git is required."

head=$(git -C "$root_dir" rev-parse HEAD 2>/dev/null) ||
    fail "deployment directory is not a Git checkout."

[ "$head" = "$release" ] ||
    fail "checked-out commit does not match .billwatch-release."

[ -z "$(git -C "$root_dir" status --porcelain --untracked-files=normal)" ] ||
    fail "deployment checkout must be clean."

case "$output" in
    /*) ;;
    *) fail "BILLWATCH_EXTERNAL_APPROVAL_EVIDENCE_FILE must be an absolute path outside the deployment checkout." ;;
esac

case "$output" in
    "$root_dir"|"$root_dir"/*)
        fail "approval evidence must be stored outside the deployment checkout."
        ;;
esac

[ ! -e "$output" ] && [ ! -L "$output" ] ||
    fail "approval evidence destination already exists; evidence is never overwritten."

output_parent=$(dirname -- "$output")
[ -d "$output_parent" ] ||
    fail "approval evidence parent directory does not exist."

completed_at=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
extra_line=
phases=

case "$approval_type" in
    backup)
        expected='I attest that provider-enforced immutable/Object-Lock/WORM or equivalent backup protection is configured and that recovery from the protected storage path succeeded for this deployed release.'
        [ "${BILLWATCH_BACKUP_PROTECTION_ATTESTATION:-}" = "$expected" ] ||
            fail "backup protection requires the exact BILLWATCH_BACKUP_PROTECTION_ATTESTATION phrase after provider-side protection and protected-path recovery have actually been verified."
        phases='provider-immutable-backup-attested,protected-backup-recovery-attested'
        ;;
    legal)
        legal_source="$root_dir/BillWatch.Core/Legal/BillWatchLegalDocuments.cs"
        [ -f "$legal_source" ] && [ ! -L "$legal_source" ] ||
            fail "BillWatchLegalDocuments.cs must be a regular non-symlink file."
        legal_version=$(awk '/CurrentVersion[[:space:]]*=/{getline; line=$0; gsub(/[[:space:]"]/, "", line); sub(/;.*/, "", line); print line; exit}' "$legal_source")
        [ -n "$legal_version" ] ||
            fail "could not determine the deployed legal document version."
        expected='I attest that a qualified review of the deployed BillWatch Terms and Privacy documents is complete and approved for trusted private beta.'
        [ "${BILLWATCH_LEGAL_REVIEW_ATTESTATION:-}" = "$expected" ] ||
            fail "legal review requires the exact BILLWATCH_LEGAL_REVIEW_ATTESTATION phrase after qualified review has actually been completed."
        extra_line="LEGAL_DOCUMENT_VERSION=$legal_version"
        phases='qualified-terms-review-attested,qualified-privacy-review-attested'
        ;;
esac

temp=$(mktemp "$output_parent/.billwatch-external-approval.XXXXXX") ||
    fail "could not create protected temporary evidence."
cleanup()
{
    rm -f "$temp"
}
trap cleanup EXIT HUP INT TERM

{
    printf '%s\n' \
        'VERSION=1' \
        'RESULT=complete' \
        "RELEASE_SHA=$release" \
        "COMPLETED_AT_UTC=$completed_at" \
        "APPROVAL_TYPE=$approval_type" \
        "PASSED_PHASES=$phases"
    [ -z "$extra_line" ] || printf '%s\n' "$extra_line"
} > "$temp"

chmod 600 "$temp"

if ! ln "$temp" "$output" 2>/dev/null; then
    fail "could not publish approval evidence without overwriting an existing file."
fi
rm -f "$temp"
trap - EXIT HUP INT TERM

printf '%s\n' \
    "Recorded release-pinned $approval_type external approval attestation for $release." \
    "This record proves that the explicit attestation was made; it does not independently prove the underlying provider or legal-review claim."
