# BillWatch Current Context

Last updated: 2026-09-12

## Authority / continuation rules

This is the durable BillWatch development handoff. Current source, exact-head CI, and verified production state win over this file for implementation/runtime truth.

- Stop immediately for compile/runtime/test/CI/deployment failures caused by current work, destructive migration risk, genuine security problems, or unresolved architecture uncertainty.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to pass a check.
- Work in coherent slices. Do not merge a feature branch until its exact final head has passed the complete CI/container/recovery gate.
- Never deploy a feature branch directly to production. Use the guarded release path from a verified `master` commit.
- Prefer useful code or real acceptance work over repeated audits and synthetic acceptance artifacts.
- Do not start lower-priority P1 expansion while genuine P0 private-beta acceptance gates remain open unless acceptance exposes a concrete P0 defect that needs code changes.

## Product promise

**Know when your bills change — and why.**

BillWatch is transaction-first. Bank transactions discover recurring bills. Provider statements/evidence explain why bills changed. AI may produce structured candidate facts, but deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final persistence/alert decisions. AI output is never evidence by itself.

## Repository / stack

Repository: `RealizmModz/BillWatch`

Default/release branch: `master`
Active integration branch: `development`

Current shared release/integration baseline:

- `master`: `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`
- `development`: `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`
- Release PR: #82, `Refresh BillWatch hardening baseline documentation`
- PR #82 merged 2026-09-12 and produced release commit `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`.
- Post-merge BillWatch CI #515 passed all three jobs on that exact release: `Backend build and tests`, `MAUI Android build`, and `Linux production container`.
- Final MAUI Release build reported **0 warnings / 0 errors**.

`development` was restored to the exact released commit after promotion so future work starts from the deployed baseline rather than a stale pre-release head.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://billbeacon.net`
Public API: `https://api.billbeacon.net`
Production path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side/protected at rest.
- Web bearer/refresh tokens remain inside encrypted HttpOnly BFF state and are not intentionally exposed to browser JavaScript.
- External provider ID tokens/proofs must not be exposed to browser JavaScript. Temporary provider proof is held server-side in the short-lived encrypted HttpOnly external-auth session.
- User financial resources and statements remain ownership-scoped; cross-user IDs normally return 404 where appropriate.
- Staff roles do not grant access to another user's financial evidence.
- Statement storage paths never leave the API; signature/type/size validation remains enforced.
- Financial/auth API and BFF responses remain no-store.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted reverse-proxy configuration.
- Never log raw statements, full account numbers, auth/Plaid/provider tokens, passwords, recovery codes, provider/database/Restic secrets, or private operations webhooks.
- AI-derived persistence remains disabled; deterministic extraction remains production persistence.

## Current verified production release

Production is now guarded-deployed at exact release:

`71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`

The guarded deployment completed successfully and verified:

- production configuration preflight;
- release integrity;
- permissions and exposure boundaries;
- API/Web/database/edge runtime health;
- API and Web readiness;
- production HTTP security boundaries;
- antiforgery issuance and protected logout;
- encrypted Restic recovery point creation before replacement.

Production API: `https://api.billbeacon.net`
Production Web: `https://billbeacon.net`

The prior email-verification resend crash caused by indexing missing `changedEmail` state is included in this deployed release. Do not claim browser-level confirmation until that production flow is retested through the UI.

## Production host maintenance completed 2026-09-12

Production host package maintenance was completed without changing the BillWatch release.

- Normal Ubuntu package updates applied successfully.
- Login now reports **0 immediately applicable normal updates**.
- Ubuntu Pro/ESM Apps still advertises optional extended updates for packages covered by that service; ESM Apps was not enabled merely to suppress the banner.
- Ubuntu `docker-buildx` 0.30.1 was installed, eliminating the previous Docker Compose Bake warning caused by missing Buildx.
- Docker Compose remained available at 2.40.3.
- Docker remained enabled and active.
- Production verification passed after package maintenance.
- Production release SHA remained exactly `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`.

### Controlled reboot proof: PASSED

A real controlled host reboot was completed using the repository's guarded preflight/postflight workflow.

Preflight passed with the full private-beta host prerequisite gate. The host was then manually rebooted. Postflight proved:

- a distinct boot actually occurred;
- the release marker and Git HEAD were unchanged;
- Docker returned automatically;
- the complete private-beta host prerequisite gate passed after boot;
- production release remained `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`.

The accidental second preflight state created after the successful postflight was intentionally cleared without another reboot.

Therefore the controlled reboot/recovery acceptance gate is complete for the current release.

## Verified P0/private-beta code position

The repository includes regression-tested P0 work covering identity/BFF isolation, ownership, Plaid state handling, statement validation and processing, subscription rollout gating, backup/recovery, operations alerts, release integrity, controlled reboot evidence, and private-beta evidence correlation.

Important verified identity/security slices include:

- centralized Web antiforgery, security headers, no-store boundaries, sensitive endpoint authorization, and user-partitioned authenticated rate limits;
- Owner/Admin and access-key privilege boundaries with role-claim freshness;
- versioned private-beta Terms/Privacy acceptance across Web, MAUI, and direct API registration;
- account deletion reauthentication/2FA/staff-role protections, Plaid revoke-first behavior, crash-safe statement quarantine/reconciliation, owned-data erasure, and deployed-release disposable-account deletion proof support;
- server-side BFF access-token refresh with rotated-token persistence and fail-closed sign-out on refresh failure;
- objective cross-user Web/BFF ownership smoke using a second controlled identity and real foreign-owned resources;
- external sign-in support for Google/Apple/Microsoft with BillWatch 2FA completion after provider proof;
- one-time recovery-code support for external sign-in, external identity unlink, and external identity link flows;
- external identity unlink requires current BillWatch password and, when 2FA is enabled, exactly one authenticator code or unused recovery code;
- external identity link preserves temporary provider proof only in the encrypted HttpOnly external session, requires BillWatch reauthentication, and routes authenticator and recovery-code factors distinctly;
- recovery codes retain one-time redemption semantics and ambiguous dual-factor submissions fail closed.

Important verified Plaid/statement/beta slices include:

- Plaid `RequiresAttention` classification/persistence, repair-state retention, retry stopping, safe disconnect, and ownership isolation;
- PDF/JPG/JPEG/PNG statement signature validation, upload/status/download ownership, terminal-state semantics, native/scanned OCR coverage, and storage-path secrecy;
- guarded direct API, authenticated Web/BFF, Owner/Admin, access-key, Plaid, statement lifecycle, statement semantic-review, subscription lifecycle, and account-deletion smoke/proof harnesses;
- subscription rollout preflight while global subscription enforcement remains OFF;
- Internal Beta 0 runner requiring release-matched acceptance evidence;
- two-phase release-pinned Plaid Hosted Link observation proof requiring human completion plus objective Active/sync verification.

Important verified backup/operations slices include:

- encrypted Restic backup/restore verification and guarded clean-host recovery drill support with isolated PostgreSQL and no production volumes;
- append-only routine backup boundary with separate delete-capable retention-maintenance authority;
- runtime watchdog, release-integrity checks, metadata-only operations alerts, independent external readiness alerts, and controlled reboot pre/postflight proof;
- release-pinned technical, alert-observation, Plaid-observation, and private-beta acceptance evidence verifiers;
- trusted-beta launch evidence gate requiring complete machine acceptance plus explicit provider-immutability/protected-recovery and qualified Terms/Privacy review attestations.

## Current machine-verifiable P0 position

No compile, test, CI, container, guarded-deploy, production-verification, or controlled-reboot failure remains open for release `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`.

Most remaining private-beta P0 items are **real-environment acceptance gates**, not missing generic application code. Do not manufacture extra scripts merely to make human/provider facts look machine-verifiable.

In particular:

- provider-enforced immutable/Object-Lock/WORM/equivalent backup behavior cannot be truthfully claimed until the actual off-host backup provider and its retention/delete/version semantics are known and configured;
- qualified legal review cannot be replaced by application code;
- human Plaid Hosted Link/update-mode behavior and alert receipt require real observation;
- representative statement semantic accuracy requires comparison against operator-known facts;
- authenticated browser/BFF/direct-API smoke must be run against the deployed release with controlled identities rather than inferred from CI.

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

- run authenticated direct API/Web-BFF/admin/access-key/Plaid/statement/subscription smoke with controlled identities and fixtures;
- run the objective cross-user Web/BFF ownership smoke with a second controlled identity that owns a real controlled statement fixture;
- run the disposable account-deletion proof and feed its same-release evidence into Internal Beta 0;
- complete the human Plaid Hosted Link/update-mode flow and finalize the release-pinned Plaid observation proof after Active/sync verification;
- review representative PDF/scanned-PDF/JPG/PNG extraction/OCR fields and bill-change explanations against operator-known facts;
- run clean-host restore against the actual off-host repository and record same-release recovery evidence;
- configure provider-enforced immutable/Object-Lock/WORM/equivalent protection, prove recovery from that protected path, and only then record the explicit same-release backup approval attestation;
- run the alert-observation proof and personally confirm the challenge in both independent destinations;
- combine same-release technical, alert, Plaid, recovery, and acceptance evidence with the private-beta acceptance verifier;
- complete Internal Beta 0 on real controlled bills with explicit expected subscription state where known;
- obtain qualified review of the exact deployed Terms/Privacy version and record the legal approval attestation;
- run the trusted-beta launch evidence verifier only after every underlying real-environment fact above is genuinely complete.

The controlled reboot gate is no longer outstanding for release `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`.

## Immediate resume point

1. Treat exact release `71f71a67c5683a9a18ca0ac21ab8f342c38ac08c`, post-merge CI #515, guarded production deployment, host maintenance, and successful controlled reboot recovery as the current verified baseline.
2. `master` and `development` currently share that exact baseline.
3. Prefer the next genuine real-environment P0 acceptance step over more generic hardening churn.
4. Highest-value next acceptance work is deployed authenticated browser/BFF/direct-API smoke, followed by controlled cross-user ownership and statement/Plaid observations where practical.
5. If acceptance exposes a concrete defect, stop progression, create a focused branch from current `development`, fix it, and require the full three-job CI gate before merge.
6. Preserve every security invariant above and all user-owned data ownership boundaries.
