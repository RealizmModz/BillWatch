# BillWatch Current Context

Last updated: 2026-09-01

## Product promise

**Know when your bills change — and why.**

BillWatch is a transaction-first bill-intelligence product. Bank transactions discover recurring bills. Provider statements and other source evidence explain why a bill changed.

BillWatch uses a hybrid AI + deterministic architecture:

- AI understands messy, variable evidence and returns structured candidate facts.
- Deterministic code validates facts, performs financial arithmetic, enforces security/ownership, compares history, applies confidence/alert thresholds, and makes final system decisions.
- AI output is never treated as evidence by itself.
- If evidence is insufficient, BillWatch must say that it does not know why rather than invent an explanation.

## Current architecture

- .NET 10
- .NET MAUI client
- ASP.NET Core Web API
- BillWatch.Web Blazor Web App / BFF
- PostgreSQL + Entity Framework Core
- ASP.NET Core Identity bearer authentication behind the API
- encrypted HttpOnly web authentication ticket for the BFF session
- Plaid bank connectivity
- xUnit integration/unit tests
- PdfPig PDF extraction
- Local Tesseract OCR
- Docker / Docker Compose
- Caddy HTTPS edge
- Restic encrypted recovery

## Current product surface

Primary authenticated navigation:

- Overview
- Bills
- Activity
- Account

Account contains connected-bank management, transactions, privacy/session controls, data export, and permanent account deletion.

Public website:

- `https://billbeacon.net`

Public API:

- `https://api.billbeacon.net`

## Statement intelligence architecture

Target pipeline:

Transaction → recurring Bill Stream discovery → statement/document acquisition → secure storage → document classification → text/OCR extraction → structured extraction → deterministic validation → Bill Stream matching → historical comparison → deterministic change calculation → evidence-backed explanation → confidence validation → alert/action recommendation.

The statement-processing pipeline depends on the vendor-neutral `IBillStatementExtractionService` boundary rather than directly depending on a specific parser strategy.

The current registered implementation is `DeterministicBillStatementExtractionService`, which composes the existing deterministic statement and line-item parsers. This preserves current behavior while allowing a future server-side AI-assisted extractor or provider adapter to implement the same boundary.

Important rules for future AI implementations:

- AI vendor SDKs/configuration stay inside the API implementation layer.
- No AI keys, prompts, or provider secrets go to the MAUI client or browser JavaScript.
- Controllers and UI must not contain prompts/model configuration.
- Bill Stream/provider context supplied to extraction is a hint, not evidence.
- Extracted facts must still pass deterministic validation before persistence.
- Financial arithmetic remains deterministic.
- Evidence references should be returned where practical; missing evidence must not be fabricated.
- Prefer one normalized extraction call per document and reuse persisted structured results rather than repeatedly sending the same document to a model.
- Provider-specific parsers/adapters are optimizations or reliability fallbacks, not the fundamental architecture.

## Implemented core pipeline

Bank connection → account/transaction sync → recurring bill discovery → Bill Stream → background monitoring → provider statement upload → secure storage → text/OCR extraction → structured extraction boundary → deterministic validation → persistence → historical comparison → change detection → explanation → alert.

Implemented alerts include:

- Bill increase
- Bill decrease
- New fee
- Removed discount
- Payment due when explicitly present on provider evidence
- Connection issue
- Newly discovered recurring bill

## Security rules already represented in code

- Plaid access tokens stay server-side and are protected at rest.
- MAUI protected services obtain access tokens through `AuthenticationService.GetValidAccessTokenAsync()`.
- The web BFF keeps API access/refresh tokens inside the protected HttpOnly authentication ticket; API tokens are not exposed to JavaScript.
- User-owned database queries are ownership-scoped.
- Important entity relationships use `(Id, UserId)` composite ownership foreign keys where implemented.
- Cross-user resource manipulation should return 404 rather than disclose existence.
- Statement storage uses ownership-scoped secure paths and never returns physical storage paths to clients.
- Statement uploads validate type/signature and enforce size limits.
- Financial API responses are configured as non-cacheable.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, explicit AllowedHosts, and trusted proxy configuration.
- Sensitive document/AI processing remains server-side.

## Background work

- Statement processing uses a hosted background worker.
- Bank monitoring uses a hosted scheduler.
- Active connections are refreshed on a conservative cadence.
- Integration tests disable scheduled Plaid monitoring.

## Production deployment

The production VPS repository is:

`/opt/billwatch`

Production services:

- PostgreSQL
- BillWatch.API
- BillWatch.Web
- Caddy edge

The guarded deployment validates configuration, creates a verified encrypted recovery point before replacing an existing release, starts the stack, waits for health/readiness, and records the release only after public verification succeeds.

The API and Web applications both have explicit production readiness endpoints.

Current known deployed application release from the initial public Web activation:

`019a5fbe92d545b45d2cdcc26e9a6b5a06f7264b`

The repository has advanced beyond that release with documentation and CI fixes. Do not assume Git `master` is deployed until the guarded deployment is run and the release marker is verified.

## BillWatch.Web production architecture

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

## Production smoke-test status

Confirmed in the public browser:

- `https://billbeacon.net` loads over HTTPS.
- Landing page renders correctly.
- Sign-in works.
- Authenticated application shell renders.
- Browser establishes the Blazor Interactive Server WebSocket through Caddy.
- Overview exits loading and shows the correct synchronized-empty state.
- Bills loads and shows the correct empty state.
- Navigation from Overview → Bills → Overview remains healthy.
- Browser console showed no application errors during this verification.

### Resolved Overview loading incident

Immediately after the first production Web activation, one authenticated Overview load remained on skeleton placeholders.

Diagnostics showed:

- Web-to-API readiness calls were healthy and fast.
- A later browser refresh established the `/_blazor` WebSocket successfully.
- Overview completed the BFF-backed load and rendered normally.
- Bills and return navigation also worked normally.

No security control was weakened and no source change was required to make the authenticated data path work. GitHub Issue #1 preserves the incident history.

If this symptom recurs, inspect the browser interactive connection and BFF requests before changing production configuration.

## CI status

GitHub Actions contains separate Windows backend build/test and Linux production-container jobs.

After BillWatch.Web was added to production Compose, CI initially failed because the Linux job did not build the Web image or provide the Web hostname expected by the four-service stack.

That CI workflow was corrected on `master`.

The follow-up `BillWatch CI` run for commit `1905517bbc85c898dde8457ccedfed31ccc4cbc7` completed successfully.

## AI trust boundary

The hybrid statement-intelligence foundation has explicit trust layers:

1. `IBillStatementExtractionService` isolates the persistence pipeline from extraction strategy.
2. `IBillStatementAiExtractor` defines vendor-neutral AI candidate output with source evidence.
3. `BillStatementAiCandidateValidator` and `BillStatementAiCandidateConversionService` reject unsupported model facts before accepted candidates can map into BillWatch structured statement data.

The active runtime extractor remains deterministic.

OpenAI-backed extraction infrastructure exists for controlled evaluation, but AI-derived facts are not routed into production persistence.

Shadow evaluation, durable attempt accounting, corpus safety boundaries, deterministic ground-truth scoring, and fail-closed readiness gates exist to evaluate quality before any future activation.

Do not route AI output into persistence until the ground-truth corpus, accuracy, false-alert, operational, and explicit authorization gates are satisfied.

## Recovery / operations state

Production recovery uses encrypted Restic snapshots that contain the database dump, statement files, and Data Protection keys required for a coherent recovery point.

Deployment performs a verified encrypted recovery point before replacing a running release.

Remaining operator-level work includes:

- daily scheduled encrypted backups
- clean-host off-host restore drill
- retention/immutability controls
- forced external readiness alert drill
- controlled VPS reboot/recovery test

## Remaining private-beta launch gates

1. Complete the public authenticated smoke test across Activity, Account, transactions, export, logout, and relevant mutations.
2. Enable the daily encrypted backup timer and perform a clean-host recovery drill with real protected production-format data.
3. Configure `BILLWATCH_PRODUCTION_URL=https://api.billbeacon.net` for external readiness monitoring and verify a forced failure raises an alert.
4. Validate Plaid sandbox Hosted Link, sync, failure, reconnect, and disconnect behavior through the public Web surface before any production-Plaid decision.
5. Produce MAUI release artifacts separately with `BillWatchApiBaseUrl=https://api.billbeacon.net/` when native-client beta work is scheduled.
6. Build a ground-truth provider statement corpus and measure extraction accuracy and false alerts before enabling AI shadow provider calls or persistence.
7. Run internal Beta 0 on real bills, then invite 3–5 trusted testers after P0 gates close.

## Current active checkpoint

The original production Overview-loading incident is no longer blocking progress.

Continue the **production authenticated smoke test** before adding another major feature.

Next surfaces to verify:

1. Activity
2. Account
3. Transactions
4. account export
5. logout/session behavior

If any runtime error, exception, failed request, unexpected loading state, or security regression appears, stop smoke-test progression and diagnose it before continuing.

## Development workflow

Use coherent batches of complete files for source changes. Stop roadmap progression immediately for compiler/runtime/test failures. Do not guess the contents of large current files; the newest source on `master` is authoritative for implementation details. Aim for 0 errors and 0 warnings when reasonably achievable.
