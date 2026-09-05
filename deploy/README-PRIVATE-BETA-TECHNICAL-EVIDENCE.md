# Private-beta technical evidence

BillWatch keeps the highest-impact private-beta proofs separate so one harness cannot silently weaken another. This package adds release-pinned metadata-only proof records for the clean-host recovery and controlled reboot drills, then verifies those records alongside a complete Internal Beta 0 result.

## Recovery proof

Run the wrapper on the clean recovery host instead of invoking the drill directly:

```sh
export BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE=/secure/billwatch/recovery-proof.state
sh /opt/billwatch/deploy/run-clean-host-recovery-proof.sh /secure/billwatch/recovery.env
```

The underlying clean-host recovery drill retains all of its existing safeguards: protected environment ownership/permissions, exact clean Git release, explicit restore opt-in, off-host Restic repository, isolated PostgreSQL, no production volumes, encrypted snapshot verification, and cleanup. Evidence is written only after that drill succeeds.

## Controlled reboot proof

Use the wrapper for both phases and keep the same evidence destination configured across the manual reboot:

```sh
export BILLWATCH_REBOOT_DRILL_ALLOW=true
export BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE=/secure/billwatch/reboot-proof.state

sh /opt/billwatch/deploy/run-controlled-reboot-proof.sh preflight /opt/billwatch
# Reboot the host manually.
sh /opt/billwatch/deploy/run-controlled-reboot-proof.sh postflight /opt/billwatch
```

Preflight never writes completion evidence. The underlying reboot drill still proves a changed kernel boot ID, unchanged release, Docker auto-start, and complete private-beta host prerequisites before the wrapper records postflight evidence.

## Verify one release

After a complete Internal Beta 0 run and the two recovery proofs:

```sh
export BILLWATCH_BETA0_EVIDENCE_FILE=/secure/billwatch/beta0.state
export BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE=/secure/billwatch/recovery-proof.state
export BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE=/secure/billwatch/reboot-proof.state
export BILLWATCH_TECHNICAL_EVIDENCE_FILE=/secure/billwatch/technical-proof.state

sh /opt/billwatch/deploy/verify-private-beta-technical-evidence.sh /opt/billwatch
```

The verifier requires a clean checkout whose Git HEAD exactly matches `.billwatch-release`. Every input must be a regular mode-600 non-symlink file outside the deployment checkout, must report `RESULT=complete`, must match that same release SHA, and must contain the exact expected phase set. It refuses to overwrite an existing combined evidence file.

All proof files contain only format version, result, release SHA, UTC completion time, and phase names. They intentionally exclude credentials, tokens, URLs, repository identifiers, account IDs, provider data, statements, response bodies, boot IDs, and other sensitive runtime information.

A successful technical evidence verification is **not** a private-beta launch authorization by itself. Human Plaid Hosted Link/update-mode observation, observed external alert delivery, provider-enforced immutable/Object-Lock/WORM backup protection and recovery from that protected path, and qualified Terms/Privacy review remain separate gates.
