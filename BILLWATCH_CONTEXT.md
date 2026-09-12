# BillWatch Current Context

Last updated: 2026-09-11

## Authority / continuation rules

This is the durable BillWatch development handoff. Current source and exact-head CI results win over this file for implementation details.

- Stop immediately for compile/runtime/test/CI/deployment failures caused by current work, destructive migration risk, genuine security problems, or unresolved architecture uncertainty.
- Never weaken authentication, BFF isolation, antiforgery, HTTPS, ownership checks, trusted-proxy rules, token protection, statement protections, backup protections, migration safety, or financial-data boundaries to pass a check.
- Work in coherent slices. Do not merge a feature branch until its exact final head has passed the complete CI/container/recovery gate.
- Never deploy a feature branch directly to production. Use the guarded release path from a verified `master` commit.
- Prefer useful code or real acceptance work over repeated audits and synthetic acceptance artifacts.
- Do not start lower-priority P1 expansion while genuine P0 private-beta acceptance gates remain open unless acceptance exposes a concrete P0 defect that needs code changes.

## Product promise

**Know when your bills change — and why.**

BillWatch is transaction-first. Bank transactions discover recurring bills. Provider statements/evidence explain why bills changed. AI may produce structured candidate facts, but deterministic code validates evidence, performs arithmetic, enforces ownership/security, compares history, and makes final persistence/alert decisions. AI output is never evidence by itself.

## Repository / stack

Repository: `RealizmModz/BillWatch`

Default/release branch: `master`
Active integration branch: `development`

PR #81, `Harden CI, BFF boundaries, and financial security regression coverage`, was merged into `development` on 2026-09-11 after its exact final feature-branch head passed the complete three-job CI gate.

Stack: .NET 10 MAUI + ASP.NET Core API + Blazor Interactive Server Web/BFF, PostgreSQL/EF Core, Identity bearer auth, encrypted HttpOnly Web/BFF auth, Plaid, xUnit, PdfPig, Tesseract, Docker Compose/Caddy/systemd, encrypted Restic recovery.

Public Web: `https://billbeacon.net`
Public API: `https://api.billbeacon.net`
Production path: `/opt/billwatch`

## Security invariants

- Plaid access tokens remain server-side/protected at rest.
- Web bearer/refresh tokens remain inside encrypted HttpOnly BFF state and are not intentionally exposed to browser JavaScript.
- External provider ID tokens/proofs must not be exposed to browser JavaScript. Temporary provider proof is held server-side in the short-lived encrypted HttpOnly external-auth session.
- User financial resources and statements remain ownership-scoped; cross-user IDs normally return 404 where appropriate.
- Staff roles do not grant access to another user's financial evidence.
- Statement storage paths never leave the API; signature/type/size validation remains enforced.
- Financial/auth API and BFF responses remain no-store.
- Production requires persistent Data Protection keys, explicit statement storage, Plaid credentials, AllowedHosts, and trusted reverse-proxy configuration.
- Never log raw statements, full account numbers, auth/Plaid/provider tokens, passwords, recovery codes, provider/database/Restic secrets, or private operations webhooks.
- AI-derived persistence remains disabled; deterministic extraction remains production persistence.

## Current hardened development baseline

The newest verified development feature head is:

- Feature head: `014ac49064eaa17da68262d39d7d5d4023a6d1bc`
- PR: #81
- BillWatch CI: **#513**, completed successfully on 2026-09-11 on that exact head.
- CI #513 passed all three jobs: `Backend build and tests`, `MAUI Android build`, and `Linux production container`.
- PR #81 was then merged into `development` as merge commit `bef5133dfedd0fff3a2ce2c4058be51272b20895`.

The hardening batch changed 51 files across 56 feature-branch commits. It added the MAUI Android Release build as a required CI surface and expanded repository hygiene, BFF malformed-request handling, backup trust-boundary hardening, authorization/ownership/cache/security-header/query-boundary regression coverage, documentation portability, and other low-risk reliability hardening.

The MAUI gate exposed two real build defects during validation. Both were fixed before the final green run: the MAUI restore invocation was corrected, and the unsupported `Entry.AutoCapitalization` use in the account-deletion confirmation UI was replaced with supported MAUI keyboard configuration. The final CI run completed with the Android build green.

Do not treat the `development` merge commit itself as a production release. Promotion to `master` and guarded production deployment remain separate release decisions.

## Verified P0/private-beta code position

The repository includes regression-tested P0 work covering identity/BFF isolation, ownership, Plaid state handling, statement validation and processing, subscription rollout gating, backup/recovery, operations alerts, release integrity, controlled reboot evidence, and private-beta evidence correlation.

Important verified identity/security slices include:

- centralized Web antiforgery, security headers, no-store boundaries, sensitive endpoint authorization, and user-partitioned authenticated rate limits;
- Owner/Admin and access-key privilege boundaries with role-claim freshness;
- versioned private-beta Terms/Privacy acceptance across Web, MAUI, and direct API registration;
- account deletion reauthentication/2FA/staff-role protections, Plaid revoke-first behavior, crash-safe statement quarantine/reconciliation, owned-data erasure, and deployed-release disposable-account deletion proof;
- server-side BFF access-token refresh with rotated-token persistence and fail-closed sign-out on refresh failure;
- objective cross-user Web/BFF ownership smoke using a second controlled identity and real foreign-owned resources;
- external sign-in support for Google/Apple/Microsoft with BillWatch 2FA completion after provider proof;
- one-time recovery-code support for external sign-in, external identity unlink, and external identity link flows;
- external identity unlink requires current BillWatch password and, when 2FA is enabled, exactly one authenticator code or unused recovery code;
- external identity link preserves the temporary provider proof only in the encrypted HttpOnly external session, requires BillWatch reauthentication, and routes authenticator and recovery-code factors distinctly;
- recovery codes retain one-time redemption semantics and ambiguous dual-factor submissions fail closed.

Important verified Plaid/statement/beta slices include:

- Plaid `RequiresAttention` classification/persistence, repair-state retention, retry stopping, safe disconnect, and ownership isolation;
- PDF/JPG/JPEG/PNG statement signature validation, upload/status/download ownership, terminal-state semantics, native/scanned OCR coverage, and storage-path secrecy;
- guarded direct API, authenticated Web/BFF, Owner/Admin, access-key, Plaid, statement lifecycle, statement semantic-review, subscription lifecycle, and account-deletion smoke/proof harnesses;
- subscription rollout preflight while global subscription enforcement remains OFF;
- Internal Beta 0 runner requiring release-matched acceptance evidence;
- two-phase release-pinned Plaid Hosted Link observation proof requiring human completion plus objective Active/sync verification.

Important verified backup/operations slices include:

- encrypted Restic backup/restore verification and guarded clean-host recovery drill with isolated PostgreSQL and no production volumes;
- append-only routine backup boundary with separate delete-capable retention-maintenance authority;
- runtime watchdog, release-integrity checks, metadata-only operations alerts, independent external readiness alerts, and controlled reboot pre/postflight proof;
- release-pinned technical, alert-observation, Plaid-observation, and private-beta acceptance evidence verifiers;
- trusted-beta launch evidence gate requiring complete machine acceptance plus explicit provider-immutability/protected-recovery and qualified Terms/Privacy review attestations.

## Production baseline

Production remains a separate release line from the newly merged `development` hardening batch. Do not claim PR #81 is deployed merely because it is green and merged to `development`.

The previously verified production release remains the last known guarded deployment until a newer `master` release is explicitly promoted, passes its release gate, and is deployed through the guarded production path.

## Current machine-verifiable P0 position

No compile, test, CI, container, or recovery-simulation failure remains open from PR #81. Its exact final feature head passed the complete current CI gate before merge.

Most remaining private-beta P0 items are **real-environment acceptance gates**, not missing generic application code. Do not manufacture extra scripts merely to make human/provider facts look machine-verifiable.

In particular:

- provider-enforced immutable/Object-Lock/WORM/equivalent backup behavior cannot be truthfully claimed until the actual off-host backup provider and its retention/delete/version semantics are known and configured;
- qualified legal review cannot be replaced by application code;
- human Plaid Hosted Link/update-mode behavior and alert receipt require real observation;
- controlled reboot acceptance requires an actual reboot between the existing guarded preflight/postflight phases;
- representative statement semantic accuracy requires comparison against operator-known facts.

## Production/rollout rules

- Global subscription enforcement remains OFF until its separate rollout gate is deliberately approved.
- Staff roles do not grant access to another user's financial evidence.
- AI-derived persistence remains disabled.
- Startup EF migrations mean production remains one API instance until migration ownership is redesigned.
- Never run `docker compose down --volumes` against production.
- Beta Terms/Privacy are operational drafts, not qualified legal review.
- Guarded-deploy only a fully green `master` release through the normal release path.

## Remaining real-environment private-beta gates

Before trusted external beta invitations:

- guarded-deploy the final green release from the normal release path;
- run authenticated direct API/Web-BFF/admin/access-key/Plaid/statement/subscription smoke with controlled identities and fixtures;
- run the objective cross-user Web/BFF ownership smoke with a second controlled identity that owns a real controlled statement fixture;
- run the disposable account-deletion proof and feed its same-release evidence into Internal Beta 0;
- complete the human Plaid Hosted Link/update-mode flow and finalize the release-pinned Plaid observation proof after Active/sync verification;
- review representative PDF/scanned-PDF/JPG/PNG extraction/OCR fields and bill-change explanations against operator-known facts;
- run clean-host restore against the actual off-host repository and record same-release recovery evidence;
- configure provider-enforced immutable/Object-Lock/WORM/equivalent protection, prove recovery from that protected path, and only then record the explicit same-release backup approval attestation;
- run the alert-observation proof and personally confirm the challenge in both independent destinations;
- perform controlled reboot preflight, manual reboot, and postflight, then record same-release reboot evidence;
- combine same-release technical, alert, and Plaid evidence with the private-beta acceptance verifier;
- complete Internal Beta 0 on real controlled bills with explicit expected subscription state where known;
- obtain qualified review of the exact deployed Terms/Privacy version and record the legal approval attestation;
- run the trusted-beta launch evidence verifier only after every underlying real-environment fact above is genuinely complete.

## Immediate resume point

1. Treat PR #81 / exact green feature head `014ac49064eaa17da68262d39d7d5d4023a6d1bc` / BillWatch CI #513 as the newest verified development hardening checkpoint.
2. `development` contains that batch at merge commit `bef5133dfedd0fff3a2ce2c4058be51272b20895`; do not confuse that integration merge with a production deployment.
3. Check the latest `master`, `development`, open PRs, and CI before the next code or release change.
4. Prefer the next genuine real-environment P0 acceptance step or another evidence-backed hardening slice. If new code changes are made, use a focused branch from current `development` and require the full three-job CI gate before merge.
5. Preserve every security invariant above and all user-owned data ownership boundaries.
