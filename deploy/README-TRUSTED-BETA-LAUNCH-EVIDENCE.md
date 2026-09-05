# Trusted private-beta launch evidence

BillWatch deliberately separates machine-verifiable acceptance evidence from external decisions that code cannot honestly prove by itself.

A trusted private-beta launch requires all three evidence groups for the exact deployed release:

1. Complete same-release private-beta acceptance evidence from `deploy/verify-private-beta-acceptance-evidence.sh`.
2. A provider-protection attestation recorded only after provider-enforced immutable/Object-Lock/WORM (or equivalent) backup protection is configured **and recovery from that protected storage path has actually succeeded**.
3. A qualified legal-review attestation recorded only after the deployed Terms and Privacy documents have been reviewed and approved for trusted private beta.

The two external records are human attestations. BillWatch validates that an explicit attestation was made, pins it to the deployed release, protects the evidence file, and for legal review pins it to `BillWatchLegalDocuments.CurrentVersion`. It does not claim to independently prove the provider configuration or the quality/sufficiency of legal review.

## Record backup-provider approval

Choose a new absolute evidence path outside the checkout. The recorder refuses symlinks, existing output files, dirty/release-mismatched deployments, and in-checkout evidence.

Only after the protected-path recovery has actually been tested, run:

```sh
BILLWATCH_EXTERNAL_APPROVAL_EVIDENCE_FILE='/secure/billwatch-evidence/backup-approval.env' \
BILLWATCH_BACKUP_PROTECTION_ATTESTATION='I attest that provider-enforced immutable/Object-Lock/WORM or equivalent backup protection is configured and that recovery from the protected storage path succeeded for this deployed release.' \
sh deploy/record-private-beta-external-approval.sh backup /opt/billwatch
```

## Record qualified Terms/Privacy review

The legal recorder reads the legal-document version directly from the deployed release. Do not record this evidence until the exact deployed Terms and Privacy documents have received the review required for the intended trusted-beta audience.

```sh
BILLWATCH_EXTERNAL_APPROVAL_EVIDENCE_FILE='/secure/billwatch-evidence/legal-approval.env' \
BILLWATCH_LEGAL_REVIEW_ATTESTATION='I attest that a qualified review of the deployed BillWatch Terms and Privacy documents is complete and approved for trusted private beta.' \
sh deploy/record-private-beta-external-approval.sh legal /opt/billwatch
```

Changing `BillWatchLegalDocuments.CurrentVersion` automatically makes an older legal approval unusable for the launch gate.

## Verify the trusted-beta launch gate

After the machine acceptance evidence and both external approvals exist:

```sh
BILLWATCH_ACCEPTANCE_EVIDENCE_FILE='/secure/billwatch-evidence/private-beta-acceptance.env' \
BILLWATCH_BACKUP_APPROVAL_EVIDENCE_FILE='/secure/billwatch-evidence/backup-approval.env' \
BILLWATCH_LEGAL_APPROVAL_EVIDENCE_FILE='/secure/billwatch-evidence/legal-approval.env' \
BILLWATCH_TRUSTED_BETA_LAUNCH_EVIDENCE_FILE='/secure/billwatch-evidence/trusted-beta-launch.env' \
sh deploy/verify-trusted-beta-launch-evidence.sh /opt/billwatch
```

The verifier requires all input evidence to be regular non-symlink mode-600 files outside the checkout, complete, same-release, and in the exact expected phase set. The legal approval must also match the currently deployed legal-document version. The optional combined output is created atomically and never overwritten.

A successful launch evidence file means the required technical evidence and explicit external attestations line up for that release and legal version. It is not a substitute for actually configuring immutable storage, actually performing protected-path recovery, or obtaining the required legal review.
