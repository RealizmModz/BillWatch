# BillWatch Current Context

Last updated: 2026-09-05

## Authority / continuation rules

This is the durable BillWatch development handoff. Current source wins over this file for implementation details.

- Stop immediately for compile/runtime/test/CI/deployment failures caused by current work, destructive migration risk, genuine security problems, or unresolved architecture uncertainty.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to pass a check.
- Work in large coherent slices; hourly continuation is not a commit boundary.
- Keep the current work on the same draft PR/feature branch. Do not deploy the feature branch directly to production or merge before the full CI/container/recovery gate is green.
- Prefer useful code over repeated audits or synthetic acceptance artifacts. Do not start lower-priority expansion while a genuine P0 production/security gate is still open.

## Product promise

**Know when your bills change — and why.**

BillWatch is transaction-first. Bank transactions discover recurring bills. Provider statements/evidence explain why bills changed. AI may produce structured candidate facts, but deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final persistence/alert decisions. AI output is never evidence by itself.

## Repository / stack

Repository: `RealizmModz/BillWatch`

Branch: `work/p0-beta-verification-2026-09-04`

Draft PR: #44, `P0 private-beta security verification`, targeting `master`.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://billbeacon.net`
Public API: `https://api.billbeacon.net`
Production path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side/protected at rest.
- Web bearer/refresh tokens remain inside encrypted HttpOnly BFF state and are not intentionally exposed to browser JavaScript.
- User financial resources and statements remain ownership-scoped; cross-user IDs normally return 404 where appropriate.
- Statement storage paths never leave the API; signature/type/size validation remains enforced.
- Financial/auth API and BFF responses remain no-store.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted reverse-proxy configuration.
- Never log raw statements, full account numbers, auth/Plaid tokens, passwords, provider/database/Restic secrets, or private operations webhooks.
- AI-derived persistence remains disabled; deterministic extraction remains production persistence.

## Verified P0/private-beta work on PR #44

The branch now includes and regression-tests the following coherent security/readiness slices:

### Identity, Web/BFF, and ownership

- Stripe webhook 256 KB body cap, chunked/unknown-length protection, and fail-closed invalid signatures.
- Centralized Web antiforgery, security-header, and no-store boundaries.
- Anonymous/sensitive endpoint authorization and rate-limit coverage, including user-partitioned authenticated limits.
- Owner/Admin policy behavior, role-claim freshness, and controlled access-key create/list/redeem/exhaust/revoke privilege boundaries.
- Versioned private-beta Terms/Privacy acceptance across Web, MAUI, and direct API registration, including registration-size limits.
- Account deletion reauthentication/2FA/staff-role protections, Plaid revoke-first behavior, crash-safe statement quarantine/reconciliation, and owned-data erasure coverage.
- Deployed-release disposable-account deletion proof with explicit destructive opt-in and release-pinned metadata-only evidence.
- Server-side BFF access-token refresh regression proof: proactive refresh, one refresh/retry on upstream 401, rotated-token persistence in the authenticated server session, no browser token disclosure, and fail-closed sign-out on refresh failure.
- Objective cross-user Web/BFF ownership smoke that authenticates a second controlled identity, derives a real foreign-owned bill-stream/statement-upload pair from that account export, and proves the primary identity receives 404 for those exact foreign resources.

### Plaid, statements, subscriptions, and beta acceptance

- Plaid `RequiresAttention` classification/persistence, repair-state retention, retry stopping, safe provider disconnect, and ownership isolation.
- Statement PDF/JPG/JPEG/PNG signature validation, upload/status/download ownership, terminal-state semantics, native/scanned OCR regression coverage, and storage-path secrecy.
- Guarded direct API, authenticated Web/BFF, Owner/Admin, access-key, Plaid, statement lifecycle, statement semantic-review, subscription lifecycle, and disposable account-deletion smoke/proof harnesses.
- Subscription rollout preflight while global subscription enforcement remains OFF.
- Internal Beta 0 runner requiring release-matched account-deletion evidence plus admin authorization, access-key, Plaid, statement lifecycle, statement semantic-review, and subscription gates for a complete result.
- Two-phase release-pinned Plaid Hosted Link observation proof: explicit human completion followed by objective server-side completion, connection-scoped sync, Active-state verification, and metadata-only evidence.

### Backup, recovery, operations, and evidence

- Encrypted Restic backup/restore verification and guarded clean-host recovery drill with isolated PostgreSQL and no production volumes.
- Backup trust separation: routine capture remains append-only at the BillWatch command boundary; delete-capable retention maintenance requires separate trusted-host authority and explicit maintenance opt-in.
- Runtime watchdog, release-integrity checks, metadata-only operations alerts, independent external readiness alerts, and controlled reboot pre/postflight proof.
- Release-pinned clean-host recovery and controlled-reboot evidence with a same-release technical-evidence verifier.
- Two-phase release-pinned alert-observation proof requiring the same random challenge to be observed in both independent destinations before evidence can be finalized.
- Same-release private-beta acceptance verifier correlating machine technical evidence, alert-observation evidence, and Plaid-observation evidence.
- Trusted private-beta launch evidence gate requiring complete machine acceptance plus explicit same-release provider-immutability/protected-recovery and qualified Terms/Privacy review attestations.
- Legal approval evidence is additionally pinned to `BillWatchLegalDocuments.CurrentVersion`; human approval records are intentionally labeled attestations rather than independent machine proof.

## Definitive green baseline

Commit `f37ed2f30a0c7fe1597ff4e24b3c36260343afbb` (`Fix cross-user Web smoke login fixture`) passed BillWatch CI #411 completely on 2026-09-05.

CI #411 validates the repaired objective cross-user Web/BFF ownership smoke on top of all prior P0 work. The complete gate includes Release build, EF pending-model verification, full xUnit suite, production/beta operation regression suites, production API/Web images, HTTPS readiness, HTTP security boundaries, release-label verification, encrypted backup creation, isolated PostgreSQL/statement/Data Protection restore, and post-recovery API readiness.

The preceding CI #410 failure was confined to the new shell test fixture: its fake curl matched `/auth/login` against the broader login-page case. Production authentication behavior was not changed; commit `f37ed2f30a0c7fe1597ff4e24b3c36260343afbb` corrected the fixture ordering and #411 proved the repair.

## Current machine-verifiable P0 position

No current-work compile/test/CI failure is open on the definitive green baseline above.

Most remaining P0 items are now **real-environment acceptance gates**, not missing generic application code. Do not manufacture additional scripts merely to turn human/provider facts into apparent machine proofs. In particular:

- provider-enforced immutable/Object-Lock/WORM/equivalent backup behavior cannot be truthfully implemented or claimed until the actual backup provider and its retention/delete/version semantics are known;
- qualified legal review cannot be replaced by application code;
- human Plaid Hosted Link behavior and alert receipt require real observation;
- a controlled reboot requires an actual manual reboot between the existing guarded preflight/postflight phases;
- representative statement semantic accuracy requires comparison against operator-known facts.

## Production/rollout rules

- Global subscription enforcement remains OFF until its separate rollout gate is deliberately approved.
- Staff roles do not grant access to another user's financial evidence.
- AI-derived persistence remains disabled.
- Startup EF migrations mean production remains one API instance until migration ownership is redesigned.
- Never run `docker compose down --volumes` against production.
- Beta Terms/Privacy are operational drafts, not qualified legal review.
- PR #44 stays draft/unmerged until the exact final head has a complete green CI/container/recovery gate and the intended release is ready for guarded deployment.

## Remaining real-environment private-beta gates

Before trusted external beta invitations:

- guarded-deploy the final green release from the normal release path; never deploy this feature branch directly;
- run authenticated direct API/Web-BFF/admin/access-key/Plaid/statement/subscription smoke with controlled identities and fixtures;
- run the objective cross-user Web/BFF ownership smoke with a second controlled identity that owns a real controlled statement fixture;
- run the disposable account-deletion proof and feed its same-release evidence into Internal Beta 0;
- complete the human Plaid Hosted Link/update-mode flow and finalize the release-pinned Plaid observation proof after Active/sync verification;
- review representative PDF/scanned-PDF/JPG/PNG extraction/OCR fields and bill-change explanations against operator-known facts;
- run clean-host restore against the actual off-host repository and record same-release recovery evidence;
- configure provider-enforced immutable/Object-Lock/WORM/equivalent protection, prove recovery from that protected path, and only then record the explicit same-release backup approval attestation;
- run the alert-observation proof and personally confirm the challenge in both independent destinations;
- perform controlled reboot preflight, manual reboot, and postflight, then record same-release reboot evidence;
- combine same-release technical, alert, and Plaid evidence with the private-beta acceptance verifier;
- complete Internal Beta 0 on real controlled bills with explicit expected subscription state where known;
- obtain qualified review of the exact deployed Terms/Privacy version and record the legal approval attestation;
- run the trusted-beta launch evidence verifier only after every underlying real-environment fact above is genuinely complete.

## Immediate resume point

1. Treat `f37ed2f30a0c7fe1597ff4e24b3c36260343afbb` / CI #411 as the latest definitive green **code** baseline unless a newer exact head has itself passed the complete gate.
2. If the context-only refresh commit is newer than that baseline, do not mistake documentation-only head movement for a newly verified runtime baseline; use the CI result for that exact head when it exists.
3. Do not start P1 product expansion while the real-environment P0 gates above remain open unless a concrete P0 defect discovered during acceptance requires code changes.
4. On the next implementation slice, prefer fixing an observed production/acceptance defect or a clearly missing application-level P0 behavior. Avoid redundant ownership/security audits and avoid inventing provider/legal proofs.
5. Keep PR #44 draft and unmerged. Preserve all work on `work/p0-beta-verification-2026-09-04` until the private-beta release gate is actually ready.
