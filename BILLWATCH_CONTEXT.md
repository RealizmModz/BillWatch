# BillWatch Current Context

Last updated: 2026-09-03 local time / 2026-09-04 UTC

## Authority / continuation rules

This file is the durable BillWatch handoff.

- Treat current source as authoritative for implementation details.
- If this document and source disagree, current source wins.
- Stop roadmap progression for compile errors, runtime errors, test failures, CI failures, deployment failures, or unexpected behavior.
- Do not weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, proxy trust, token protection, migration safety, or financial-data protections to make a test pass.
- Do not merge a PR until the full CI gate is green.
- Do not deploy a feature branch directly to production.

The user prefers to be called “Master” naturally when appropriate.

A single `.`, `..`, `.]`, `,`, or whitespace around a dot means continue to the next logical BillWatch checkpoint. Do not recap the whole project when continuing.

## Product promise

**Know when your bills change — and why.**

BillWatch is transaction-first:

- bank transactions discover recurring bills;
- provider statements explain why a bill changed;
- AI may turn messy evidence into candidate facts;
- deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final system decisions;
- AI output is never evidence by itself;
- insufficient evidence must produce a truthful “we don't know yet” outcome rather than an invented explanation.

BillWatch must answer three questions exceptionally well:

1. What changed?
2. Why did it change?
3. How much will it cost me?

Important recurring increases should be presented as monthly + annualized impact, for example:

`$79.99 → $104.99`

`+$25/month`

`+$300/year`

Reason:

`Promotion expired +$20`

`New fee +$5`

BillWatch is not primarily a budgeting app, bill-payment app, transfer app, investment tracker, credit-score app, generic financial dashboard, or one-time AI bill scanner.

Do not let feature expansion dilute the core promise unless a feature directly strengthens bill-change detection, explanation, trust, or actionability.

## Business / launch goal

The target progression is:

**trustworthy private beta → controlled public beta → sustainable paid product**

Current stage: **P0 private-beta readiness / production verification**.

Do not confuse “public beta” with unlimited scale. Public beta may remain enrollment-controlled while infrastructure is still intentionally small.

Before expensive infrastructure or broad feature expansion, BillWatch should begin validating whether users find the product valuable enough to pay for.

Revenue validation is a product requirement, not something to postpone until the product is “finished.”

## Cost discipline

BillWatch should remain intentionally cheap until product-market evidence justifies more spending.

Current planning target:

- aim to keep additional development/infrastructure spending through public beta at roughly **$500 or less** unless the user explicitly approves going above it;
- prefer free/open-source/self-hosted components where they are secure and operationally reasonable;
- avoid paid managed databases, observability suites, SMS, large AI workloads, native-store fees, or oversized infrastructure before they solve a demonstrated need;
- use one coherent CI/build gate per meaningful batch rather than waste compute/model usage on redundant cycles;
- infrastructure spending should increasingly correlate with real users, reliability needs, or revenue validation.

This is a planning guardrail, not a promise that every future provider cost will remain under the target.

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

PostgreSQL currently runs on the production VPS. Do not migrate to a managed database merely to chase a free tier. Migrate only when reliability, security, operational simplicity, scaling, or economics make it worthwhile.

## Security rules

BillWatch handles sensitive financial information.

Never:

- expose Plaid access tokens to MAUI or browser JavaScript;
- expose API access/refresh tokens to browser JavaScript;
- ask users to paste Plaid credentials, database passwords, production secrets, or access tokens into chat;
- log raw statements, full account numbers, credentials, access tokens, refresh tokens, Plaid tokens, provider secrets, or raw AI prompts/responses containing financial evidence;
- return physical statement-storage paths to clients;
- trust user-controlled paths;
- claim a security feature exists unless it is implemented and verified.

Every user-owned database entity must be ownership-scoped.

Prefer `UserId + resource ID` checks. Important relationships should enforce ownership again at the database layer with composite relationships such as `(Id, UserId)` where appropriate.

Cross-user ID manipulation should normally return 404 rather than reveal that another user's object exists.

Current architecture rules:

- Plaid access tokens remain server-side and protected at rest.
- MAUI protected services obtain API access tokens through `AuthenticationService.GetValidAccessTokenAsync()`.
- The Web BFF keeps API access/refresh tokens inside its encrypted HttpOnly authentication ticket.
- Browser financial calls use same-origin BFF routes.
- BFF mutations use antiforgery protection.
- Statement storage is ownership-scoped and physical paths are not returned to clients.
- Statement upload type/signature/size validation exists.
- Financial/auth responses are no-store where appropriate.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted-proxy configuration.

## AI architecture and privacy rules

AI is an **isolated evidence-processing component**, never an unrestricted financial-data agent and never the system of record.

Target production flow:

**raw statement / evidence → local extraction/OCR → local minimization/redaction → narrow AI request → structured candidate facts → deterministic validation → persistence / explanation / alert**

### AI privacy gateway

All production AI requests should eventually pass through one server-side BillWatch AI privacy gateway/service boundary.

That boundary should enforce:

- approved providers/models only;
- task-specific schemas;
- minimum necessary evidence;
- local redaction/minimization before transmission;
- maximum payload/text size;
- cost/rate controls;
- timeouts/retry policy;
- safe structured error handling;
- no raw prompt/response logging;
- auditable metadata about which task/model was used without storing unnecessary financial content.

### Data minimization

The model should receive only evidence necessary for the specific task.

When possible, remove or avoid sending:

- names;
- street addresses;
- full account numbers;
- barcodes/QR data;
- statement IDs unrelated to the task;
- unrelated line items;
- unrestricted transaction history;
- internal database identifiers that have no semantic value to the model.

### AI must never receive

- Plaid access tokens;
- API access or refresh tokens;
- passwords or credentials;
- database connection strings;
- raw provider secrets;
- unrestricted database access;
- unrestricted Plaid access;
- physical statement-storage paths;
- full account numbers unless an explicitly approved future use case genuinely requires them, which should normally be avoided.

BillWatch constructs narrow AI requests. The model must not independently browse PostgreSQL, Plaid, statement storage, or user accounts.

### AI authority boundary

AI may identify candidate facts from messy evidence, such as:

- promotion expiration;
- fee introduction/increase;
- discount removal;
- plan/rate changes;
- likely usage-driven charges;
- ambiguous explanatory text.

Deterministic code remains responsible for:

- arithmetic;
- monthly/annualized impact;
- ownership/security decisions;
- historical comparisons;
- confidence thresholds;
- final persistence authorization;
- alert decisions where deterministic verification is possible.

A confident “we don't know yet” is better than an invented explanation.

### AI rollout gate

AI-derived production persistence remains disabled until all of the following are satisfied:

1. private ground-truth corpus exists;
2. quality is measured across representative providers/document types;
3. false-positive/false-alert behavior is understood;
4. cost controls are proven;
5. privacy/minimization controls are implemented;
6. provider data-handling configuration is reviewed for production use;
7. explicit authorization is given to change the persistence boundary.

Use shadow evaluation first. Passing evaluation metrics must not automatically authorize AI persistence.

For production AI providers, do not opt into provider training/data-sharing using BillWatch financial evidence. Prefer the strongest practical retention controls available for the selected provider, including Zero Data Retention or equivalent where available and appropriate. Provider policy must be re-verified at implementation time rather than assumed from this document.

## Core document / intelligence pipeline

Implemented/target flow:

Upload
↓
Secure storage
↓
Document classification
↓
Text/OCR extraction
↓
Structured bill extraction
↓
Validation
↓
Bill Stream matching
↓
Historical comparison
↓
Change detection
↓
Explanation
↓
Alert

Transaction path:

Bank connection → account sync → transaction sync → recurring bill discovery → Bill Stream → monitoring → evidence acquisition/statement → document pipeline.

Implemented alert categories include:

- bill increase;
- bill decrease;
- new fee;
- removed discount;
- evidence-backed payment due;
- connection issue;
- newly discovered recurring bill.

Deterministic extraction remains the production persistence path today.

## Current product state

The core production architecture and primary Web product surface are substantially built.

Completed/merged work includes:

- secure Identity authentication and Web BFF architecture;
- Plaid production connection and transaction synchronization;
- recurring Bill Stream discovery from real production history;
- Overview;
- Bills;
- bill detail;
- Activity/alerts;
- Transactions;
- Account;
- account export/delete/disconnect foundations;
- statement upload UI and processing pipeline foundation;
- Subscription/access-key foundation;
- Owner/Admin/Moderator administration foundation;
- secure email delivery;
- password recovery;
- 2FA-aware sign-in;
- account security center;
- editable profile/display name;
- privacy surface;
- premium light/dark fintech UI;
- English/Spanish localization across major Web surfaces;
- localized Plaid Hosted Link connect/reconnect browser status flow;
- production operations/verifier tooling;
- encrypted Restic backup automation.

Global subscription enforcement remains **OFF** until its rollout gate is explicitly approved.

## Proven production state

Previously verified production evidence includes:

- Plaid production environment intentionally configured;
- successful production bank connection;
- 2 persisted bank accounts;
- 518 active transactions persisted from the active connection at the time of verification;
- recurring discovery successfully created a Spotify Bill Stream from 3 monthly transactions;
- public Web and API HTTPS/readiness have passed guarded deployments;
- premium Web surface has been visually verified in production;
- daily encrypted backup timer is enabled/active;
- a completed Restic snapshot tagged `billwatch-complete` was verified.

Treat counts/snapshot IDs above as historical proof, not guaranteed current totals.

## Backup / recovery philosophy

A backup on the same VPS is not sufficient protection against VPS loss, disk failure, destructive deployment, corruption, or host compromise.

BillWatch backup/recovery should protect together:

- PostgreSQL data;
- uploaded statement files;
- ASP.NET Data Protection keys;
- other critical production state needed for a coherent restore.

Backups should be encrypted and operationally off-host.

Keep backup storage inexpensive; the goal is recoverability, not expensive enterprise infrastructure at current scale.

Do not declare backups trustworthy until a real clean-host/off-host restore drill proves the database, statements, and Data Protection keys restore coherently.

Remaining recovery work includes retention/immutability policy and tested backup-failure alerting.

## Localization

The Web app uses server-selected culture with an English fallback and Spanish UI support.

Major Web surfaces are localized. The Plaid Hosted Link browser flow was localized in PR #38 and merged after BillWatch CI passed.

Do not translate provider/institution names or financial evidence itself unless translation is explicitly part of a future evidence-explanation feature. UI chrome/status/error language may be localized while preserving source evidence truthfully.

## Current roadmap position

BillWatch is currently approximately in:

**Foundation → Web App → Security → Production Setup → Localization → [CURRENT: FINAL P0 VERIFICATION] → Bill Intelligence Quality → AI Evaluation → Private Beta → Controlled Public Beta**

The remaining work before trusted external testers is primarily verification and hardening, not building another large screen set.

## P0 — remaining private-beta launch gates

Do not start a major new product feature while a P0 security/production defect is open.

Remaining P0 work includes:

### Production browser/auth smoke

- registration through the public website;
- access-token refresh without browser token exposure;
- bill detail runtime smoke;
- Activity loads real alerts;
- mark-alert-read;
- dismiss-alert;
- Account and Transactions current-release smoke;
- account export ownership/safe JSON verification;
- bank disconnect through Web;
- cross-user ID manipulation returns 404;
- invalid/expired authentication does not leak provider/API details.

### Plaid verification

- Hosted Link final beta smoke;
- popup behavior in major browsers;
- `RequiresAttention` state and truthful guidance;
- update-mode reconnect;
- disconnect/revocation behavior;
- runtime confirmation that Plaid/access tokens never appear in browser JS, HTML, logs, or response bodies.

### Statement intelligence verification

- real PDF upload;
- real JPG/PNG upload;
- unsupported extension/signature rejection;
- >15 MB rejection;
- upload rate limiting;
- Uploaded → Processing → terminal status behavior;
- OCR path;
- ReadyForParsing path;
- Processed state changes only the owning Bill Stream;
- truthful/recoverable Failed state;
- physical storage paths never leave API;
- cross-user upload/status/file IDs remain inaccessible.

### Production operations

- prove PostgreSQL has no public host binding;
- prove API/Web internal port is not publicly exposed;
- prove only Caddy exposes intended public ports;
- verify API/Web/Caddy/PostgreSQL restart behavior;
- controlled VPS reboot and automatic recovery;
- configure/prove external readiness monitoring;
- forced readiness-failure notification drill.

### Backup / recovery

- independently prove Restic backend is genuinely off-host;
- clean-host/off-host restore drill;
- restore DB + statements + Data Protection keys together;
- document operator disaster recovery after the real drill;
- retention/immutability controls;
- backup-failure alerting test.

### Security review

- audit user-owned controller endpoints for ownership scoping;
- review composite `(Id, UserId)` relationships;
- review antiforgery on every BFF mutation;
- review API rate limiting/partitioning;
- review cookie flags/Data Protection persistence;
- review trusted reverse-proxy configuration;
- search source/logging for secrets, raw statements, full account numbers, tokens, or unsafe AI logging;
- run dependency/container vulnerability scans.

### Internal Beta 0

After technical P0 gates close, run BillWatch internally on real bills long enough to measure:

- false positives;
- missed recurring bills;
- time-to-first-discovery;
- explanation usefulness;
- operational failures;
- statement-processing reliability.

Only then invite roughly 3–5 trusted testers.

## P1 — bill intelligence quality

After P0 is secure/stable:

- improve recurring merchant normalization;
- improve amount tolerance and cadence classification;
- prevent duplicate recurring Bill Streams;
- handle variable monthly, annual, quarterly, and irregular-but-predictable bills;
- improve provider/statement matching confidence;
- require sufficient evidence before automatic statement attachment;
- improve deterministic promotion-expiration detection;
- improve fee-added/fee-increase detection;
- improve discount-removal detection;
- improve usage-driven vs one-time-charge differentiation;
- preserve monthly + annualized impact everywhere important.

## P1 — AI shadow evaluation

- keep AI-derived persistence disabled;
- build a private ground-truth corpus;
- target at least 5 providers and at least 100 statements before relying on readiness metrics;
- include clean PDFs, OCR-heavy scans, utilities, telecom, insurance, subscriptions, promotions, fees, and ambiguous line items;
- measure field precision, recall, candidate coverage, provider failures, false explanations, and false alerts;
- measure approximate AI cost per processed statement/user;
- do not enable uncontrolled runtime AI calls before privacy and cost gates are ready.

## P1 — observability

Add only safe telemetry:

- correlation IDs;
- bank-monitoring metrics;
- recurring-discovery metrics;
- statement-processing metrics;
- alert-generation metrics;
- uptime monitoring;
- disk/storage/database capacity alerts.

Never put financial secrets, raw statement text, raw AI evidence prompts, or full account data into telemetry.

## P2 — notifications and beta

- begin with a low-cost notification channel, likely email;
- notify for meaningful bill increases, fees, due dates, and connection attention;
- add preferences/unsubscribe controls;
- invite 3–5 trusted testers after P0/Internal Beta 0 gates close;
- collect structured feedback and reliability metrics;
- begin willingness-to-pay validation before building expensive infrastructure.

## P2 — business / legal / controlled public beta

Before controlled public beta:

- decide pricing from user/value evidence rather than guesswork;
- avoid payment infrastructure until users actually need it;
- add Terms of Service and Privacy Policy;
- clearly state BillWatch does not move money and is not a bank;
- ensure AI/data-processing disclosures accurately describe real behavior;
- define support/privacy/deletion expectations;
- retain enrollment controls if needed to keep cost/reliability manageable.

## P2 — MAUI release

- keep the MAUI project healthy while Web beta is validated;
- build release artifacts with `-p:BillWatchApiBaseUrl=https://api.billbeacon.net/` when native beta is scheduled;
- preserve `AuthenticationService.GetValidAccessTokenAsync()` for protected services;
- do not duplicate API truth in the client;
- native app-store releases are not required to prove the Web public beta.

## Immediate resume point

1. Finish/merge the current small localization/error-safety cleanup only after CI is green.
2. Continue remaining P0 browser-side error/localization safety where raw JS/server messages can leak through localized UI.
3. Move into final production smoke/Plaid/statement/recovery verification rather than another major feature.
4. Update `BILLWATCH_TODO.md` as P0 gates are actually proven so roadmap status remains truthful.
5. Do not enable AI persistence or broad AI processing yet; build the privacy gateway/evaluation path deliberately during P1.

## Development workflow

- Work incrementally like a senior developer beside the user.
- Prefer coherent multi-file batches over repeated one-file/build cycles when safe.
- 1–4 tightly related files per normal batch; two complete files is often ideal.
- For modifications, provide full paths and complete replacement files when working manually with the user.
- When GitHub is explicitly requested, GitHub may be used directly for the coherent change/PR workflow.
- Stop and diagnose immediately for compiler/runtime/test/CI/deployment failures or unexpected behavior.
- Aim for 0 errors and 0 warnings where reasonably achievable.
- Visual Studio analyzer Messages are not warnings/errors.
- If local BillWatch.API is running, stop it before rebuilding to avoid locked assemblies.
- Do not guess large current source files; inspect current source first.
- Use CI as the primary gate after a coherent batch when this avoids redundant local build cycles.

## Git checkpoint format

When recommending a coherent manual GitHub Desktop checkpoint, provide:

Summary:
`...`

Description:
`...`
