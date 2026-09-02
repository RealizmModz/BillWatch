# BillWatch Future TODO

Last updated: 2026-09-01

This file is ordered by priority. Do not start lower-priority product expansion while a P0 production defect is open.

## P0 — Production smoke test

- [x] Landing page loads over HTTPS.
- [x] Login works through the public website.
- [ ] Registration works.
- [ ] Logout works.
- [x] Session survives normal navigation between Overview and Bills.
- [ ] Access-token refresh works without exposing tokens to JavaScript.
- [x] Overview loads real state.
- [x] Bills page loads.
- [ ] Bill detail loads.
- [ ] Activity loads actual alerts.
- [ ] Mark-alert-read works.
- [ ] Dismiss-alert works.
- [ ] Account page loads.
- [ ] Transaction page loads.
- [ ] Account export downloads only user-owned safe JSON.
- [ ] Bank disconnect works.
- [ ] Cross-user resource IDs return 404.
- [ ] Invalid/expired authentication does not leak API/provider details.

### Resolved production incident — Overview loading skeletons

The initial post-login Overview once remained on its loading skeleton state. Follow-up diagnostics showed:

- the Web container could reach the API readiness endpoint successfully;
- the Blazor Interactive Server WebSocket connected successfully through Caddy;
- no browser console errors were present;
- Overview subsequently loaded the correct synchronized-empty state;
- navigation to Bills and back to Overview remained healthy.

GitHub Issue #1 tracks the incident history. No weakening of authentication, BFF token handling, antiforgery, HTTPS, ownership, AllowedHosts, or trusted-proxy protections was required.

## P0 — Plaid production verification

- [ ] Keep Plaid sandbox until production verification is intentionally scheduled.
- [ ] Verify Hosted Link launches from `billbeacon.net`.
- [ ] Verify popup behavior in major browsers.
- [ ] Verify successful bank connection.
- [ ] Verify first account/transaction sync.
- [ ] Verify recurring-bill discovery.
- [ ] Verify RequiresAttention state.
- [ ] Verify update-mode reconnect.
- [ ] Verify disconnect/revocation behavior.
- [ ] Verify Plaid access tokens never appear in browser JavaScript, HTML, logs, or response bodies.

## P0 — Statement intelligence verification

- [ ] Upload a real test PDF.
- [ ] Upload a real JPG/PNG.
- [ ] Reject unsupported extensions and invalid file signatures.
- [ ] Reject files larger than 15 MB.
- [ ] Verify statement-upload rate limiting.
- [ ] Verify Uploaded → Processing → terminal status behavior.
- [ ] Verify OCR path.
- [ ] Verify ReadyForParsing path.
- [ ] Verify Processed state updates the Bill Stream.
- [ ] Verify Failed state is truthful and recoverable.
- [ ] Verify physical storage paths never leave the API.
- [ ] Verify another user cannot access upload/status/file IDs.

## P0 — Production operations

- [ ] Verify `.env.production` remains mode 600 and outside Git.
- [ ] Verify PostgreSQL is not publicly exposed.
- [ ] Verify API/Web port 8080 is not publicly exposed.
- [ ] Verify only Caddy exposes 80/443.
- [ ] Verify API and Web health endpoints externally and internally.
- [ ] Verify restart behavior for API, Web, Caddy, and PostgreSQL.
- [ ] Perform one controlled VPS reboot and confirm automatic recovery.
- [ ] Configure `BILLWATCH_PRODUCTION_URL=https://api.billbeacon.net` for the external readiness workflow.
- [ ] Perform a forced readiness-failure notification drill.

## P0 — Backup and recovery

- [ ] Enable the daily encrypted backup timer.
- [ ] Confirm the Restic repository is truly off-host.
- [ ] Verify a fresh encrypted backup manually.
- [ ] Perform a clean-host restore drill using real protected production-format data.
- [ ] Verify database, statement files, and Data Protection keys restore together.
- [ ] Document the operator recovery procedure.
- [ ] Establish retention/immutability controls.
- [ ] Add backup-failure alerting.

## P1 — Bill intelligence quality

- [ ] Improve recurring merchant normalization.
- [ ] Improve amount-tolerance and cadence classification.
- [ ] Prevent duplicate recurring Bill Streams.
- [ ] Handle variable monthly, annual, quarterly, and irregular-but-predictable bills.
- [ ] Improve provider/statement matching confidence.
- [ ] Require sufficient evidence before automatic statement attachment.
- [ ] Improve deterministic promotion-expiration detection.
- [ ] Improve fee-added / fee-increase detection.
- [ ] Improve discount-removal detection.
- [ ] Improve usage-driven and one-time-charge differentiation.
- [ ] Preserve monthly + annualized impact everywhere important.

## P1 — AI shadow evaluation

- [ ] Keep AI-derived persistence disabled.
- [ ] Build a private ground-truth statement corpus.
- [ ] Cover at least 5 providers and at least 100 statements before evaluating the existing readiness gate.
- [ ] Include clean PDFs, OCR-heavy scans, utilities, telecom, insurance, subscriptions, promotions, fees, and ambiguous line items.
- [ ] Measure field precision, recall, candidate coverage, provider failures, and false alerts.
- [ ] Do not enable runtime shadow provider calls until the private corpus and cost controls are ready.
- [ ] Passing shadow metrics must not automatically authorize AI persistence.

## P1 — Security review

- [ ] Audit every user-owned controller endpoint for ownership scoping.
- [ ] Review composite `(Id, UserId)` database ownership relationships.
- [ ] Review antiforgery on every BFF mutation.
- [ ] Review API rate limits and partitioning.
- [ ] Review cookie flags and Data Protection persistence.
- [ ] Review trusted reverse-proxy configuration.
- [ ] Search source/logging for token, credential, raw statement, and full account-number exposure.
- [ ] Run dependency and container vulnerability scans.

## P1 — Observability

- [ ] Add safe structured production logs with correlation IDs.
- [ ] Add metrics for bank monitoring, recurring discovery, statement processing, and alert generation.
- [ ] Add external uptime alerting.
- [ ] Add disk/storage/database capacity alerting.
- [ ] Never add financial secrets or raw statement text to telemetry.

## P2 — Notifications and beta

- [ ] Start with a low-cost notification channel, likely email.
- [ ] Support meaningful increase, fee, payment-due, and connection-attention notifications.
- [ ] Add notification preferences and unsubscribe controls.
- [ ] Run internal Beta 0 on real bills.
- [ ] Invite 3–5 trusted testers after P0 gates close.
- [ ] Track false positives, missed bills, time-to-first-discovery, and explanation usefulness.

## P2 — Business / legal

- [ ] Decide private-beta pricing only after product behavior is trustworthy.
- [ ] Avoid payment infrastructure before users actually need it.
- [ ] Add Terms of Service and Privacy Policy before broader public beta.
- [ ] Clearly state BillWatch does not move money and is not a bank.

## P2 — MAUI release

- [ ] Keep the MAUI project healthy while web beta is validated.
- [ ] Build release artifacts with `-p:BillWatchApiBaseUrl=https://api.billbeacon.net/`.
- [ ] Preserve `AuthenticationService.GetValidAccessTokenAsync()` for protected services.
- [ ] Do not duplicate API truth in the client.

## Immediate resume point

Continue the production smoke test with the authenticated Activity and Account surfaces before adding another major product feature.
