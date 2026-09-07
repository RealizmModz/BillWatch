# BillWatch external readiness alerting

BillWatch's GitHub-hosted production readiness workflow probes the public API and Web origins from outside the production host. A failed probe must be able to alert through an explicit BillWatch-controlled delivery path rather than relying only on generic GitHub Actions notifications.

## Configure the protected webhook

Create the repository Actions secret `BILLWATCH_READINESS_ALERT_WEBHOOK_URL` with the private HTTPS webhook used for external readiness alerts.

Requirements:

- The value must be an `https://` URL.
- Do not commit the URL to the repository, workflow YAML, tickets, screenshots, or logs.
- Prefer an alert destination that remains reachable when the BillWatch production host is unavailable.
- The receiving endpoint should accept a small JSON POST body.

The sender emits metadata only:

- source: `billwatch-external-readiness`
- event
- target (`API` or `Web`)
- GitHub workflow run ID
- UTC occurrence timestamp

It does not attach application logs, statement data, account data, credentials, tokens, database information, or the private webhook URL.

`deploy/send-readiness-alert.sh` stores the webhook URL only in a mode-600 temporary curl configuration, constrains curl to HTTPS/TLS 1.2+, refuses redirects, and removes that temporary file on exit.

## Normal monitoring

`.github/workflows/production-monitor.yml` checks both of these public readiness origins every 15 minutes:

- `https://api.billbeacon.net/health/ready`
- `https://billbeacon.net/health/ready`

If a readiness job fails, its final alert step invokes the external alert sender. The workflow remains failed; alert delivery never converts an unhealthy readiness result into success.

## Controlled forced-failure proof

Run this proof only after the intended production release is deployed and healthy.

1. Confirm both normal readiness jobs are passing.
2. Manually dispatch **BillWatch Production Readiness** with `force_failure=true`.
3. Confirm the API readiness probe succeeds first.
4. Confirm the API matrix job then fails at the intentional controlled drill step.
5. Confirm the `Deliver independent readiness failure alert` step runs.
6. Confirm the expected metadata-only alert is actually observed at the external destination.
7. Record the workflow run ID and observation time in the private operator record. Do not copy the webhook URL or any secret into that record.

The forced-failure run is expected to be red. Its purpose is to prove that an independently hosted monitoring failure produces an externally observed alert.

Do not mark the private-beta external alert-delivery gate complete merely because the sender has regression coverage or because the workflow contains the alert step. Completion requires an observed real delivery from a controlled forced-failure run.
