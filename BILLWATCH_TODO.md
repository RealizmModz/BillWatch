# BillWatch Future TODO

Last updated: 2026-09-02 local time / 2026-09-03 UTC

This file is ordered by priority. Do not start lower-priority product expansion while a P0 production/security defect is open.

## P0 — Role-aware administration release

- [x] Bootstrap the sole production account as Owner out-of-band.
- [x] Confirm production DB contains exactly one Owner assignment.
- [x] Identify why a fresh login still receives 403 from Admin.
- [x] Add Identity role services to API authentication registration.
- [x] Add regression coverage for Owner bearer authorization and normal-user denial.
- [x] Add guarded first-Owner bootstrap tooling for future clean installs.
- [ ] Run the final full build/test/CI gate for the beta-readiness branch.
- [ ] Deploy the role-aware release.
- [ ] Sign out/in and verify `/app/admin` authorizes Owner.
- [ ] Verify a non-staff authenticated user remains denied.
- [ ] Create one controlled short-lived access key.
- [ ] Redeem the key through Subscription and verify the entitlement.
- [ ] Revoke/retire temporary access-key test material.
- [ ] Keep global subscription enforcement OFF until its separate rollout gate is approved.

## P0 — Production smoke test

- [x] Landing page loads over HTTPS.
- [x] Login works through the public website.
- [ ] Registration works through the public website.
- [x] Logout/sign-in cycle works well enough to establish a fresh Web session.
- [x] Session survives normal navigation between Overview and Bills.
- [ ] Access-token refresh works without exposing tokens to JavaScript.
- [x] Overview loads real production state.
- [x] Bills page loads and displays a real discovered Bill Stream.
- [ ] Bill detail loads.
- [ ] Activity loads actual alerts.
- [ ] Mark-alert-read works.
- [ ] Dismiss-alert works.
- [ ] Account page loads through the current release.
- [ ] Transaction page loads through the current release.
- [ ] Account export downloads only user-owned safe JSON.
- [ ] Bank disconnect works through the public Web surface.
- [ ] Cross-user resource IDs return 404.
- [ ] Invalid/expired authentication does not leak API/provider details.

## P0 — Plaid production verification

- [x] Production Plaid environment is intentionally configured on the VPS.
- [x] Successful production bank connection exists.
- [x] Account/transaction sync works: 2 accounts and 518 active transactions were persisted from the active connection.
- [x] Recurring-bill discovery works against real production history: Spotify was discovered from 3 monthly linked transactions.
- [ ] Verify Hosted Link launch again as part of the final beta smoke sequence.
- [ ] Verify popup behavior in major browsers.
- [ ] Verify `RequiresAttention` state and truthful user guidance.
- [ ] Verify update-mode reconnect.
- [ ] Verify disconnect/revocation behavior.
- [ ] Verify Plaid access tokens never appear in browser JavaScript, HTML, logs, or response bodies during runtime smoke testing.

## P0 — Statement intelligence verification

- [ ] Upload a real test PDF.
- [ ] Upload a real JPG/PNG.
- [ ] Reject unsupported extensions and invalid file signatures.
- [ ] Reject files larger than 15 MB.
- [ ] Verify statement-upload rate limiting.
- [ ] Verify Uploaded → Processing → terminal status behavior.
- [ ] Verify OCR path.
- [ ] Verify ReadyForParsing path.
- [ ] Verify Processed state updates only the owning Bill Stream.
- [ ] Verify Failed state is truthful and recoverable.
- [ ] Verify physical storage paths never leave the API.
- [ ] Verify another user cannot access upload/status/file IDs.

## P0 — Production operations

- [x] `.env.production` observed as deployment-account owned with mode 600.
- [x] Public API and Web readiness checks have passed after guarded deployments.
- [x] Add repeatable production permission/exposure/runtime verification tooling.
- [x] Add a combined automated private-beta host verifier.
- [ ] Run the new exposure verifier after the pending role-aware release is deployed.
- [ ] Prove PostgreSQL has no public host binding.
- [ ] Prove API/Web port 8080 has no public host binding.
- [ ] Prove only Caddy exposes 80/443.
- [ ] Verify restart behavior for API, Web, Caddy, and PostgreSQL.
- [ ] Perform one controlled VPS reboot and confirm automatic recovery.
- [ ] Configure `BILLWATCH_PRODUCTION_URL=https://api.billbeacon.net` for the external readiness workflow.
- [ ] Perform a forced readiness-failure notification drill.

## P0 — Backup and recovery

- [x] Enable the daily encrypted backup timer.
- [x] Confirm the timer is enabled/active and has a future run scheduled.
- [x] Verify a fresh encrypted production backup manually.
- [x] Verify a completed Restic snapshot tagged `billwatch-complete`.
- [x] Add repeatable timer/snapshot verification tooling.
- [ ] Independently confirm the Restic backend is operationally off-host, not merely configuration-valid.
- [ ] Perform a clean-host/off-host restore drill using protected production-format data.
- [ ] Verify database, statement files, and Data Protection keys restore together on the clean host.
- [ ] Document the complete operator disaster-recovery procedure after the real drill.
- [ ] Establish retention/immutability controls.
- [ ] Add and test backup-failure alerting.

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

- [x] Keep AI-derived persistence disabled.
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
- [ ] Invite 3–5 trusted testers only after P0 gates close.
- [ ] Track false positives, missed bills, time-to-first-discovery, and explanation usefulness.

## P2 — Business / legal

- [ ] Decide private-beta pricing only after product behavior is trustworthy.
- [ ] Avoid payment infrastructure before users actually need it.
- [ ] Add Terms of Service and Privacy Policy before broader public beta.
- [ ] Clearly state BillWatch does not move money and is not a bank.

## P2 — MAUI release

- [ ] Keep the MAUI project healthy while Web beta is validated.
- [ ] Build release artifacts with `-p:BillWatchApiBaseUrl=https://api.billbeacon.net/` when native beta is scheduled.
- [ ] Preserve `AuthenticationService.GetValidAccessTokenAsync()` for protected services.
- [ ] Do not duplicate API truth in the client.

## Immediate resume point

Finish the `work/beta-readiness-2026-09-02` batch, run one complete build/test/CI gate, deploy it, and prove the production Owner can enter `/app/admin`. Then continue the remaining P0 browser/Plaid/statement/recovery gates before adding another major feature.
