# Private-beta alert observation proof

BillWatch treats webhook acceptance and human observation as different facts. A successful HTTP request to an alert destination is not enough to claim that an operator actually saw the notification.

This proof therefore has two explicit phases tied to one verified release.

## 1. Send two independent proof alerts

Use a protected evidence directory outside the BillWatch checkout. The production operations webhook remains in `.env.production`; the external readiness webhook must be supplied separately and must be a different HTTPS destination.

```sh
export BILLWATCH_ALERT_PROOF_ALLOW_SEND=true
export BILLWATCH_ALERT_PROOF_PENDING_FILE=/secure/billwatch/alert-proof.pending
export BILLWATCH_ALERT_PROOF_EVIDENCE_FILE=/secure/billwatch/alert-proof.state
export BILLWATCH_READINESS_ALERT_WEBHOOK_URL='https://independent-monitor.example/secret-path'

sh /opt/billwatch/deploy/run-alert-observation-proof.sh send /opt/billwatch
```

The script requires a clean checkout whose Git HEAD exactly matches `.billwatch-release`, protected production configuration, two distinct alert destinations, and explicit send opt-in. It generates a random 128-bit challenge, sends that challenge through both the production operations alert path and the independent external-readiness alert path, and writes only a mode-600 pending record outside the checkout. It does not write completion evidence at this stage.

Observe both destinations and verify that the same challenge printed by the command appears in both proof messages.

## 2. Record the observation

After personally observing both matching proof messages, provide the challenge and exact confirmation phrase:

```sh
export BILLWATCH_ALERT_PROOF_CHALLENGE='<challenge shown by send phase>'
export BILLWATCH_ALERT_PROOF_CONFIRMATION='I observed both BillWatch alert proof messages'

sh /opt/billwatch/deploy/run-alert-observation-proof.sh confirm /opt/billwatch
```

Confirmation refuses a missing, symlinked, weakly-permissioned, malformed, already-consumed, or cross-release pending record. It also refuses a dirty or release-mismatched checkout, a wrong challenge, an inexact confirmation phrase, or an existing evidence destination.

On success it atomically publishes a new mode-600 evidence file containing only the format version, result, release SHA, UTC completion time, and these phases:

```text
operations-alert-observed,external-readiness-alert-observed
```

The challenge, webhook URLs, response bodies, credentials, and operator identity are intentionally excluded from completed evidence.

## What this proves

The completed record is an auditable **operator attestation** that both release-correlated proof messages were observed in separate alert destinations. It is stronger than recording HTTP 2xx alone, but it is not an independent mathematical or provider-side proof of notification delivery. Do not describe it as such.

Keep this evidence with the same-release private-beta technical evidence and legal/provider launch records. The existing technical-evidence verifier remains intentionally machine-verifiable-only and does not silently convert this human attestation into a machine proof.
