# BillWatch private-beta operator checklist

Do not enable subscription enforcement merely because this checklist exists. Enforcement remains a separate deliberate rollout decision.

## Release

- [ ] Full solution build completes with zero errors and zero warnings where reasonably achievable.
- [ ] Full xUnit suite passes.
- [ ] Pull-request CI/recovery pipeline passes.
- [ ] Guarded production deployment succeeds.
- [ ] `.billwatch-release` matches the intended Git commit.
- [ ] `sh deploy/verify-production.sh /opt/billwatch` passes.

## Authentication and administration

- [ ] Registration succeeds for an intentional test account.
- [ ] Login succeeds.
- [ ] Logout clears the Web session.
- [ ] Access-token refresh survives normal use without exposing tokens to JavaScript.
- [ ] Production Owner receives a role-aware bearer session after fresh login.
- [ ] `/app/admin` authorizes Owner.
- [ ] Non-staff authenticated accounts cannot access admin endpoints.
- [ ] Access-key plaintext is visible once only.
- [ ] Revocation works.
- [ ] Access-key redemption grants the expected entitlement.

## User-owned financial surfaces

- [ ] Overview loads real state.
- [ ] Bills loads real discovered streams.
- [ ] Bill detail loads.
- [ ] Activity loads actual alerts.
- [ ] Mark-read works.
- [ ] Dismiss works.
- [ ] Account loads.
- [ ] Transactions load.
- [ ] Export returns only the current user's safe data.
- [ ] Cross-user IDs return 404 where required.
- [ ] Bank disconnect/revocation works.

## Plaid

- [ ] Hosted Link launches from the public Web site.
- [ ] Successful connection persists accounts and transactions.
- [ ] Recurring discovery runs after transaction sync.
- [ ] RequiresAttention is surfaced truthfully.
- [ ] Update-mode reconnect works.
- [ ] Disconnect works.
- [ ] Plaid tokens never appear in browser-visible content or logs.

## Statements

- [ ] Real PDF upload reaches a truthful terminal state.
- [ ] Real JPG/PNG upload reaches a truthful terminal state.
- [ ] Invalid signatures/extensions are rejected.
- [ ] Files over 15 MB are rejected.
- [ ] OCR path is exercised.
- [ ] Processed statement updates only the owning Bill Stream.
- [ ] Storage paths never leave the API.
- [ ] Cross-user statement IDs are inaccessible.

## Recovery and operations

- [ ] Daily encrypted backup timer is enabled.
- [ ] Fresh encrypted backup succeeds.
- [ ] Restic repository is confirmed off-host.
- [ ] Clean-host restore drill succeeds.
- [ ] Database, statements, and Data Protection keys restore coherently.
- [ ] Backup retention/immutability policy is established.
- [ ] Backup failure alerting is configured and tested.
- [ ] External readiness monitor is configured and a forced-failure notification is proven.
- [ ] Controlled VPS reboot returns BillWatch to healthy automatically.

## Beta entry

- [ ] Subscription enforcement remains OFF unless its separate rollout gate is explicitly approved.
- [ ] AI-derived persistence remains disabled.
- [ ] Internal Beta 0 is run on real bills.
- [ ] False positives/missed bills are reviewed before inviting trusted testers.
- [ ] Terms/Privacy requirements are reviewed before broader public beta.
