# BillWatch Current Context

Last updated: 2026-09-01 local time (some GitHub/production timestamps are 2026-09-02 UTC)

## Authority / continuation rules

This file is the durable handoff for a new BillWatch project chat.

- Treat the newest source file on `master` as authoritative for implementation details.
- If this document and current source disagree, current source wins.
- Read this file before making architectural changes.
- Stop roadmap progression immediately for compile errors, runtime errors, test failures, deployment failures, or unexpected behavior.
- Do not weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, proxy trust, token protection, or other security controls to make a test pass.

## Product promise

**Know when your bills change — and why.**

BillWatch is a transaction-first bill-intelligence product.

- Bank transactions discover recurring bills.
- Provider statements and other source evidence explain why a bill changed.
- AI may understand messy evidence and produce candidate facts.
- Deterministic code validates facts, performs arithmetic, enforces ownership/security, compares history, applies confidence thresholds, and makes final decisions.
- AI output is never evidence by itself.
- If evidence is insufficient, BillWatch must say that it does not know why rather than invent an explanation.

Important increases should be communicated as monthly change + annualized impact, for example:

`$79.99 → $104.99`

`+$25/month`

`+$300/year`

BillWatch is not primarily a budgeting app, bill-payment app, transfer app, investment tracker, credit-score app, or one-time AI bill scanner.

## Local repository / stack

Repository:

`C:\Users\brist\source\repos\BillWatch\`

Solution:

`C:\Users\brist\source\repos\BillWatch\BillWatch.slnx`

Projects:

- `BillWatch.csproj` — .NET MAUI client
- `BillWatch.API\BillWatch.API.csproj` — ASP.NET Core Web API
- `BillWatch.Core\BillWatch.Core.csproj`
- `BillWatch.Tests\BillWatch.Tests.csproj`
- `BillWatch.Web\BillWatch.Web.csproj` — Blazor Web App / BFF

Technology:

- .NET 10
- .NET MAUI
- ASP.NET Core Web API
- Blazor Interactive Server
- PostgreSQL + Entity Framework Core
- ASP.NET Core Identity bearer authentication behind the API
- encrypted HttpOnly web authentication ticket for the BFF session
- Plaid
- xUnit
- PdfPig
- local Tesseract OCR
- Docker / Docker Compose
- Caddy HTTPS edge
- Restic encrypted recovery

Local API URLs:

- `https://localhost:7243`
- `http://localhost:5189`

Run API:

`dotnet run --launch-profile https --project "C:\Users\brist\source\repos\BillWatch\BillWatch.API\BillWatch.API.csproj"`

## Current product surface

Primary authenticated navigation:

- Overview
- Bills
- Activity
- Account

Account contains connected-bank management, transactions, privacy/session controls, data export, and permanent account deletion.

Public website:

`https://billbeacon.net`

Public API:

`https://api.billbeacon.net`

## Security rules represented in code

BillWatch handles sensitive financial information. Security is a first-class requirement.

- Plaid access tokens remain server-side and are protected at rest.
- MAUI protected services obtain API access tokens through `AuthenticationService.GetValidAccessTokenAsync()`.
- The Web BFF keeps API access/refresh tokens inside the encrypted HttpOnly authentication ticket; API tokens are not exposed to JavaScript.
- User-owned database queries are ownership-scoped.
- Important relationships use `(Id, UserId)` composite ownership foreign keys where implemented.
- Cross-user resource manipulation should normally return 404 rather than disclose existence.
- Statement storage uses ownership-scoped secure paths and never returns physical storage paths to clients.
- Statement uploads validate type/signature and enforce size limits.
- Financial API responses are configured as non-cacheable.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, explicit AllowedHosts, and trusted proxy configuration.
- Sensitive document/AI processing remains server-side.
- Never log raw statements, full account numbers, credentials, access tokens, refresh tokens, Plaid tokens, or provider secrets.

## Core transaction / bill pipeline

Implemented flow:

Bank connection → account sync → transaction sync → recurring bill discovery → Bill Stream → background monitoring → provider statement upload → secure storage → text/OCR extraction → structured extraction → deterministic validation → persistence → historical comparison → change detection → explanation → alert.

Implemented alerts include:

- Bill increase
- Bill decrease
- New fee
- Removed discount
- Payment due when explicitly present in provider evidence
- Connection issue
- Newly discovered recurring bill

The monitoring refresh service performs account sync first, then Plaid transaction sync, then recurring discovery, then connection-health alert reconciliation.

The background scheduler only refreshes `Active` connections. `RequiresAttention` connections require user action; `Disconnected` connections are ignored.

## Production Plaid / transaction diagnosis — CURRENT IMPORTANT STATE

A production smoke test reported that transactions appeared not to be pulling. This has now been isolated.

Safe production diagnostics proved:

- `.env.production` reports `PLAID_ENVIRONMENT=production`.
- Bank connection states: 1 `Active`, 2 `Disconnected`.
- The active connection's latest successful sync was `2026-09-02 00:01:00.969452+00`.
- 2 bank accounts are persisted.
- 518 bank transactions are persisted.
- All 518 active transactions belong to the currently active connection.
- Therefore Plaid → BillWatch transaction sync is working.

The real defect was recurring-bill candidate filtering, not Plaid sync.

Under the previous production classifier, only **4 of 518** non-removed transactions were eligible to reach recurring-bill cadence detection.

Production category analysis showed real recurring obligations in categories such as:

- `RENT_AND_UTILITIES_RENT`
- `LOAN_PAYMENTS_BNPL`
- `LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT`
- `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT`
- `ENTERTAINMENT_TV_AND_MOVIES`
- `ENTERTAINMENT_MUSIC_AND_AUDIO`

The old classifier accepted only a very narrow subset of `RENT_AND_UTILITIES` categories, which filtered out legitimate recurring bills before cadence analysis.

### Recurring-bill classifier fix on master

GitHub Issue #2:

**Recurring bill discovery filters out real production transactions**

Fix commits already landed on `master`:

- `8f33b1d9f9071af874dbd26842bc2f7e9f806826` — Broaden recurring bill category eligibility
- `6548c1f0907a36b38edcc64b69eec56bad6901bb` — Cover broader recurring bill categories

The classifier now allows recurring candidates for:

- rent
- additional utility categories
- real loan repayments
- TV/movie subscriptions
- music/audio subscriptions
- insurance
- storage

It still rejects obvious non-bill noise such as:

- transfers
- restaurants
- home-improvement purchases
- cash advances / EWA borrowing events
- broad entertainment
- automotive service

Broadening the category classifier does **not** automatically create Bill Streams. The deterministic recurring detector remains the second gate and still requires recurrence evidence, including enough same-merchant history and monthly-like cadence.

Issue #2 must remain open until the fix is deployed to production and recurring discovery is rerun/verified against the existing 518 transactions.

## Browser-local bank sync timestamp work on master

After the classifier fix, `master` received browser-local timestamp work:

- `572985e84ebb8a42e439bde78bc63c2d03eab0fc` — Add browser-local timestamp formatter
- `9fc61be084989515041ddf06d4492125061500cf` — Display bank sync times in browser local timezone

`Account.razor` now formats connected-bank creation/sync timestamps in the browser's local timezone through `wwwroot/js/local-time.js`, with UTC fallback text so timestamp formatting cannot prevent account data from loading.

`9fc61be084989515041ddf06d4492125061500cf` was the latest functional `master` commit immediately before this context refresh. This context refresh itself is documentation-only and is newer.

## Premium Web UI revamp — ACTIVE BRANCH / PR

The user approved a full UI overhaul because the existing Web UI looked shabby and unprofessional.

Design target:

- premium consumer fintech / Apple Wallet feel
- stronger typography and hierarchy
- larger important financial values
- refined spacing and rounded surfaces
- restrained gradients and shadows
- coherent light/dark themes
- polished loading/empty/error states
- responsive mobile layout with improved bottom navigation
- accessible focus states and reduced-motion support
- preserve all real functionality; no fake buttons or fake product behavior

Work exists on branch:

`ui-premium-revamp`

Branch head:

`30e01b2c8fe3cb660d923cd9cf80e3df01c1f117`

Pull request:

**PR #3 — Premium BillWatch UI revamp**

Scope includes:

- public landing surface
- login/register styling
- authenticated app shell/navigation
- Overview
- Bills and Bill detail
- Activity and alert cards
- Account and bank connections
- Transactions
- Privacy/account management
- onboarding states
- statement upload
- shared loading/empty/error states
- theme controls
- Blazor reconnect modal

The implementation intentionally uses layered premium CSS so existing functional Razor/BFF behavior stays intact. It is presentation-only; auth, BFF routes, antiforgery, Plaid behavior, ownership checks, and backend business logic were not intentionally changed by the UI revamp.

PR #3 CI run **#43 (`33578011465`) completed successfully**:

- Backend build and tests: success
- Production API image build: success
- Production Web image build: success
- Four-service production stack start: success
- API HTTPS readiness: success
- Web HTTPS readiness: success
- Encrypted backup/recovery fixture: success
- Isolated database/file restore proof: success
- API recovery verification: success

### Important PR #3 merge state

PR #3 is **not merged yet**.

While PR #3 CI was running, `master` received the browser-local timestamp commits listed above. The UI branch and `master` are now diverged, and GitHub currently reports PR #3 as not mergeable.

Do **not** force-update `master` or overwrite the newer timestamp work.

Before merging PR #3:

1. Bring the latest `master` into `ui-premium-revamp` (merge/rebase safely).
2. Preserve the browser-local timestamp functionality and this context update.
3. Resolve any conflicts deliberately.
4. Rerun CI on the reconciled UI branch.
5. Merge PR #3 only after CI is clean.

## BillWatch.Web architecture

BillWatch.Web is a .NET 10 Blazor Web App using Interactive Server rendering.

The API remains the business-data authority.

The browser talks to same-origin BFF routes such as:

- `/bff/bank-connections`
- `/bff/bank-accounts`
- `/bff/bank-transactions`
- `/bff/bill-streams`
- `/bff/alerts`
- `/bff/account/export`
- Plaid BFF routes
- statement upload/status/download BFF routes

The BFF proxies authenticated requests to the internal API and refreshes the API bearer session server-side when needed.

Production Web protections include:

- secure HttpOnly `__Host-` authentication cookie
- persistent Data Protection keys
- antiforgery on mutations
- explicit AllowedHosts
- explicit trusted proxy configuration
- HSTS
- security headers
- no-store caching on auth/BFF/health responses
- API base URL/host-header validation
- health/live and health/ready endpoints

## Statement intelligence architecture

Target pipeline:

Transaction → recurring Bill Stream discovery → statement/document acquisition → secure storage → document classification → text/OCR extraction → structured extraction → deterministic validation → Bill Stream matching → historical comparison → deterministic change calculation → evidence-backed explanation → confidence validation → alert/action recommendation.

The statement-processing pipeline depends on the vendor-neutral `IBillStatementExtractionService` boundary rather than directly depending on a specific parser strategy.

The currently registered runtime implementation is `DeterministicBillStatementExtractionService`.

AI trust layers exist through:

1. `IBillStatementExtractionService`
2. `IBillStatementAiExtractor`
3. `BillStatementAiCandidateValidator` / conversion logic

OpenAI-backed extraction infrastructure exists for controlled evaluation only. AI-derived facts are not routed into production persistence.

Shadow evaluation, durable attempt accounting, private corpus safety boundaries, deterministic ground-truth scoring, and fail-closed readiness gates exist. Do not route AI output into persistence until explicit quality and authorization gates are satisfied.

Financial arithmetic remains deterministic.

## Production deployment

Production VPS repository:

`/opt/billwatch`

Production services:

- PostgreSQL (`database` service in current production Compose)
- BillWatch.API
- BillWatch.Web
- Caddy edge

Production domains:

- Web: `https://billbeacon.net`
- API: `https://api.billbeacon.net`

Known production deployment configuration includes:

- PostgreSQL database/user `billwatch`
- API internal DB host `database`
- explicit `BILLWATCH_HOST=api.billbeacon.net`
- explicit `BILLWATCH_WEB_HOST=billbeacon.net`
- `PLAID_ENVIRONMENT=production` confirmed safely on the VPS

The guarded deployment validates configuration, creates a verified encrypted recovery point before replacing an existing release, starts the stack, waits for health/readiness, and records the release only after public verification succeeds.

Current known deployed application release from the initial public Web activation remains:

`019a5fbe92d545b45d2cdcc26e9a6b5a06f7264b`

The repository is ahead of that deployed release.

**Do not assume the recurring-category fix, browser-local timestamp work, premium UI revamp, or this context refresh is deployed until the guarded production deployment runs and the release marker is verified.**

## Production smoke-test status

Confirmed in the public browser:

- `https://billbeacon.net` loads over HTTPS.
- Landing page renders.
- Sign-in works.
- Authenticated application shell renders.
- Browser establishes the Blazor Interactive Server WebSocket through Caddy.
- Overview exits loading and shows the synchronized-empty state.
- Bills loads and shows the empty state.
- Navigation Overview → Bills → Overview remains healthy.
- Browser console showed no application errors during that verification.

### Resolved Overview-loading incident

Immediately after the first production Web activation, one authenticated Overview load remained on skeleton placeholders.

Later diagnostics showed the Blazor `/_blazor` WebSocket connected successfully and the BFF-backed Overview/Bills surfaces rendered normally without a source/config change.

No security control was weakened. GitHub Issue #1 preserves the incident history and is no longer the active blocker.

If the symptom recurs, inspect the browser interactive connection and BFF requests before changing production configuration.

## Recovery / operations state

Production recovery uses encrypted Restic snapshots containing the database dump, statement files, and Data Protection keys required for a coherent recovery point.

CI proves the production-container backup/recovery workflow in an isolated test stack.

Remaining operator-level work includes:

- daily scheduled encrypted backups
- real clean-host off-host restore drill
- retention/immutability controls
- forced external readiness alert drill
- controlled VPS reboot/recovery test

## Current active checkpoint — NEW CHAT SHOULD RESUME HERE

Do not start a new major backend feature yet.

The next safe sequence is:

1. **Reconcile PR #3 with latest `master`.** Preserve browser-local timestamp changes and this context update; do not force-push `master` over newer work.
2. **Rerun CI** on the reconciled premium UI branch.
3. **Merge PR #3** only after CI is fully green.
4. **Run the guarded production deployment** for the resulting latest `master` and verify the release marker/readiness.
5. **Verify recurring discovery in production** using the already-synced 518 transactions. The category fix is intended to let legitimate candidates reach the deterministic cadence detector; it must not promote transfer/purchase noise solely because categories were broadened.
6. Keep GitHub Issue #2 open until Bills/Bill Streams are verified after deployment.
7. Resume the authenticated production smoke test across Activity, Account, Transactions, account export, alert mutations, logout/session behavior, reconnect/disconnect, and cross-user 404 behavior.
8. If any runtime exception, failed request, unexpected loading state, build/test failure, or security regression appears, stop progression and diagnose it first.

## Remaining private-beta launch gates

- Complete authenticated production smoke testing.
- Verify the broadened recurring-bill classifier and Bill Stream discovery against real production data.
- Enable daily encrypted backup scheduling and perform a real clean-host recovery drill.
- Configure external readiness monitoring and prove a forced failure alerts correctly.
- Validate Plaid connect/sync/failure/reconnect/disconnect behavior through the public Web surface before broad beta use.
- Build a ground-truth provider statement corpus and measure extraction accuracy/false alerts before enabling AI provider calls or persistence.
- Run internal Beta 0 on real bills, then invite a very small trusted group after P0 gates close.
- Produce MAUI release artifacts separately when native-client beta work is scheduled.

## Development workflow

- Work incrementally like a senior developer beside the user.
- Prefer coherent batches of 1–4 tightly related files.
- When replacing an existing source file in chat, provide the full path and complete replacement file, not snippets.
- Stop and diagnose immediately on compiler/runtime/test/deployment failures.
- Aim for 0 errors and 0 warnings where reasonably achievable.
- Visual Studio analyzer Messages are not warnings/errors.
- If the local API is running, stop it with Ctrl+C before rebuilding to avoid locked assemblies.
- Use GitHub Desktop for local Git checkpoints when appropriate.
- Do not guess the contents of large current source files; inspect current source first.
