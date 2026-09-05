# BillWatch Current Context

Last updated: 2026-09-04

## Authority / continuation rules

This is the durable BillWatch development handoff. Current source wins over this file for implementation details.

- Stop immediately for compile/runtime/test/CI/deployment failures caused by current work, destructive migration risk, genuine security problems, or unresolved architecture uncertainty.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to pass a check.
- Work in large coherent slices; hourly continuation is not a commit boundary.
- Keep the current work on the same draft PR/feature branch. Do not deploy the feature branch directly to production or merge before the full CI/container/recovery gate is green.

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

The PR now includes and regression-tests:

- Stripe webhook 256 KB body cap, chunked/unknown-length protection, fail-closed invalid signatures, and centralized Web antiforgery/security-header/no-store boundaries.
- Anonymous/sensitive endpoint authorization and rate-limit coverage, including user-partitioned authenticated limits.
- Owner/Admin policy behavior, role-claim freshness, access-key create/list/redeem/exhaust/revoke lifecycle, and privilege boundaries.
- Plaid `RequiresAttention` classification/persistence, repair-state retention, retry stopping, safe provider disconnect, and ownership isolation.
- Statement PDF/JPG/JPEG/PNG signature validation, upload/status/download ownership, terminal-state semantics, native/scanned OCR regression coverage, and storage-path secrecy.
- Versioned private-beta Terms/Privacy contract `2026-09-04-beta`, Web/MAUI/direct-API acceptance enforcement, and 16 KB registration cap. Qualified counsel review and any future audit-grade consent ledger remain separate work.
- Account deletion reauthentication/2FA/staff protections, Plaid revoke-first behavior, crash-safe statement quarantine/reconciliation, and owned-data erasure coverage.
- Repeatable guarded direct API, authenticated Web/BFF, access-key, Plaid, and statement lifecycle production smoke harnesses.
- Guarded clean-host/off-host recovery drill with isolated PostgreSQL and no production volumes.
- Independent metadata-only external readiness alert path and forced-failure regression proof.
- Guarded controlled reboot preflight/postflight proof that never initiates reboot itself.
- Guarded Internal Beta 0 runner composing direct API, Web/BFF, access-key, Plaid, and statement smoke gates with release-pinned metadata-only evidence.

## Last definitive green baseline

Commit `5e8e30d5b77f0423ae1cafd1bae025374270b6e4` (`Add guarded Internal Beta 0 acceptance runner`) passed BillWatch CI #393 completely on 2026-09-04.

That gate includes Release build, EF pending-model verification, full xUnit suite, production/beta operation regression suites, production API/Web images, HTTPS readiness, HTTP security boundaries, release-label verification, encrypted backup creation, isolated PostgreSQL/statement/Data Protection restore, and post-recovery API readiness.

## Current slice: backup trust separation

In progress on top of the CI #393 green baseline:

- normal production backup capture is explicitly `BILLWATCH_BACKUP_CLIENT_MODE=append-only` at the BillWatch command/config boundary;
- normal backup capture never automatically invokes `restic forget`/`prune`, even when a retention policy is enabled;
- destructive retention requires `BILLWATCH_BACKUP_CLIENT_MODE=maintenance` plus explicit `BILLWATCH_BACKUP_MAINTENANCE_ALLOW=true`;
- production environment validation refuses maintenance mode;
- a separate trusted-host `deploy/run-backup-maintenance.sh` uses a mode-600 environment outside the checkout, exact clean release, off-host repository, hardened no-volume container, and only the retention command;
- dedicated regression coverage proves append-only refusal, maintenance opt-in, exact retention arguments, no production-volume mounts, and protected maintenance-env behavior;
- operator documentation explicitly separates routine backup credentials from delete-capable maintenance credentials.

This slice intentionally does **not** claim provider-side immutability. Provider ACL/Object Lock/WORM/append-only enforcement and recovery from that protected path remain a real-environment launch gate. Restic's append-only guidance requires maintenance authority to be separated from the potentially compromised backup client; this slice aligns BillWatch with that threat model rather than pretending an environment flag proves provider protection.

## Production/rollout rules

- Global subscription enforcement remains OFF until its separate rollout gate is deliberately approved.
- Staff roles do not grant access to another user's financial evidence.
- AI-derived persistence remains disabled.
- Startup EF migrations mean production remains one API instance until migration ownership is redesigned.
- Never run `docker compose down --volumes` against production.
- Beta Terms/Privacy are operational drafts, not qualified legal review.

## Remaining real-environment private-beta gates

Before trusted external beta invitations:

- guarded-deploy the final green release and run authenticated direct API/Web-BFF/access-key/Plaid/statement smoke with controlled accounts/fixtures;
- complete the human Plaid Hosted Link/update-mode flow with controlled provider data and verify sync/RequiresAttention behavior;
- review representative PDF/JPG/PNG extraction/OCR fields and resulting bill-change explanation for semantic accuracy;
- run clean-host restore against the real off-host repository;
- configure provider-enforced immutable/Object-Lock/WORM/append-only backup protection and prove recovery from protected storage;
- observe production operations-alert delivery and independent external readiness forced-failure delivery;
- perform controlled reboot preflight/manual reboot/postflight;
- complete Internal Beta 0 on real controlled bills before inviting 3–5 trusted testers;
- obtain qualified Terms/Privacy review before broader public/commercial beta.

## Immediate resume point

1. Finish the backup trust-separation slice without changing provider behavior or claiming immutability.
2. Run its dedicated regression suite through CI; fix any current-work failure before proceeding.
3. If the complete CI/container/recovery gate is green, keep PR #44 draft/unmerged and treat that exact head as the new baseline.
4. Do not implement provider-specific Object Lock/WORM assumptions until the actual backup provider and its retention/delete/version semantics are known; that is a real architecture/security decision, not a generic shell toggle.
5. Prefer remaining application-level private-beta readiness work only where it adds real value; avoid duplicating already-green smoke/recovery/security audits.
