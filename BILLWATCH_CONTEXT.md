# BillWatch Current Context

Last updated: 2026-09-04

## Authority / continuation rules

This file is the durable BillWatch development handoff.

- Current source is authoritative for implementation details.
- Stop roadmap progression immediately for compile, runtime, test, CI, deployment, migration, or unexpected security failures caused by current work.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to make a check pass.
- Prefer large coherent feature slices and one complete validation gate after a meaningful milestone.
- Do not deploy a feature branch directly to production.
- Do not merge until the full CI/container/recovery gate is green.

## Product promise

**Know when your bills change — and why.**

BillWatch is transaction-first. Bank transactions discover recurring bills. Provider statements and other source evidence explain why bills changed.

AI may produce structured candidate facts from messy evidence, but deterministic code validates evidence, performs financial arithmetic, enforces ownership/security, compares history, and makes final system decisions. AI output is never evidence by itself. Insufficient evidence must produce a truthful unknown outcome rather than an invented explanation.

## Repository / stack

Repository: `RealizmModz/BillWatch`

Current overnight branch: `work/p0-beta-verification-2026-09-04`

Current PR: #44, `P0 private-beta security verification`, targeting `master`, kept as a draft.

Projects:

- `BillWatch.csproj` — .NET MAUI client
- `BillWatch.API/BillWatch.API.csproj` — ASP.NET Core Web API
- `BillWatch.Core/BillWatch.Core.csproj`
- `BillWatch.Tests/BillWatch.Tests.csproj`
- `BillWatch.Web/BillWatch.Web.csproj` — Blazor Interactive Server Web/BFF

Technology includes .NET 10, PostgreSQL/EF Core, ASP.NET Core Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, local Tesseract OCR, Docker Compose, Caddy, systemd, and encrypted Restic recovery.

Public Web: `https://billbeacon.net`

Public API: `https://api.billbeacon.net`

Production repository path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side and protected at rest.
- Web API access/refresh tokens stay inside the encrypted HttpOnly BFF authentication ticket and are not intentionally exposed to browser JavaScript.
- User-owned financial data is ownership-scoped. Cross-user resource access should normally return 404 where appropriate.
- Statement storage is ownership-scoped; physical storage paths never leave the API.
- Statement upload signature/type/size validation remains enforced.
- Financial/auth API and BFF responses are no-store.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted reverse-proxy configuration.
- Never log raw statements, full account numbers, access/refresh tokens, Plaid tokens, passwords, provider secrets, database credentials, Restic secrets, or private operations-webhook URLs.
- AI-derived persistence remains disabled. Deterministic extraction remains the production persistence path.

## PR #44 milestone state

PR #44 contains three substantial overnight commits. Temporary per-file GitHub persistence commits should continue to be reconsolidated into those milestone commits rather than growing hourly history.

### Security and Web/API boundaries

Implemented and regression-tested:

- Stripe webhook bodies capped at 256 KB, including unknown-length/chunked requests.
- Oversized webhook requests return 413 before signature processing.
- Invalid Stripe signatures fail closed without echoing payload/signature material.
- Centralized antiforgery validation for actual unsafe `/auth` and `/bff` routes before handler execution.
- Unknown/fallback Web routes are not incorrectly converted into antiforgery failures.
- Sensitive Web/API responses remain no-store and carry expected security headers.
- Anonymous authorization coverage spans destructive/sensitive financial, statement, alert, account, and subscription routes.
- End-to-end rate-limit coverage spans authentication, account export, subscription redemption, statement upload, and statement download.
- Authenticated rate limiting is proven user-partitioned where required.

### Identity, beta access, and Plaid lifecycle

Implemented and regression-tested:

- Owner/Admin authorization policies and Moderator/user denial.
- Fresh bearer role claims required after promotion.
- Admin cannot escalate to Owner or manage Owner authority.
- Beta access-key create/list-without-plaintext/redeem/exhaust/revoke/rejected-redemption lifecycle.
- Plaid sync coordinator persists `RequiresAttention` only for explicit user-action Item errors.
- Protected access token and transaction cursor are retained for update-mode repair.
- Automatic retries stop after a persisted RequiresAttention provider failure while the original exception still propagates.
- Disconnect revokes provider access, removes local protected credentials/cursor, deactivates accounts, preserves cross-user isolation, handles already-removed Items safely, and preserves local state on other provider failures.

### Recovery, retention, and operations alerts

Implemented:

- Guarded encrypted Restic backup workflow and isolated recovery verification.
- Opt-in retention with minimum enabled floors of 14 daily / 8 weekly / 12 monthly / 3 yearly completed snapshots.
- Weak/malformed retention is rejected before destructive `restic forget --prune` operations.
- Retention runs only after a completed backup passes repository verification.
- Non-destructive retention-policy verification.
- Backup failures route through a dedicated systemd operations-alert unit.
- Operations alert payloads contain fixed metadata only and never service logs, financial data, statements, request bodies, credentials, or tokens.
- Private alert webhook URL remains in protected configuration and out of curl process arguments.
- Provider-side Object Lock/WORM/append-only protection remains a separate required real-environment beta gate; repository pruning is not treated as equivalent immutability.

### Release/deployment integrity

Implemented and regression-tested:

- `.billwatch-release`, `BILLWATCH_RELEASE_ID`, Git HEAD, clean source, and first-party OCI revision labels must agree.
- Protected release marker is deployment-owned, mode 600, non-symlinked, Git-ignored, untracked, and exactly one lowercase 40-character SHA.
- Production deployment rejects stale/missing API, Web, or backup image revisions before startup.
- Existing API/Web/edge runtime must be consistently running and match the last verified release before replacement.
- Encrypted recovery point is taken before replacing a verified running release.
- Candidate startup/readiness/HTTP-security failure stops unverified API/Web/edge services while leaving PostgreSQL and the last verified marker untouched.
- Automatic code rollback is intentionally not performed after candidate startup because forward migrations may already have changed database schema.

### Runtime watchdog / reboot readiness — current slice

Implemented on the same third milestone commit and awaiting its definitive CI gate:

- `billwatch-runtime-readiness.service` runs the complete guarded production verifier as the deployment account.
- The service requires Docker/network ordering and routes failures through the existing metadata-only operations alert service.
- Missing `.env.production`, missing `.billwatch-release`, dirty source, stale runtime revisions, unhealthy containers, bad exposure, or public readiness failures are allowed to fail the verifier and alert; systemd conditions do not silently skip missing protected state.
- `billwatch-runtime-readiness.timer` verifies shortly after boot and then five minutes after each completed verification, avoiding overlapping checks.
- `deploy/check-runtime-watchdog.sh` verifies installation, enabled/active timer state, safe service wiring, and the next scheduled run.
- `deploy/verify-beta-readiness.sh` now requires the runtime watchdog.
- Operations-alert verification now requires both backup and runtime-readiness failure routing.
- Dedicated shell regression tests cover unit wiring, fail-closed missing-state behavior, scheduling semantics, disabled/inactive timer rejection, beta-readiness integration, and alert integration.
- CI now runs the runtime-watchdog regression suite.

The watchdog deliberately does **not** automatically deploy, roll back, restart PostgreSQL, or rewrite the verified release marker. An actual controlled VPS reboot remains a real-host proof gate even after automated watchdog checks pass.

## Last definitive green baseline

Head `35ce21e9725a7c8c5b8731c79ed50af6c1186ca4` passed BillWatch CI #287 completely before the runtime-watchdog slice was added.

That gate covered Release build, EF pending-model verification, full xUnit tests, production operation/release-integrity suites, production API/Web images, HTTPS readiness, HTTP security boundaries, encrypted backup, isolated database/statement/Data Protection restore, and post-recovery API readiness.

The runtime-watchdog head must receive a new complete CI gate before the current milestone is considered closed.

## Production backup / operations state

Production has an active daily encrypted backup design and CI repeatedly proves isolated restore of database, statement files, and Data Protection material.

Still-required real-host recovery/operations gates include:

- clean-host/off-host restore drill using production-like protected storage;
- provider-side immutable/Object-Lock/WORM or equivalent protection and tested recovery from it;
- observed external backup/runtime-failure alert delivery;
- independent external readiness forced-failure alert proof;
- controlled VPS reboot and automatic healthy recovery, followed by `verify-beta-readiness.sh`.

## Core pipeline

Implemented flow:

Bank connection → account sync → transaction sync → recurring bill discovery → Bill Stream → background monitoring → provider statement upload → secure storage → text/OCR extraction → structured extraction → deterministic validation → persistence → historical comparison → change detection → evidence-backed explanation → alert.

Implemented alerts include bill increase/decrease, new fee, removed discount, evidence-backed payment due, connection issue, and newly discovered recurring bill.

## Production / rollout rules

- Global subscription enforcement remains OFF until its separate rollout gate is explicitly approved.
- Staff roles do not grant access to another user's financial evidence.
- AI-derived persistence remains disabled.
- Startup EF migrations mean production should remain one API instance until migration ownership is redesigned.
- Never run `docker compose down --volumes` against production.

## Immediate resume point

1. Reconsolidate the runtime-watchdog/context persistence commits into the existing third substantial milestone commit.
2. Run one definitive full CI/container/recovery gate on that exact consolidated head.
3. Fix any failure caused by current work before progressing.
4. If green, keep PR #44 draft/unmerged and continue the remaining real-environment or automatable P0 beta-readiness gates without reopening already-green audits.

## Remaining private-beta launch gates

Before trusted external beta invitations:

- complete authenticated browser smoke testing;
- production Owner/Admin browser/API smoke after guarded deployment;
- real access-key create/redeem/revoke smoke;
- Plaid Hosted Link, RequiresAttention, update-mode reconnect, and disconnect smoke with real sandbox/production configuration;
- real PDF/JPG/PNG/OCR statement fixtures and ownership checks;
- real clean-host/off-host recovery drill;
- provider-side immutable backup protection and recovery proof;
- observed external operations-alert delivery;
- independent external readiness monitoring with forced-failure proof;
- controlled VPS reboot/recovery test;
- internal Beta 0 on real bills before inviting 3–5 trusted testers;
- Terms/Privacy review before broader public beta.

## Development workflow

- Work in the largest safe coherent batches possible.
- Prefer finishing an entire feature slice across backend/Web/tests/configuration/docs before checkpointing.
- Hourly automation runs are continuation opportunities, not commit boundaries.
- Aim for roughly 1–3 substantive commits across the overnight session.
- Full CI is a milestone gate, not an hourly ritual.
- Preserve work on the same feature branch/PR between runs.
- Stop immediately for genuine security problems, destructive migration risk, unresolved architectural uncertainty, or failures caused by current work.
