# BillWatch Current Context

Last updated: 2026-09-02 local time / 2026-09-03 UTC

## Authority / continuation rules

This file is the durable BillWatch handoff.

- Treat current source as authoritative for implementation details.
- If this document and source disagree, current source wins.
- Stop roadmap progression for compile errors, runtime errors, test failures, deployment failures, or unexpected behavior.
- Do not weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, proxy trust, token protection, or financial-data protections to make a test pass.

## Product promise

**Know when your bills change — and why.**

BillWatch is transaction-first:

- bank transactions discover recurring bills;
- provider statements explain why a bill changed;
- AI may turn messy evidence into candidate facts;
- deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final system decisions;
- AI output is never evidence by itself;
- insufficient evidence must produce a truthful “we don't know yet” outcome rather than an invented explanation.

Important recurring increases should be presented as monthly + annualized impact, for example:

`$79.99 → $104.99`

`+$25/month`

`+$300/year`

BillWatch is not primarily a budgeting app, bill-payment app, transfer app, investment tracker, credit-score app, or one-time AI bill scanner.

## Repository / stack

Local repository:

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
- ASP.NET Core Identity bearer authentication
- encrypted HttpOnly Web authentication ticket / BFF
- Plaid
- xUnit
- PdfPig
- local Tesseract OCR
- Docker / Docker Compose
- Caddy HTTPS edge
- encrypted Restic recovery

Public Web:

`https://billbeacon.net`

Public API:

`https://api.billbeacon.net`

Production VPS repository:

`/opt/billwatch`

## Security rules represented in code

BillWatch handles sensitive financial information.

- Plaid access tokens remain server-side and protected at rest.
- MAUI protected services obtain API access tokens through `AuthenticationService.GetValidAccessTokenAsync()`.
- The Web BFF keeps API access/refresh tokens inside its encrypted HttpOnly authentication ticket; tokens are not intentionally exposed to browser JavaScript.
- User-owned data access is ownership-scoped.
- Important relationships use `(Id, UserId)` ownership relationships where implemented.
- Cross-user resource manipulation should normally return 404 rather than disclose another user's object.
- Statement storage is ownership-scoped and physical storage paths are not returned to clients.
- Statement upload type/signature/size validation exists.
- Financial/auth API responses are no-store.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted-proxy configuration.
- Never log raw statements, full account numbers, credentials, access tokens, refresh tokens, Plaid tokens, or provider secrets.

## Core pipeline

Implemented flow:

Bank connection → account sync → transaction sync → recurring bill discovery → Bill Stream → background monitoring → provider statement upload → secure storage → text/OCR extraction → structured extraction → deterministic validation → persistence → historical comparison → change detection → explanation → alert.

Implemented alert categories include:

- bill increase
- bill decrease
- new fee
- removed discount
- evidence-backed payment due
- connection issue
- newly discovered recurring bill

AI-derived persistence remains disabled. Deterministic extraction remains the production persistence path.

## Production state proven tonight

### Plaid and recurring discovery

Production diagnostics proved:

- `PLAID_ENVIRONMENT=production`;
- 1 active bank connection and 2 disconnected connections;
- 2 persisted bank accounts;
- 518 persisted active bank transactions from the active connection;
- therefore Plaid transaction sync is working.

The recurring classifier was broadened to allow additional real bill categories while the cadence detector remains the second deterministic gate.

Production rerun proved automatic recurring discovery is working:

- one discovered Bill Stream: Spotify;
- category: `Other`;
- 3 linked non-removed transactions;
- first seen `2026-06-25`;
- last seen `2026-08-25`;
- monthly cadence evidence is present.

Spotify is visible in the production Bills/Overview experience.

GitHub Issue #2 was closed after this production verification.

### Premium Web UI

The premium fintech UI work is merged and deployed.

Production has been visually verified on the public Web surface, including Overview, Bills, Subscription, dark theme, and correct `$` currency formatting.

The Linux/invariant-culture `¤` currency bug was fixed by explicitly setting Web process culture to `en-US` and deployed successfully.

### Subscription foundation

The subscription/admin foundation is merged and deployed.

Current rules:

- global subscription enforcement remains **OFF**;
- subscription status and access-key redemption are implemented;
- Owner > Admin > Moderator staff hierarchy exists;
- Beta Tester/Internal Tester are program memberships, not staff/subscription roles;
- access-key plaintext is one-time only and hashes are persisted;
- staff roles do not grant access to another user's financial evidence;
- subscription escape-hatch endpoints exist for account/privacy/disconnect/export/delete/admin/status/redemption flows.

The production Subscription page is deployed and polished.

## Current production release

Latest known successfully deployed production application release:

`d43bb2643ca4939f5e3a6158fc8fce1b83cd2ffc`

This is the merge of PR #12 (`Polish subscription access experience`).

The current beta-readiness branch is ahead of production and is **not deployed yet**.

## Active production blocker: role-aware bearer principals

Production database state:

- exactly 1 application user;
- seeded roles exist: Owner, Admin, Moderator;
- the sole production user has been assigned Owner out-of-band;
- database verification shows `Owner | 1`.

After a sign-out/sign-in, `/app/admin` still returned Access denied.

Root cause was found in `BillWatch.API/Program.cs`:

```csharp
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<BillWatchDbContext>();
```

Identity's role services were not registered, so the database role assignment did not flow into fresh bearer principals used by `RequireRole` authorization.

The fix on the beta-readiness branch adds:

```csharp
.AddRoles<IdentityRole<Guid>>()
```

`BillWatchDbContext` is already correctly typed as:

`IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`

and the admin/subscription migration seeds Owner/Admin/Moderator roles.

A regression integration test was added to prove:

- a user assigned Owner before login can access an `AdminOrOwner` endpoint using a freshly issued bearer token;
- a normal authenticated user remains forbidden.

A local solution build of the one-line role-registration change succeeded. Per user request, the branch's complete test suite/CI is intentionally deferred until the current batch is finished.

## Current working branch

GitHub branch:

`work/beta-readiness-2026-09-02`

Known branch head before this context refresh:

`31db9145fbe89743c32fdbd80a92d40cfde7845e`

The branch is based directly on production `master` and was 0 commits behind when created.

Current branch scope includes:

- role-aware Identity bearer principal fix;
- integration regression test for role authorization;
- guarded first-Owner bootstrap script;
- production permission/exposure/runtime verification scripts;
- Identity-role / Owner-count / subscription-safety verification;
- daily backup timer/snapshot verification;
- secure authenticated/admin API smoke scripts;
- combined private-beta host verifier;
- production operations guide;
- private-beta operator checklist.

The smoke scripts prompt for credentials interactively, do not accept passwords/tokens as command-line arguments, keep temporary auth material in private temp files, and do not print protected response bodies.

## First Owner bootstrap

`deploy/bootstrap-owner.sh` exists on the working branch for future clean deployments.

It fails closed unless:

- `.env.production` is protected and owned by the deployment account;
- exactly one BillWatch user exists;
- exactly one seeded Owner role exists;
- zero Owners already exist;
- the supplied email exactly matches the sole application user.

It becomes unavailable permanently after an Owner exists.

This replaces ad-hoc manual SQL as the documented first-Owner procedure.

## Production backup state

The daily encrypted backup timer is enabled and active.

Verified timer state showed the next daily run scheduled normally.

A manual production backup completed successfully.

A completed encrypted Restic snapshot was verified with tag:

`billwatch-complete`

Known verified snapshot ID:

`0deb75d5`

Production backup scripts restore API/Web/edge service state after the consistent backup operation.

Remaining recovery gates include a real clean-host/off-host restore drill, retention/immutability policy, and backup-failure alert delivery.

## Production operations tooling on working branch

Primary commands after the branch is eventually merged/deployed:

Production health/security verification:

```sh
sh deploy/verify-production.sh /opt/billwatch
```

Automated private-beta host prerequisites:

```sh
sh deploy/verify-beta-readiness.sh /opt/billwatch
```

Authenticated API read smoke:

```sh
sh deploy/smoke-authenticated-api.sh https://api.billbeacon.net
```

Owner/Admin authorization smoke:

```sh
sh deploy/smoke-admin-api.sh https://api.billbeacon.net
```

The automated verifier intentionally does not claim to prove browser behavior, Plaid failure/reconnect behavior, statement processing, a clean-host restore, controlled reboot recovery, or alert delivery.

## BillWatch.Web architecture

BillWatch.Web is a .NET 10 Blazor Web App using Interactive Server rendering.

The API remains the business-data authority.

Browser calls use same-origin BFF routes for financial data and mutations. The BFF proxies to the internal API and refreshes bearer sessions server-side.

Production Web protections include:

- secure HttpOnly `__Host-` authentication cookie;
- persistent Data Protection keys;
- antiforgery on mutations;
- explicit AllowedHosts;
- trusted proxy configuration;
- HSTS/security headers;
- no-store auth/BFF/health responses;
- validated internal API base URL/host header;
- live/ready health endpoints.

## Statement intelligence architecture

Target flow:

Transaction → recurring Bill Stream discovery → statement acquisition → secure storage → classification → text/OCR extraction → structured extraction → deterministic validation → Bill Stream matching → historical comparison → deterministic change calculation → evidence-backed explanation → confidence validation → alert/action recommendation.

The runtime persistence boundary is `IBillStatementExtractionService` and the production implementation remains deterministic.

OpenAI-backed extraction infrastructure exists only for controlled evaluation. Do not route AI output into persistence until the private corpus, measured quality gates, explicit authorization, and cost controls are satisfied.

Financial arithmetic remains deterministic.

## Immediate resume point

Do not start a new major backend feature yet.

Current sequence:

1. Finish auditing/cleaning the `work/beta-readiness-2026-09-02` batch.
2. Run one complete solution build + full xUnit suite after the batch is finished.
3. Open one PR for the beta-readiness batch and let the full CI/container/recovery gate run.
4. Fix any failures before merge.
5. Merge only after the full gate is green.
6. Guarded-deploy the resulting `master`.
7. Sign out/in and prove `/app/admin` now authorizes the production Owner.
8. Run the new production/beta verification scripts.
9. Create/redeem/revoke a controlled short-lived access key while subscription enforcement remains OFF.
10. Continue the remaining P0 browser/Plaid/statement/recovery smoke gates.

## Remaining private-beta launch gates

Still required before trusted external beta invitations:

- complete authenticated browser smoke testing;
- prove Owner/Admin role authorization after the pending role-aware release;
- prove access-key create/redeem/revoke flow;
- test Plaid RequiresAttention/update-mode reconnect/disconnect behavior;
- verify statement PDF/image/OCR/ownership paths with real test documents;
- perform a real clean-host/off-host recovery drill;
- establish backup retention/immutability and failure alerting;
- configure/prove external readiness alert delivery;
- perform a controlled VPS reboot/recovery test;
- complete security review of ownership, antiforgery, rate limits, proxy/cookie configuration and secret-safe logging;
- run internal Beta 0 on real bills before inviting 3–5 trusted testers.

## Development workflow

- Work incrementally like a senior developer beside the user.
- For large cleanup/beta-readiness work, coherent multi-file batches are preferred over building after every file.
- The user explicitly requested one full build/test cycle after the current batch rather than repeated intermediate test cycles.
- Stop and diagnose immediately if the user reports a compiler/runtime/deployment/test failure.
- Aim for 0 errors and 0 warnings where reasonably achievable.
- Visual Studio analyzer Messages are not warnings/errors.
- If local BillWatch.API is running, stop it before rebuilding to avoid locked assemblies.
- Do not guess large current source files; inspect current source first.
