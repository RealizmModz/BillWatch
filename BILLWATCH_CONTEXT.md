# BillWatch Current Context

Last updated: 2026-09-14

## Authority / continuation rules

This is the durable BillWatch development handoff. Current source, exact-head CI, and verified production state win over this file for implementation/runtime truth.

- Stop immediately for compile/runtime/test/CI/deployment failures caused by current work, destructive migration risk, genuine security problems, or unresolved architecture uncertainty.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to pass a check.
- Work in coherent slices. Do not merge a feature branch until its exact final head has passed the complete CI/container/recovery gate.
- Never deploy a feature branch directly to production. Use the guarded release path from a verified `master` commit.
- Prefer useful code or real acceptance work over repeated audits and synthetic acceptance artifacts.

## Product promise

**Know when your bills change — and why.**

BillWatch is transaction-first. Bank transactions discover recurring bills. Provider statements/evidence explain why bills changed. AI may produce structured candidate facts, but deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final persistence/alert decisions. AI output is never evidence by itself.

## Repository / stack

Repository: `RealizmModz/BillWatch`

Default/release branch: `master`
Active integration branch: `development`

### Current GitHub baseline

- `master`: `03d757a6328aa237c719012757ad95a614228d9c`
- `development`: `a5fcdf1b192aa2d2c3960a71797d42730b1dc32f`
- PR #85 promoted the Slack-compatible readiness-alert payload to `master`.
- PR #86, **Add secure 2FA support to private-beta smoke harness**, merged into `development` as `a5fcdf1b192aa2d2c3960a71797d42730b1dc32f`.
- PR #86 feature head was `1a32918cebd1e597bd87e48c1765db04e6459326`.
- BillWatch CI #521 passed successfully on that exact PR #86 feature head. All three jobs were green: backend build/tests, MAUI Android build, and Linux production container/recovery gate.
- No post-merge CI run is being claimed for merge commit `a5fcdf1...`; the verified automated gate is the exact PR #86 feature head.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, ASP.NET Core Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://billbeacon.net`
Public API: `https://api.billbeacon.net`
Production path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side/protected at rest.
- Web bearer/refresh tokens remain inside encrypted HttpOnly BFF state and are not intentionally exposed to browser JavaScript.
- External provider ID tokens/proofs must not be exposed to browser JavaScript.
- User financial resources and statements remain ownership-scoped; cross-user IDs normally return 404 where appropriate.
- Staff roles do not grant access to another user's financial evidence.
- Statement storage paths never leave the API; signature/type/size validation remains enforced.
- Financial/auth API and BFF responses remain no-store.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted reverse-proxy configuration.
- Never log raw statements, full account numbers, auth/Plaid/provider tokens, passwords, recovery codes, provider/database/Restic secrets, or private operations webhooks.
- AI-derived persistence remains disabled; deterministic extraction remains production persistence.

## Current verified production state

The last explicitly verified guarded production deployment is still:

`71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`

Do **not** claim production has been deployed to current `master` (`03d757a...`) unless a new guarded deployment is actually verified.

Production verification at `71f71a...` included configuration/release integrity, permissions/exposure boundaries, API/Web/database/edge health, readiness, HTTP security boundaries, antiforgery/protected logout, and encrypted Restic recovery-point creation.

Production host maintenance and the controlled reboot proof were completed for that release. Docker Buildx 0.30.1 and Compose 2.40.3 were verified, Docker returned after reboot, the release marker/Git HEAD remained unchanged, and post-reboot production verification passed.

## Verified P0/private-beta code position

The repository contains regression-tested P0 work covering identity/BFF isolation, ownership, Plaid state handling, statement validation/processing, subscription rollout gating, backup/recovery, operations alerts, release integrity, controlled reboot evidence, and private-beta evidence correlation.

Important verified slices include:

- centralized Web antiforgery, security headers, no-store boundaries, sensitive endpoint authorization, and user-partitioned authenticated rate limits;
- Owner/Admin and access-key privilege boundaries with role-claim freshness;
- versioned private-beta Terms/Privacy acceptance across Web, MAUI, and direct API registration;
- account deletion reauthentication/2FA/staff-role protections, Plaid revoke-first behavior, crash-safe statement quarantine/reconciliation, owned-data erasure, and deployed-release disposable-account deletion proof support;
- server-side BFF access-token refresh with rotated-token persistence and fail-closed sign-out on refresh failure;
- objective cross-user Web/BFF ownership smoke using a second controlled identity and real foreign-owned resources;
- external sign-in support and BillWatch 2FA/recovery-code flows;
- Plaid `RequiresAttention` classification/persistence and ownership isolation;
- PDF/JPG/JPEG/PNG statement signature validation, upload/status/download ownership, terminal-state semantics, OCR coverage, and storage-path secrecy;
- guarded direct API, authenticated Web/BFF, Owner/Admin, access-key, Plaid, statement lifecycle, statement semantic-review, subscription lifecycle, and account-deletion smoke/proof harnesses;
- Internal Beta 0 release-pinned acceptance runner;
- release-pinned Plaid Hosted Link observation proof;
- encrypted Restic backup/restore verification and guarded clean-host recovery support;
- append-only routine backup boundary, runtime watchdog, release-integrity checks, metadata-only operations alerts, independent external readiness alerts, and controlled reboot pre/postflight proof.

## Private-beta smoke hardening

PR #86 added secure second-factor support to `deploy/smoke-private-beta.sh` without weakening its existing ownership/admin/mutation safeguards.

The harness supports either:

- `BILLWATCH_SMOKE_TWO_FACTOR_CODE_FILE`, or
- `BILLWATCH_SMOKE_RECOVERY_CODE_FILE`

Secret files must be regular, non-symlink files with mode `600`; both cannot be supplied simultaneously. Authentication failures are handled without printing tokens or secret values.

The deployed Web/BFF smoke harness on current `development` likewise supports TOTP/recovery-code files, encrypted cookie-session verification, authenticated BFF probes, export secret/storage-boundary checks, antiforgery issuance, and protected logout. The cross-user Web/BFF harness supports a separate foreign identity with its own protected second factor and proves foreign bill-stream/statement-upload access returns 404.

These capabilities are code/CI verified. They are **not** proof that the deployed production environment has been exercised with controlled accounts.

## Current machine-verifiable P0 position

The exact PR #86 feature head passed the complete CI gate. No unresolved compile/test/CI/container/recovery failure is known from that change.

Most remaining private-beta P0 items are real-environment acceptance gates, not missing generic application code. Do not manufacture synthetic evidence.

In particular:

- authenticated browser/BFF/direct-API smoke must be run against the deployed release with controlled identities;
- objective cross-user Web/BFF ownership smoke requires a second controlled identity and real controlled fixture;
- disposable account-deletion evidence must be produced and matched to the deployed release;
- human Plaid Hosted Link/update-mode behavior and Active/sync verification require real observation;
- representative statement semantic/OCR accuracy requires operator-known facts;
- clean-host restore must use the actual off-host repository;
- provider-enforced immutable/Object-Lock/WORM/equivalent protection must be configured and proven;
- independent alert receipt must be observed;
- qualified legal review of the exact Terms/Privacy version remains external evidence.

## Production/rollout rules

- Global subscription enforcement remains OFF until its separate rollout gate is deliberately approved.
- Staff roles do not grant access to another user's financial evidence.
- AI-derived persistence remains disabled.
- Startup EF migrations mean production remains one API instance until migration ownership is redesigned.
- Never run `docker compose down --volumes` against production.
- Beta Terms/Privacy are operational drafts, not qualified legal review.
- Guarded-deploy only a fully green `master` release through the normal release path.

## Remaining real-environment private-beta gates

Before trusted external beta invitations:

1. Guard-deploy the final green `master` release.
2. Run authenticated direct API/Web-BFF/admin/access-key/Plaid/statement/subscription smoke with controlled identities and fixtures.
3. Run objective cross-user Web/BFF ownership smoke with a second controlled identity.
4. Run disposable account-deletion proof and feed same-release evidence into Internal Beta 0.
5. Complete human Plaid Hosted Link/update-mode observation and Active/sync verification.
6. Review representative PDF/scanned-PDF/JPG/PNG extraction/OCR fields and bill-change explanations against operator-known facts.
7. Run clean-host restore against the actual off-host repository.
8. Configure and prove provider-enforced immutable/protected backup recovery.
9. Run independent alert-observation proof and personally confirm both destinations.
10. Combine same-release technical, alert, Plaid, recovery, and acceptance evidence.
11. Complete Internal Beta 0 on real controlled bills with explicit expected subscription state where known.
12. Obtain qualified review of the exact deployed Terms/Privacy version.
13. Run the trusted-beta launch evidence verifier only after every underlying real-world fact is genuinely complete.

## Immediate resume point

1. `development` is currently `a5fcdf1b192aa2d2c3960a71797d42730b1dc32f`, including PR #86's secure private-beta 2FA smoke hardening.
2. `master` is currently `03d757a6328aa237c719012757ad95a614228d9c`; do not infer production deployment from that branch state.
3. Exact PR #86 feature-head CI #521 is the verified automated gate for the 2FA smoke change.
4. The highest-value next step remains real deployed authenticated browser/BFF/direct-API acceptance, followed by cross-user ownership and provider/statement observations.
5. If acceptance exposes a concrete defect, stop progression, create a focused branch from current `development`, fix it, and require the full three-job CI gate before merge.
6. Preserve every security invariant above and all user-owned data ownership boundaries.
