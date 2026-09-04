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

PR #44 is the shared overnight P0 verification PR. Connector persistence may temporarily create per-file commits, but coherent work should be reconsolidated before its definitive milestone CI gate rather than treating hourly runs as commit boundaries.

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

### Statement ingestion and client semantics

Implemented and regression-tested:

- PDF/JPG/JPEG/PNG upload boundaries rely on detected signatures rather than claimed MIME type.
- Extension/signature mismatches, unsupported extensions, empty files, missing multipart files, and untrusted filenames fail safely without exposing storage paths.
- Native Tesseract and scanned-PDF OCR coverage already exist in the test suite.
- Web statement upload polling treats `Processed`, `Failed`, `NeedsOcr`, and `ReadyForParsing` as terminal states so unsupported/manual-review outcomes do not poll forever.

### Recovery, retention, alerts, release integrity, and runtime watchdog

Implemented and regression-tested:

- Guarded encrypted Restic backup workflow and isolated recovery verification.
- Opt-in retention with minimum enabled floors of 14 daily / 8 weekly / 12 monthly / 3 yearly completed snapshots.
- Weak/malformed retention is rejected before destructive `restic forget --prune` operations.
- Backup/runtime failures route through a metadata-only systemd operations-alert unit whose private HTTPS webhook remains out of process arguments.
- Provider-side Object Lock/WORM/append-only protection remains a separate required real-environment gate.
- `.billwatch-release`, `BILLWATCH_RELEASE_ID`, Git HEAD, clean source, and first-party OCI revision labels must agree.
- Candidate startup/readiness/HTTP-security failure stops unverified API/Web/edge services while leaving PostgreSQL and the last verified marker untouched.
- Automatic code rollback is intentionally not performed after candidate startup because forward migrations may already have changed database schema.
- Runtime watchdog executes the guarded production verifier after boot and repeatedly thereafter, fails on missing trusted state, and alerts without deploying, rolling back, restarting PostgreSQL, or rewriting the release marker.
- Independent off-host readiness monitoring also exists; real observed forced-failure delivery remains an external proof gate.

### Terms, Privacy, and registration acceptance — current slice

Implemented in the current working tree and awaiting the next definitive full CI gate:

- Shared legal contract version: `2026-09-04-beta`.
- Public Web `/terms` and `/privacy` beta legal drafts describe the current read-only financial-data, statement/OCR, security, retention, and AI/deterministic architecture.
- Web account creation requires an explicit Terms/Privacy checkbox and submits the exact current legal version.
- MAUI account creation requires the same explicit checkbox and exposes Terms/Privacy links through the public Web origin.
- Direct API registration is fail-closed: missing, false, or stale acceptance is rejected before Identity creates a user.
- Registration request bodies are bounded to 16 KB at the API boundary.
- Test registration helpers and the production-container recovery fixture submit the current acceptance contract.
- Integration tests cover missing/false/stale/current acceptance, oversized registration requests, public legal pages, and the required versioned Web consent surface.

Important limitation: this slice enforces explicit current-version acceptance at account creation but does **not** create an audit-grade historical consent ledger for existing/future users. Do not claim legal review or compliance completion from this implementation. Qualified counsel review and any future existing-user re-consent/consent-ledger requirements remain separate launch work.

## Last definitive green baseline

Head `e0629e7c41455f7f3db0f4f1d1a001ebbf55f600` passed BillWatch CI #313 completely before the current legal-acceptance slice.

That gate covered Release build, EF pending-model verification, full xUnit tests, production operation/beta/watchdog suites, production API/Web images, HTTPS readiness, HTTP security boundaries, release-label verification, encrypted backup creation, isolated database/statement/Data Protection restore, and post-recovery API readiness.

The legal-acceptance working tree must receive a new complete CI/container/recovery gate before this milestone is considered closed.

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
- Beta Terms/Privacy documents are operational drafts, not a substitute for qualified legal review before broader public/commercial launch.

## Immediate resume point

1. Finish consistency review of the versioned Terms/Privacy registration-acceptance slice.
2. Reconsolidate its temporary persistence commits onto the last green baseline rather than leaving per-file history.
3. Run one definitive full CI/container/recovery gate on the exact consolidated head.
4. Fix any failure caused by current work before progressing.
5. If green, keep PR #44 draft/unmerged and continue only genuinely remaining P0 gates; do not duplicate already-green off-host-monitor, statement/OCR, security, or recovery audits.

## Remaining private-beta launch gates

Before trusted external beta invitations:

- complete authenticated browser smoke testing;
- production Owner/Admin browser/API smoke after guarded deployment;
- real access-key create/redeem/revoke smoke;
- Plaid Hosted Link, RequiresAttention, update-mode reconnect, and disconnect smoke with real sandbox/production configuration;
- real representative PDF/JPG/PNG statement fixtures against production-like configuration and ownership checks;
- real clean-host/off-host recovery drill;
- provider-side immutable backup protection and recovery proof;
- observed external operations-alert delivery;
- independent external readiness monitoring forced-failure proof;
- controlled VPS reboot/recovery test;
- internal Beta 0 on real bills before inviting 3–5 trusted testers;
- qualified Terms/Privacy review before broader public/commercial beta.

## Development workflow

- Work in the largest safe coherent batches possible.
- Prefer finishing an entire feature slice across backend/Web/tests/configuration/docs before checkpointing.
- Hourly automation runs are continuation opportunities, not commit boundaries.
- Aim for roughly 1–3 substantive commits across the overnight session.
- Full CI is a milestone gate, not an hourly ritual.
- Preserve work on the same feature branch/PR between runs.
- Stop immediately for genuine security problems, destructive migration risk, unresolved architectural uncertainty, or failures caused by current work.
