#!/bin/sh
set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
record_script="$root_dir/deploy/record-private-beta-external-approval.sh"
verify_script="$root_dir/deploy/verify-trusted-beta-launch-evidence.sh"
temp=$(mktemp -d)
cleanup(){ rm -rf "$temp"; }
trap cleanup EXIT HUP INT TERM

release=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
deployment="$temp/deployment"
evidence="$temp/evidence"
fakebin="$temp/bin"
mkdir -p "$deployment/BillWatch.Core/Legal" "$evidence" "$fakebin"
printf '%s\n' "$release" > "$deployment/.billwatch-release"
cat > "$deployment/BillWatch.Core/Legal/BillWatchLegalDocuments.cs" <<'CS'
namespace BillWatch.Core.Legal;
public static class BillWatchLegalDocuments
{
    public const string CurrentVersion =
        "2026-09-04-beta";
}
CS
cat > "$fakebin/git" <<'SH'
#!/bin/sh
if [ "${3:-}" = rev-parse ] && [ "${4:-}" = HEAD ]; then
    printf '%s\n' "${FAKE_RELEASE:?}"
    exit 0
fi
if [ "${3:-}" = status ]; then
    exit 0
fi
exit 2
SH
chmod 755 "$fakebin/git"
export PATH="$fakebin:$PATH"
export FAKE_RELEASE="$release"

backup="$evidence/backup.env"
legal="$evidence/legal.env"
acceptance="$evidence/acceptance.env"
launch="$evidence/launch.env"

BILLWATCH_EXTERNAL_APPROVAL_EVIDENCE_FILE="$backup" \
BILLWATCH_BACKUP_PROTECTION_ATTESTATION='I attest that provider-enforced immutable/Object-Lock/WORM or equivalent backup protection is configured and that recovery from the protected storage path succeeded for this deployed release.' \
sh "$record_script" backup "$deployment" >/dev/null

[ "$(stat -c '%a' "$backup")" = 600 ]
grep -qx 'APPROVAL_TYPE=backup' "$backup"
grep -qx 'PASSED_PHASES=provider-immutable-backup-attested,protected-backup-recovery-attested' "$backup"

if BILLWATCH_EXTERNAL_APPROVAL_EVIDENCE_FILE="$evidence/missing-attestation.env" \
    sh "$record_script" backup "$deployment" >/dev/null 2>&1; then
    printf '%s\n' 'backup recorder accepted missing explicit attestation' >&2
    exit 1
fi

BILLWATCH_EXTERNAL_APPROVAL_EVIDENCE_FILE="$legal" \
BILLWATCH_LEGAL_REVIEW_ATTESTATION='I attest that a qualified review of the deployed BillWatch Terms and Privacy documents is complete and approved for trusted private beta.' \
sh "$record_script" legal "$deployment" >/dev/null

grep -qx 'LEGAL_DOCUMENT_VERSION=2026-09-04-beta' "$legal"
grep -qx 'PASSED_PHASES=qualified-terms-review-attested,qualified-privacy-review-attested' "$legal"

cat > "$acceptance" <<EOF
VERSION=1
RESULT=complete
RELEASE_SHA=$release
COMPLETED_AT_UTC=2026-09-05T00:00:00Z
PASSED_PHASES=machine-technical,alert-observation,plaid-observation
EOF
chmod 600 "$acceptance"

BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$acceptance" \
BILLWATCH_BACKUP_APPROVAL_EVIDENCE_FILE="$backup" \
BILLWATCH_LEGAL_APPROVAL_EVIDENCE_FILE="$legal" \
BILLWATCH_TRUSTED_BETA_LAUNCH_EVIDENCE_FILE="$launch" \
sh "$verify_script" "$deployment" >/dev/null

[ "$(stat -c '%a' "$launch")" = 600 ]
grep -qx "RELEASE_SHA=$release" "$launch"
grep -qx 'LEGAL_DOCUMENT_VERSION=2026-09-04-beta' "$launch"
grep -qx 'PASSED_PHASES=machine-acceptance,provider-immutable-backup-attestation,qualified-legal-review-attestation' "$launch"

if BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$acceptance" \
    BILLWATCH_BACKUP_APPROVAL_EVIDENCE_FILE="$backup" \
    BILLWATCH_LEGAL_APPROVAL_EVIDENCE_FILE="$legal" \
    BILLWATCH_TRUSTED_BETA_LAUNCH_EVIDENCE_FILE="$launch" \
    sh "$verify_script" "$deployment" >/dev/null 2>&1; then
    printf '%s\n' 'launch verifier overwrote existing evidence' >&2
    exit 1
fi

bad_legal="$evidence/bad-legal.env"
cp "$legal" "$bad_legal"
sed -i 's/LEGAL_DOCUMENT_VERSION=.*/LEGAL_DOCUMENT_VERSION=old-version/' "$bad_legal"
chmod 600 "$bad_legal"
if BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$acceptance" \
    BILLWATCH_BACKUP_APPROVAL_EVIDENCE_FILE="$backup" \
    BILLWATCH_LEGAL_APPROVAL_EVIDENCE_FILE="$bad_legal" \
    sh "$verify_script" "$deployment" >/dev/null 2>&1; then
    printf '%s\n' 'launch verifier accepted stale legal-document approval' >&2
    exit 1
fi

bad_backup="$evidence/bad-backup.env"
cp "$backup" "$bad_backup"
sed -i 's/^RELEASE_SHA=.*/RELEASE_SHA=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/' "$bad_backup"
chmod 600 "$bad_backup"
if BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$acceptance" \
    BILLWATCH_BACKUP_APPROVAL_EVIDENCE_FILE="$bad_backup" \
    BILLWATCH_LEGAL_APPROVAL_EVIDENCE_FILE="$legal" \
    sh "$verify_script" "$deployment" >/dev/null 2>&1; then
    printf '%s\n' 'launch verifier accepted cross-release backup approval' >&2
    exit 1
fi

chmod 644 "$backup"
if BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$acceptance" \
    BILLWATCH_BACKUP_APPROVAL_EVIDENCE_FILE="$backup" \
    BILLWATCH_LEGAL_APPROVAL_EVIDENCE_FILE="$legal" \
    sh "$verify_script" "$deployment" >/dev/null 2>&1; then
    printf '%s\n' 'launch verifier accepted weak approval evidence permissions' >&2
    exit 1
fi

printf '%s\n' 'Trusted private-beta launch evidence regression tests passed.'
