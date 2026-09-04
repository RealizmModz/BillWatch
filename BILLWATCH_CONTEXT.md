# BillWatch Current Context

Last updated: 2026-09-04

## Authority / continuation rules

This file is the durable BillWatch development handoff.

- Current source is authoritative for implementation details.
- Stop roadmap progression immediately for compile, runtime, test, CI, deployment, migration, or unexpected security failures caused by current work.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, or financial-data boundaries to make a check pass.
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

Current PR: #44, `P0 private-beta security verification`, targeting `master`.

Projects:

- `BillWatch.csproj` — .NET MAUI client
- `BillWatch.API/BillWatch.API.csproj` — ASP.NET Core Web API
- `BillWatch.Core/BillWatch.Core.csproj`
- `BillWatch.Tests/BillWatch.Tests.csproj`
- `BillWatch.Web/BillWatch.Web.csproj` — Blazor Interactive Server Web/BFF

Technology includes .NET 10, PostgreSQL/EF Core, ASP.NET Core Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, local Tesseract OCR, Docker Compose, Caddy, and encrypted Restic recovery.

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

## Current P0 security milestone

PR #44 includes a substantial security verification milestone.

Implemented:

- Stripe webhook bodies are capped at 256 KB, including unknown-length/chunked request bodies.
- Oversized webhook requests return 413 before signature processing.
- Invalid Stripe signatures remain fail-closed and do not echo request payload/signature evidence.
- A centralized Web antiforgery boundary validates actual unsafe `/auth` and `/bff` endpoint route templates before handler execution.
- Unknown/fallback Web routes are not incorrectly converted into antiforgery failures.
- Web integration-test infrastructure exercises the real ASP.NET Core pipeline with authenticated test principals.
- Missing-antiforgery coverage spans authentication, subscription, alerts, Plaid, bank disconnect, statement upload, account deletion/preferences, account security/2FA, and admin mutations.
- Anonymous API authorization regression coverage was expanded across destructive/sensitive financial, statement, account, alert, and subscription routes.
- API no-store behavior is regression-tested for sensitive responses.
- End-to-end rate-limit tests cover authentication, account export, subscription redemption, statement upload, and statement download.
- Account-export limiting is proven to partition authenticated users separately rather than throttling another user sharing the same client IP.

### Last fully green milestone

Security head `454bace79554d0c851cb0c46d128d7782867ecd1` passed BillWatch CI run #211 completely:

- backend Release build passed;
- EF pending-model-change verification passed;
- all 412 tests passed;
- production API image passed;
- production Web image passed;
- public HTTPS readiness passed;
- encrypted backup creation passed;
- isolated database/statement/Data Protection restore passed;
- post-restore API readiness passed.

This is the last known fully green baseline while the resilience slice below is still being finalized/validated.

## Current P0 operations-resilience slice

A second substantial PR slice is implemented on the same branch. Its goal is to close repository-level backup-retention and backup-failure-alerting gaps without pretending repository controls provide storage-provider immutability.

Implemented:

- Backup retention configuration is explicit and opt-in.
- Safe enabled floors are 14 daily, 8 weekly, 12 monthly, and 3 yearly completed snapshots.
- The backup container rejects malformed or weaker enabled retention values before running any destructive Restic retention operation.
- Enabled retention applies only after a new backup passes repository integrity checking and is promoted to the `billwatch-complete` tag.
- A non-destructive `backup policy` command and `deploy/check-backup-policy.sh` verify configured retention.
- Production configuration preflight validates retention booleans/counts/floors before Docker receives configuration.
- `billwatch-backup.service` routes failures through `billwatch-operations-alert@.service`.
- `deploy/send-operations-alert.sh` sends only fixed operational metadata: source, event, unit, host, and UTC timestamp.
- The private HTTPS webhook URL is read from protected `.env.production`, rejected if unsafe, stored in a mode-600 temporary curl config, and kept out of curl process arguments.
- Alert payloads never attach service logs, financial data, statements, request bodies, credentials, or tokens.
- `deploy/check-operations-alerting.sh` verifies alerting configuration/systemd wiring without sending an external event.
- `deploy/verify-beta-readiness.sh` now requires configured backup retention and local backup-failure alert wiring.
- Production/beta shell regression tests cover safe retention floors, reject weaker retention, require HTTPS alerting, verify systemd wiring, and verify secret-safe curl configuration.
- Production documentation and beta checklist now distinguish repository retention from provider-side immutable/Object-Lock/WORM protection.

Important safety boundary:

Repository pruning is **not** equivalent to immutable recovery. Provider-side Object Lock/WORM/append-only protection remains a separate beta gate. If provider immutability rejects Restic pruning, keep BillWatch automatic pruning disabled and use a tested provider-side lifecycle/retention policy; never weaken immutability merely to make pruning succeed.

External alert delivery is also not proven merely by configuration. Before beta invitations, send a manual `readiness-test` event and observe it at the configured external destination.

The operations-resilience slice has not yet received its definitive full CI gate as of this context update. Do not merge it based only on the prior green security run.

## Production backup state

Production has an active daily encrypted backup timer and a previously verified completed `billwatch-complete` Restic snapshot. Existing CI has repeatedly proven isolated restore of database, statement files, and Data Protection material.

Still-required real-host recovery/operations gates include:

- clean-host/off-host restore drill using production-like protected storage;
- provider-side immutable/Object-Lock/WORM or equivalent protection and tested recovery from it;
- observed external backup-failure alert delivery;
- external readiness forced-failure alert proof;
- controlled VPS reboot and automatic healthy recovery.

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

1. Finish the current operations-resilience slice review.
2. Consolidate temporary GitHub persistence commits into meaningful milestone commits rather than hourly/file-by-file history.
3. Run one definitive full CI/container/recovery gate for the completed security + resilience PR state.
4. Fix any failure caused by current work before progressing.
5. Merge only when the complete gate is green and the PR remains reviewable.
6. Guarded-deploy the resulting `master`; never deploy this feature branch directly.
7. Continue real production/browser/Plaid/statement/recovery beta gates.

## Remaining private-beta launch gates

Before trusted external beta invitations:

- complete authenticated browser smoke testing;
- prove production Owner/Admin role authorization after the role-aware release;
- prove access-key create/redeem/revoke flow;
- test Plaid RequiresAttention/update-mode reconnect/disconnect behavior;
- verify statement PDF/image/OCR/ownership paths with real test documents;
- perform a real clean-host/off-host recovery drill;
- prove provider-side immutable backup protection and recovery;
- prove backup-failure and external-readiness alert delivery;
- perform a controlled VPS reboot/recovery test;
- finish ownership/antiforgery/rate-limit/proxy/cookie/secret-safe-logging review;
- run internal Beta 0 on real bills before inviting 3–5 trusted testers.

## Development workflow

- Work in the largest safe coherent batches possible.
- Prefer finishing an entire feature slice across backend/Web/tests/configuration/docs before checkpointing.
- Hourly automation runs are continuation opportunities, not commit boundaries.
- Aim for roughly 1–3 substantive commits across the overnight session.
- Full CI is a milestone gate, not an hourly ritual.
- Preserve work on the same feature branch/PR between runs.
- Stop immediately for genuine security problems, destructive migration risk, unresolved architectural uncertainty, or failures caused by current work.
