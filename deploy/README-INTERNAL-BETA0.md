# BillWatch Internal Beta 0 acceptance

`deploy/run-internal-beta0.sh` composes the guarded production smoke/review harnesses into one release-pinned acceptance run. It is intended for controlled private-beta accounts on the actual deployed release; it is not a CI substitute and it does not bypass any child harness safety control.

## Safety boundary

The runner requires `BILLWATCH_BETA0_ALLOW=true`, an exact `.billwatch-release`/Git HEAD match, and a clean tracked worktree. A **complete** run executes direct API, Web/BFF, access-key lifecycle, Plaid lifecycle, statement lifecycle, controlled statement semantic review, and subscription lifecycle verification.

The semantic-review phase is part of the definition of a complete Internal Beta 0 run. A statement that uploads/processes/downloads correctly is not enough: the persisted provider/category, statement period, amount/currency, optional dates, and any configured bill-change facts must also match an operator-known controlled fixture.

The subscription phase is also required for a complete run. It verifies the controlled account's subscription surface and plan response and can assert expected active/paid/provider state. Stripe checkout creation, Customer Portal creation, and provider sync remain independently disabled unless their existing explicit `BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_*` opt-ins are set; Internal Beta 0 does not weaken or bypass those safeguards.

Access-key creation/revocation and statement upload remain independently fail-closed behind their existing mutation opt-ins. Plaid disconnect remains independently disabled unless its existing explicit disconnect opt-in and disposable connection ID are supplied. Statement semantic review and the default subscription lifecycle path are read-only.

By default, disabling any required lifecycle or semantic phase makes the runner fail before testing. `BILLWATCH_BETA0_ALLOW_PARTIAL=true` permits intentional diagnostic/partial runs, but those runs are labeled `partial` and must never be recorded as completed Internal Beta 0 evidence.

## Controlled credentials and fixtures

Configure the environment required by each child harness before starting. Use the existing `BILLWATCH_SMOKE_*`, `BILLWATCH_WEB_SMOKE_*`, `BILLWATCH_ACCESS_KEY_SMOKE_*`, `BILLWATCH_PLAID_SMOKE_*`, `BILLWATCH_STATEMENT_SMOKE_*`, and `BILLWATCH_SUBSCRIPTION_SMOKE_*` variables documented in the beta/operator checklists.

For semantic correctness, also configure the `BILLWATCH_SEMANTIC_REVIEW_*` expectations documented in `README-STATEMENT-SEMANTIC-REVIEW.md`, including the exact controlled Bill Stream and persisted statement IDs. Do not rely on “latest statement” ordering. The semantic fixture may be the controlled statement uploaded by the lifecycle proof or another already-processed representative fixture, but its expected financial facts must be known independently of BillWatch.

For subscription correctness, configure explicit `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE`, `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID`, and `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PROVIDER_STATUS` expectations whenever the controlled account's expected state is known. Leaving an expectation at `skip` only proves that the response is well formed; it does not prove a specific paid/unpaid provider state.

Keep password/2FA files mode 600 and outside the repository. Use a disposable access-key redeemer, controlled Plaid connection, non-sensitive representative statement fixture, and controlled subscription account. Do not commit statement text, credentials, account numbers, expected private explanation text, provider secrets, or Stripe session URLs.

For mutation-bearing phases, explicitly set their existing safeguards as appropriate:

```sh
export BILLWATCH_ACCESS_KEY_SMOKE_ALLOW_MUTATIONS=true
export BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD=true
# Optional destructive Plaid proof only with a disposable connection:
# export BILLWATCH_PLAID_SMOKE_ALLOW_DISCONNECT=true
# export BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID='<disposable-guid>'
# Optional Stripe mutation-bearing probes only when deliberately approved:
# export BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_CHECKOUT=true
# export BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_PORTAL=true
# export BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_SYNC=true
```

A complete run leaves both `BILLWATCH_BETA0_RUN_STATEMENT_SEMANTICS=true` and `BILLWATCH_BETA0_RUN_SUBSCRIPTION=true` (the defaults). Disabling either requires `BILLWATCH_BETA0_ALLOW_PARTIAL=true` and produces only partial evidence.

## Run

From the deployed checkout, normally `/opt/billwatch`:

```sh
export BILLWATCH_BETA0_ALLOW=true
export BILLWATCH_BETA0_EVIDENCE_FILE=/var/lib/billwatch/beta0-last-pass.state

sh /opt/billwatch/deploy/run-internal-beta0.sh \
  /opt/billwatch \
  https://api.billbeacon.net \
  https://billbeacon.net
```

The optional evidence file contains only the result, release SHA, UTC timestamps, and passed phase names. A complete evidence record includes `statement-semantics` and `subscription` in `PASSED_PHASES`. Its parent directory must already exist; the runner writes the file mode 600 and refuses a symlink or any path inside the deployment checkout. It never records credentials, tokens, statement contents, provider secrets, URLs, response bodies, expected values, or account identifiers.

## What completion means

A `complete` result proves the automated Internal Beta 0 gates on the exact deployed release, including lifecycle integrity, controlled statement semantic correctness, and the configured subscription assertions. It does **not** prove checkout payment completion, webhook delivery, cancellation/expiration transitions, every provider/layout, or global subscription enforcement. It also does not authorize AI-derived persistence or replace human completion/observation of Plaid institution authorization, provider-side backup immutability, external alert delivery, clean-host recovery, controlled reboot recovery, or qualified Terms/Privacy review. Those remain separate launch evidence.
