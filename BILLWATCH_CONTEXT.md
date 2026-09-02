# BillWatch Current Context

Last updated: 2026-09-01

The newest source files in this repository are the authority for implementation details. If this file and source code disagree, trust the newest source code.

## Product promise

**Know when your bills change — and why.**

BillWatch is a transaction-first bill-intelligence product.

- Bank transactions discover recurring bills.
- Provider statements and other source evidence explain why a bill changed.
- AI may interpret messy evidence and return structured candidate facts.
- Deterministic code validates facts, performs financial arithmetic, enforces ownership/security, compares history, applies confidence/alert thresholds, and makes final system decisions.
- AI output is never evidence by itself.
- If evidence is insufficient, BillWatch must say that it does not know why rather than inventing an explanation.

Important recurring increases should be expressed as both monthly and annualized impact.

## Current architecture

- .NET 10
- .NET MAUI client
- ASP.NET Core Web API
- Blazor Web App (`BillWatch.Web`)
- PostgreSQL + Entity Framework Core
- ASP.NET Core Identity bearer authentication
- Plaid bank connectivity
- xUnit tests
- PdfPig PDF extraction
- local Tesseract OCR
- Docker / Docker Compose
- Caddy TLS/reverse proxy
- Restic encrypted production recovery

Repository:

`C:\Users\brist\source\repos\BillWatch\`

Solution:

`C:\Users\brist\source\repos\BillWatch\BillWatch.slnx`

Projects:

- `BillWatch.csproj`
- `BillWatch.API\BillWatch.API.csproj`
- `BillWatch.Core\BillWatch.Core.csproj`
- `BillWatch.Tests\BillWatch.Tests.csproj`
- `BillWatch.Web\BillWatch.Web.csproj`

## Current product surface

Primary authenticated web navigation:

- Overview
- Bills
- Activity
- Account

Account contains connected-bank management, transactions, privacy/session controls, data export, and permanent account deletion.

The public web surface includes landing, registration, and login.

## Security rules

BillWatch handles sensitive financial data. Security is a first-class design requirement.

- Plaid access tokens stay server-side and are protected at rest.
- Web API bearer/refresh tokens are stored in the encrypted HttpOnly web authentication ticket and are not exposed to JavaScript.
- Accurate web security claim: **No API tokens are exposed to JavaScript.**
- MAUI protected services obtain access tokens through `AuthenticationService.GetValidAccessTokenAsync()`.
- User-owned database queries are ownership-scoped.
- Important relationships use `(Id, UserId)` composite ownership foreign keys where implemented.
- Cross-user resource manipulation should normally return 404 rather than disclose existence.
- Statement storage uses ownership-scoped secure paths and never returns physical storage paths to clients.
- Statement uploads validate type/signature and enforce size limits.
- Financial/authentication API responses are non-cacheable.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, explicit AllowedHosts, and explicit trusted proxy configuration.
- Never log raw statements, full account numbers, credentials, access tokens, refresh tokens, or Plaid secrets.
- Never route AI-derived facts directly into persisted production truth without the explicit launch gates described below.

## Core bill pipeline

Bank connection
→ account/transaction sync
→ recurring Bill Stream discovery
→ background monitoring
→ provider statement upload
→ secure storage
→ text/OCR extraction
→ structured extraction
→ deterministic validation
→ Bill Stream matching
→ persistence
→ historical comparison
→ deterministic change detection
→ evidence-backed explanation
→ alert

Implemented alert types include:

- Bill increase
- Bill decrease
- New fee
- Removed discount
- Payment due when explicitly supported by provider evidence
- Connection issue
- Newly discovered recurring bill

## Statement intelligence trust boundary

The statement-processing pipeline depends on vendor-neutral `IBillStatementExtractionService`.

The active runtime implementation remains `DeterministicBillStatementExtractionService`.

AI infrastructure exists behind `IBillStatementAiExtractor`, but AI output is not registered as the runtime persistence extraction strategy.

Existing AI safety/evaluation architecture includes:

- candidate evidence validation
- candidate conversion into the existing structured statement model only after validation
- deterministic-first shadow evaluation
- durable ownership-scoped AI attempt accounting
- one-attempt-per-user/upload/provider/model/prompt-version cost claims
- aggregate-only readiness scoring
- fail-closed shadow configuration
- private corpus path policy
- bounded private corpus loader
- aggregate corpus catalog inspection
- provider coverage gate
- deterministic corpus baseline evaluator

The OpenAI adapter remains server-side, uses structured output, bounded document text, explicit prompt/schema versioning, sanitized failure behavior, and `store: false`.

The current runtime must continue to persist deterministic statement results only.

## AI readiness gate

The existing conservative private-beta shadow baseline requires approximately:

- at least 100 statements
- at least 100 provider attempts
- at least 5 providers
- at least 10 statements per provider
- at least 99% fact precision
- at least 95% fact recall
- at least 85% ready-candidate coverage
- no more than 1% false alerts across a separately evaluated alert population of at least 100 statements
- no more than 5% provider failures

Missing alert evaluation cannot count as zero false alerts.

Passing these metrics is not authorization to persist AI-derived facts.

## Background work

- Statement processing uses a hosted background worker.
- Bank monitoring uses a hosted scheduler.
- Active bank connections are refreshed on a conservative cadence.
- The monitoring flow is accounts → transactions → recurring-bill discovery → connection-health reconciliation.
- Integration tests disable scheduled Plaid monitoring.

## Account and privacy features

Implemented API/web capabilities include:

- registration/login
- session resume/refresh
- logout
- account deletion
- ownership-scoped JSON account export
- safe statement-file download
- ownership regression coverage
- anonymous-access regression coverage
- health endpoints
- production exception handling/security headers
- per-user/IP rate limiting for statement uploads, exports, and statement downloads

The account export deliberately excludes protected provider credentials, synchronization cursors, password/security data, Plaid internal secrets, and physical statement-storage keys.

Permanent account deletion removes ownership-scoped AI evaluation metadata as well as ordinary user data and stored statement files.

## Plaid behavior

Plaid remains server-side.

Reconnect is a real Plaid update-mode flow for owned connections that require attention. Cross-user IDs return not found before provider calls. Disconnected/revoked connections cannot enter update mode.

Plaid remains in sandbox until deliberate real-institution/private-beta verification.

## BillWatch.Web architecture

`BillWatch.Web` is a server-rendered Blazor Web App using Interactive Server components and a same-origin BFF.

The API remains the business/auth/ownership authority.

The web BFF proxies authenticated browser requests to the API while keeping API bearer/refresh tokens out of browser JavaScript.

Main routes include:

- `/`
- `/login`
- `/register`
- `/app`
- `/app/bills`
- `/app/bills/{id}`
- `/app/activity`
- `/app/account`
- `/app/account/transactions`
- `/app/account/privacy`

Web functionality implemented includes:

- light/dark theme
- responsive app shell
- Overview
- Bills
- Bill detail
- real statement upload/status polling
- real alert center with read/dismiss
- bank connect/reconnect/disconnect
- transaction viewer
- account export
- permanent deletion
- production health endpoints
- production security headers
- persistent web Data Protection keys
- explicit reverse-proxy handling

## Production deployment

Public web:

`https://billbeacon.net`

Public API:

`https://api.billbeacon.net`

VPS deployment repository:

`/opt/billwatch`

Current production topology:

PostgreSQL
→ BillWatch.API
→ BillWatch.Web
→ Caddy / HTTPS

Production services:

- `database`
- `api`
- `web`
- `edge`

The web deployment was activated on 2026-09-01 after adding the Blazor web container, dual-host Caddy routing, web Data Protection persistence, production web health endpoints, BFF endpoint mappings, and auth endpoint mappings.

Historical deployment milestones:

- `2771ac588665b5272cee48aa7be1e002a9e9fcc7` — first VPS deployment against sslip.io
- `60f0f72583760c8f60a725b485c17d4062c46651` — `api.billbeacon.net` activation
- `019a5fbe92d545b45d2cdcc26e9a6b5a06f7264b` — first verified API + Web production release

Later Git commits may exist beyond the currently deployed release; always compare `.billwatch-release` on the VPS with `git rev-parse HEAD` before claiming a new commit is deployed.

Cloudflare should remain DNS-only until trusted Cloudflare client-IP forwarding is intentionally configured and regression-tested.

## Production operations

Production environment file:

`/opt/billwatch/.env.production`

Never paste that file or its secrets into chat or Git.

Validate production configuration:

```bash
sh deploy/validate-production-env.sh .env.production
```

Deploy guarded production release:

```bash
sh deploy/deploy-production.sh .env.production
```

Check services:

```bash
docker compose --env-file .env.production --file compose.production.yml ps
```

The guarded deployment:

- requires a clean checkout
- requires `BILLWATCH_RELEASE_ID` to match the checked-out commit
- prevents concurrent deploys
- validates Compose
- builds immutable images
- creates an encrypted recovery point before replacing an existing release
- waits for service health
- externally verifies API and web readiness
- records the release only after readiness succeeds
- does not attempt unsafe automatic database rollback after migrations

## Production backup/recovery

Production recovery uses an encrypted Restic operations container.

The backup contains the PostgreSQL dump, Data Protection key material, and statement files needed for coherent recovery.

CI proves a synthetic encrypted round trip, but a real off-host clean-host recovery drill is still required because CI cannot prove operator credential escrow, immutable provider retention, or restoration of real protected production-format data.

## Current active checkpoint — production Overview loading

**Do not continue product feature development until this is diagnosed.**

Confirmed production behavior:

- `https://billbeacon.net` resolves and serves valid HTTPS.
- Landing page renders correctly.
- Production sign-in succeeds.
- Authenticated app shell and signed-in identity render.
- The `/app` Overview remains on its loading skeleton state instead of completing its initial data load.

Current source behavior:

`BillWatch.Web/Components/Pages/App/Overview.razor` starts with `_isLoading = true`. On the first interactive render it imports `./js/bff.js`, then calls `getBankConnections` and `getBillStreams`. `_isLoading` is cleared only after that async load completes or throws.

Therefore the first investigation must establish whether:

1. the Blazor Interactive Server circuit is actually connecting through Caddy, and
2. the authenticated BFF calls complete.

Immediate VPS diagnostic:

```bash
docker compose --env-file .env.production --file compose.production.yml logs --tail 80 web
```

If inconclusive:

```bash
docker compose --env-file .env.production --file compose.production.yml logs --tail 80 api
```

Browser network/console inspection should specifically check:

- `/_blazor`
- `/bff/bank-connections`
- `/bff/bill-streams`

GitHub issue #1 tracks this production defect.

Do not weaken cookie security, antiforgery, ownership scoping, API-token isolation, HTTPS, AllowedHosts, or trusted-proxy validation to make the page load.

## Remaining private-beta launch gates

1. Fix and smoke-test the authenticated production web application.
2. Enable the daily encrypted backup timer and perform a clean-host recovery drill with real protected production-format data.
3. Configure `BILLWATCH_PRODUCTION_URL=https://api.billbeacon.net` and prove a forced readiness failure raises an external alert.
4. Validate Plaid sandbox flows thoroughly, then deliberately validate real institutions and reconnect/failure behavior before private beta.
5. Build the private ground-truth provider statement corpus and measure extraction accuracy and false alerts.
6. Produce MAUI release artifacts with `BillWatchApiBaseUrl=https://api.billbeacon.net/` when native-client beta is actually needed.
7. Run internal Beta 0 on real bills.
8. Invite 3–5 trusted testers only after the P0 launch gates pass.

See `BILLWATCH_TODO.md` for the prioritized operational and product backlog.

## Development workflow

Work incrementally from the current repository state.

- Use coherent batches of complete files.
- Stop roadmap progression immediately for compiler/runtime/test failures.
- Do not guess the contents of large current files; inspect the newest source first.
- Target 0 errors and 0 warnings when reasonably achievable.
- If the API is running locally, stop it before rebuilding to avoid locked binaries.
- Do not claim a security feature exists unless the code actually implements it.
- Only recommend a Git checkpoint after a coherent milestone and clean build/test.

When resuming this project, start with the active production Overview defect, not a new feature.
