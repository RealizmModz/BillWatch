#!/bin/sh
set -eu
umask 077

deployment_directory=${1:-}
acceptance=${BILLWATCH_ACCEPTANCE_EVIDENCE_FILE:-}
backup=${BILLWATCH_BACKUP_APPROVAL_EVIDENCE_FILE:-}
legal=${BILLWATCH_LEGAL_APPROVAL_EVIDENCE_FILE:-}
output=${BILLWATCH_TRUSTED_BETA_LAUNCH_EVIDENCE_FILE:-}

fail()
{
    printf '%s\n' "Trusted private-beta launch gate failed: $1" >&2
    exit 1
}

[ -n "$deployment_directory" ] && [ -d "$deployment_directory" ] ||
    fail "deployment directory is required and must exist."

root_dir=$(CDPATH= cd -- "$deployment_directory" && pwd)
release_file="$root_dir/.billwatch-release"
[ -f "$release_file" ] && [ ! -L "$release_file" ] ||
    fail ".billwatch-release must be a regular non-symlink file."
release=$(tr -d '\r\n' < "$release_file")
printf '%s' "$release" | grep -Eq '^[0-9a-f]{40}$' ||
    fail ".billwatch-release must contain one 40-character lowercase Git SHA."

command -v git >/dev/null 2>&1 || fail "git is required."
head=$(git -C "$root_dir" rev-parse HEAD 2>/dev/null) || fail "deployment directory is not a Git checkout."
[ "$head" = "$release" ] || fail "checked-out commit does not match .billwatch-release."
[ -z "$(git -C "$root_dir" status --porcelain --untracked-files=normal)" ] || fail "deployment checkout must be clean."

read_value()
{
    file=$1
    key=$2
    count=$(grep -c "^$key=" "$file" || true)
    [ "$count" -eq 1 ] || fail "$(basename -- "$file") must contain exactly one $key field."
    sed -n "s/^$key=//p" "$file"
}

verify_common()
{
    file=$1
    label=$2
    case "$file" in
        /*) ;;
        *) fail "$label evidence path must be absolute." ;;
    esac
    case "$file" in
        "$root_dir"|"$root_dir"/*) fail "$label evidence must be stored outside the deployment checkout." ;;
    esac
    [ -f "$file" ] && [ ! -L "$file" ] || fail "$label evidence must be a regular non-symlink file."
    mode=$(stat -c '%a' "$file" 2>/dev/null) || fail "could not inspect $label evidence permissions."
    [ "$mode" = 600 ] || fail "$label evidence must have mode 600."
    [ "$(read_value "$file" VERSION)" = 1 ] || fail "$label evidence version is unsupported."
    [ "$(read_value "$file" RESULT)" = complete ] || fail "$label evidence is not complete."
    [ "$(read_value "$file" RELEASE_SHA)" = "$release" ] || fail "$label evidence belongs to a different release."
}

verify_common "$acceptance" 'private-beta acceptance'
[ "$(read_value "$acceptance" PASSED_PHASES)" = 'machine-technical,alert-observation,plaid-observation' ] ||
    fail "private-beta acceptance evidence has unexpected phases."

verify_common "$backup" 'backup approval'
[ "$(read_value "$backup" APPROVAL_TYPE)" = backup ] || fail "backup approval evidence has the wrong approval type."
[ "$(read_value "$backup" PASSED_PHASES)" = 'provider-immutable-backup-attested,protected-backup-recovery-attested' ] ||
    fail "backup approval evidence has unexpected phases."

verify_common "$legal" 'legal approval'
[ "$(read_value "$legal" APPROVAL_TYPE)" = legal ] || fail "legal approval evidence has the wrong approval type."
[ "$(read_value "$legal" PASSED_PHASES)" = 'qualified-terms-review-attested,qualified-privacy-review-attested' ] ||
    fail "legal approval evidence has unexpected phases."

legal_source="$root_dir/BillWatch.Core/Legal/BillWatchLegalDocuments.cs"
[ -f "$legal_source" ] && [ ! -L "$legal_source" ] || fail "BillWatchLegalDocuments.cs must be a regular non-symlink file."
legal_version=$(awk '/CurrentVersion[[:space:]]*=/{getline; line=$0; gsub(/[[:space:]"]/, "", line); sub(/;.*/, "", line); print line; exit}' "$legal_source")
[ -n "$legal_version" ] || fail "could not determine the deployed legal document version."
[ "$(read_value "$legal" LEGAL_DOCUMENT_VERSION)" = "$legal_version" ] ||
    fail "legal approval evidence is for a different legal document version."

if [ -n "$output" ]; then
    case "$output" in
        /*) ;;
        *) fail "BILLWATCH_TRUSTED_BETA_LAUNCH_EVIDENCE_FILE must be an absolute path outside the deployment checkout." ;;
    esac
    case "$output" in
        "$root_dir"|"$root_dir"/*) fail "launch evidence must be stored outside the deployment checkout." ;;
    esac
    [ ! -e "$output" ] && [ ! -L "$output" ] || fail "launch evidence destination already exists; evidence is never overwritten."
    output_parent=$(dirname -- "$output")
    [ -d "$output_parent" ] || fail "launch evidence parent directory does not exist."
    temp=$(mktemp "$output_parent/.billwatch-trusted-beta-launch.XXXXXX") || fail "could not create protected temporary evidence."
    cleanup(){ rm -f "$temp"; }
    trap cleanup EXIT HUP INT TERM
    {
        printf '%s\n' \
            'VERSION=1' \
            'RESULT=complete' \
            "RELEASE_SHA=$release" \
            "LEGAL_DOCUMENT_VERSION=$legal_version" \
            "COMPLETED_AT_UTC=$(date -u '+%Y-%m-%dT%H:%M:%SZ')" \
            'PASSED_PHASES=machine-acceptance,provider-immutable-backup-attestation,qualified-legal-review-attestation'
    } > "$temp"
    chmod 600 "$temp"
    if ! ln "$temp" "$output" 2>/dev/null; then
        fail "could not publish launch evidence without overwriting an existing file."
    fi
    rm -f "$temp"
    trap - EXIT HUP INT TERM
fi

printf '%s\n' \
    "Trusted private-beta launch evidence is complete for release $release and legal document version $legal_version." \
    "External approval records are human attestations; this gate validates their identity, release/version binding, permissions, and completeness but does not independently prove their underlying claims."
