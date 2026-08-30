# BillWatch Current Context

Last updated: 2026-08-29

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
- PostgreSQL + Entity Framework Core
- ASP.NET Core Identity bearer authentication
- Plaid bank connectivity
- xUnit integration/unit tests
- PdfPig PDF extraction
- Local Tesseract OCR

## Current product surface

Primary navigation:

- Home
- Bills
- Activity
- Account

Account contains connected-bank management, transactions, privacy/session controls, data export, and permanent account deletion.

## Statement intelligence architecture

Target pipeline:

Transaction → recurring Bill Stream discovery → statement/document acquisition → secure storage → document classification → text/OCR extraction → structured extraction → deterministic validation → Bill Stream matching → historical comparison → deterministic change calculation → evidence-backed explanation → confidence validation → alert/action recommendation.

The statement-processing pipeline now depends on the vendor-neutral `IBillStatementExtractionService` boundary rather than directly depending on a specific parser strategy.

The current registered implementation is `DeterministicBillStatementExtractionService`, which composes the existing deterministic statement and line-item parsers. This preserves current behavior while allowing a future server-side AI-assisted extractor or provider adapter to implement the same boundary.

Important rules for future AI implementations:

- AI vendor SDKs/configuration stay inside the API implementation layer.
- No AI keys, prompts, or provider secrets go to the MAUI client.
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
- User-owned database queries are ownership-scoped.
- Important entity relationships use `(Id, UserId)` composite ownership foreign keys where implemented.
- Cross-user resource manipulation should return 404 rather than disclose existence.
- Statement storage uses ownership-scoped secure paths and never returns physical storage paths to clients.
- Statement uploads validate type/signature and enforce size limits.
- Financial API responses are configured as non-cacheable.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, and explicit AllowedHosts.
- Sensitive document/AI processing remains server-side.

## Background work

- Statement processing uses a hosted background worker.
- Bank monitoring uses a hosted scheduler.
- Active connections are refreshed on a conservative cadence.
- Integration tests disable scheduled Plaid monitoring.

## Current beta-hardening work

Implemented or in progress:

- account registration / login
- session resume and refresh
- logout
- account deletion
- self-service JSON data export excluding protected provider credentials and internal statement paths
- ownership regression tests across alerts, statements, Bill Streams, bank data, and Plaid resources
- anonymous access regression tests
- health endpoints
- production exception handling/security headers
- per-user/IP statement upload rate limiting

## Current active checkpoint

The hybrid statement-intelligence foundation now has three explicit trust layers:

1. `IBillStatementExtractionService` isolates the statement-processing pipeline from extraction strategy.
2. `IBillStatementAiExtractor` defines vendor-neutral AI candidate output with source evidence.
3. `BillStatementAiCandidateValidator` plus `BillStatementAiCandidateConversionService` reject unsupported model facts before mapping accepted candidates into BillWatch's existing structured statement model.

The active runtime statement extractor is still deterministic. A first server-side OpenAI implementation now exists behind `IBillStatementAiExtractor`, but it is disabled by default and is not part of statement persistence, so current production behavior is unchanged.

The OpenAI provider configuration is validated at startup. When explicitly enabled with a server-side key, the adapter uses strict structured output, bounded document text, explicit prompt/schema versioning, timeout/cancellation, and sanitized failure behavior. Provider credentials and request configuration remain inside the API.

AI candidate evidence validation now binds each claimed string, date, and monetary value to a verified excerpt from the supplied document text. A real excerpt cannot validate a different claimed amount. Extreme decimal values are rejected without arithmetic overflow, and missing AI currency is no longer silently treated as USD.

`BillStatementAiShadowEvaluationService` now exercises deterministic-first orchestration without implementing `IBillStatementExtractionService` and without returning AI facts to persistence. It skips AI when deterministic extraction is complete, makes at most one provider attempt per evaluation, validates and converts candidates, rejects conflicts with deterministic facts, propagates caller cancellation, and exposes only shadow-review status plus the original deterministic result.

The OpenAI provider sends `store: false`, accepts only completed Responses API results, and continues to sanitize provider failures. A host-level regression test proves runtime extraction still resolves to `DeterministicBillStatementExtractionService` and that shadow evaluation is not registered.

Durable AI attempt accounting now has an ownership-scoped `BillStatementAiEvaluationEntity`, database mapping, and migration. The table stores metadata only, has a composite `(BillStatementUploadId, UserId)` ownership foreign key, and enforces one unique record per user/upload/provider/model/prompt version. It intentionally contains no document text, prompt, model output, evidence excerpt, account identifier, or provider error body.

`BillStatementAiEvaluationLedger` claims that unique cost key before a future provider call, records one attempt, makes duplicate or restarted work return the existing evaluation, scopes completion by `UserId + evaluation ID`, and exposes cross-user uploads as not found. The database also constrains attempt count to zero or one.

`BillStatementAiShadowEvaluationCoordinator` now composes the ledger with shadow evaluation. Deterministic extraction runs first; if incomplete, the ownership-scoped durable claim must succeed before the provider can be called. Duplicate, restarted, missing, or cross-user work suppresses the provider call. Terminal shadow status is recorded without storing provider details or extracted facts. If a caller cancels after acquiring the durable claim, the consumed attempt is recorded as `Canceled` before cancellation propagates, so it cannot become a later duplicate charge. The coordinator remains unregistered and disconnected from background processing.

`BillStatementAiShadowReadinessEvaluator` now defines a fail-closed, offline accuracy gate over aggregate ground-truth metrics only. The conservative private-beta baseline requires at least 100 statements across 5 providers with at least 10 statements per provider, 99% fact precision, 95% fact recall, 85% ready-candidate coverage, no more than 1% false alerts, and no more than 5% provider failures. Passing this metric gate is explicitly not authorization to persist AI-derived facts, and the evaluator is not registered at runtime.

The next activation checkpoint requires a ground-truth statement corpus, measured accuracy/false-alert thresholds, and explicit shadow-mode configuration. Do not route AI output into persistence before those gates pass.

## Remaining private-beta launch gates

1. Clean Visual Studio build and full test suite.
2. Finish any account lifecycle failures exposed by tests.
3. Complete security/privacy audit and verify data deletion/export end to end.
4. Replace local-only client API configuration with the deployed API endpoint once hosting is selected.
5. Deploy API + PostgreSQL with HTTPS, secret storage, persistent Data Protection keys, private durable statement storage, backups, and monitoring.
6. Validate real Plaid institutions and failure/reconnect behavior.
7. Build a ground-truth provider statement corpus and measure false alerts/extraction accuracy.
8. Run internal Beta 0 on real bills, then invite 3–5 trusted testers.

## Development workflow

Use coherent batches of complete files. Stop roadmap progression immediately for compiler/runtime/test failures. Do not guess the contents of large current files; the latest supplied source is authoritative. Aim for 0 errors and 0 warnings when reasonably achievable.
