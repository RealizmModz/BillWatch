# BillWatch private-beta operator checklist

Do not enable subscription enforcement merely because this checklist exists. Enforcement remains a separate deliberate rollout decision.

## Repeatable authenticated smoke gate

Use `deploy/smoke-private-beta.sh` after a guarded deployment to exercise the non-destructive authenticated production boundary with one credential exchange instead of manually probing each API surface.

Create a temporary password file outside the repository, owned by the operator and mode `600`, then set `BILLWATCH_SMOKE_EMAIL` and `BILLWATCH_SMOKE_PASSWORD_FILE`. Run:

```sh
BILLWATCH_SMOKE_EMAIL='controlled-beta@example.com' \
BILLWATCH_SMOKE_PASSWORD_FILE='/run/user/1000/billwatch-smoke-password' \
BILLWATCH_SMOKE_ADMIN_EXPECTATION='deny' \
sh deploy/smoke-private-beta.sh \
    'https://api.billbeacon.net' \
    'https://billbeacon.net'
```

For the production Owner/Admin account, set `BILLWATCH_SMOKE_ADMIN_EXPECTATION=allow`. For a normal tester, set it to `deny`. The harness verifies login, token refresh, public Web routes, authenticated financial reads, account-export secret/storage boundaries, and the selected admin expectation. Optional known foreign Bill Stream/statement IDs can be supplied through `BILLWATCH_SMOKE_FOREIGN_BILL_STREAM_ID` and `BILLWATCH_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID` to prove ownership-scoped 404 behavior.

Mutations are **off by default**. Do not enable `BILLWATCH_SMOKE_ALLOW_MUTATIONS=true` unless the supplied alert IDs are disposable controlled test fixtures. The smoke harness intentionally does not automate Plaid disconnect, account deletion, statement upload, access-key creation/revocation, or other high-impact actions.

Remove the temporary password file immediately after the smoke gate. Never put production credentials, bearer tokens, Plaid secrets, access keys, or the password file in the repository or shell command arguments.

## Release

- [ ] Full solution build completes with zero errors and zero warnings where reasonably achievable.
- [ ] Full xUnit suite passes.
- [ ] Pull-request CI/recovery pipeline passes.
- [ ] Guarded production deployment succeeds.
- [ ] `.billwatch-release` matches the intended Git commit.
- [ ] Running API/Web containers and the production backup image report the same `org.opencontainers.image.revision` as the intended release.
- [ ] `sh deploy/verify-production.sh /opt/billwatch` passes.

## Authentication and administration

- [ ] Registration succeeds for an intentional test account.
- [ ] Login succeeds.
- [ ] Logout clears the Web session.
- [ ] Access-token refresh survives normal use without exposing tokens to JavaScript.
- [ ] `deploy/smoke-private-beta.sh` passes for a normal tester with `BILLWATCH_SMOKE_ADMIN_EXPECTATION=deny`.
- [ ] Production Owner receives a role-aware bearer session after fresh login.
- [ ] `deploy/smoke-private-beta.sh` passes for Owner/Admin with `BILLWATCH_SMOKE_ADMIN_EXPECTATION=allow`.
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
- [ ] Runtime-readiness watchdog timer is installed, enabled, and active.
- [ ] Fresh encrypted backup succeeds.
- [ ] Restic repository is confirmed off-host.
- [ ] Clean-host restore drill succeeds.
- [ ] Database, statements, and Data Protection keys restore coherently.
- [ ] Repository retention is explicitly enabled at or above 14 daily / 8 weekly / 12 monthly / 3 yearly completed snapshots.
- [ ] Provider-side immutable/Object-Lock/WORM or equivalent protection is configured and recovery from that protected storage has been tested.
- [ ] Backup/runtime failure alerting is configured and a manual `readiness-test` event is observed in the external alert destination.
- [ ] Independent external readiness monitor is configured and a forced-failure notification is proven.
- [ ] Controlled VPS reboot returns Docker, BillWatch, the backup timer, and the runtime-readiness watchdog to healthy automatically.
- [ ] After the controlled reboot, `sh deploy/verify-beta-readiness.sh /opt/billwatch` passes without modifying the verified release marker.

## Beta entry

- [ ] Subscription enforcement remains OFF unless its separate rollout gate is explicitly approved.
- [ ] AI-derived persistence remains disabled.
- [ ] Internal Beta 0 is run on real bills.
- [ ] False positives/missed bills are reviewed before inviting trusted testers.
- [ ] Terms/Privacy requirements are reviewed before broader public beta.
