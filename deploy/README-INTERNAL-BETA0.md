# BillWatch Internal Beta 0 acceptance

`deploy/run-internal-beta0.sh` composes the already-guarded production smoke harnesses into one release-pinned acceptance run. It is intended for a controlled private-beta account set on the actual deployed release; it is not a CI substitute and it does not bypass any child harness safety control.

## Safety boundary

The runner requires `BILLWATCH_BETA0_ALLOW=true`, an exact `.billwatch-release`/Git HEAD match, and a clean tracked worktree. A complete run executes direct API, Web/BFF, access-key lifecycle, Plaid lifecycle, and statement lifecycle gates. Access-key creation/revocation and statement upload remain independently fail-closed behind their existing mutation opt-ins. Plaid disconnect remains independently disabled unless its existing explicit disconnect opt-in and disposable connection ID are supplied.

By default, disabling any lifecycle phase makes the runner fail before testing. `BILLWATCH_BETA0_ALLOW_PARTIAL=true` permits intentional diagnostic/partial runs, but those runs are labeled `partial` and must never be recorded as completed Internal Beta 0 evidence.

## Controlled credentials and fixtures

Configure the environment required by each child harness before starting. In particular, use the existing `BILLWATCH_SMOKE_*`, `BILLWATCH_WEB_SMOKE_*`, `BILLWATCH_ACCESS_KEY_SMOKE_*`, `BILLWATCH_PLAID_SMOKE_*`, and `BILLWATCH_STATEMENT_SMOKE_*` variables documented in `README-BETA-CHECKLIST.md`. Keep password/2FA files mode 600 and outside the repository. Use a disposable access-key redeemer, controlled Plaid connection, and non-sensitive representative statement fixture.

For the mutation-bearing phases, explicitly set their existing safeguards as appropriate:

```sh
export BILLWATCH_ACCESS_KEY_SMOKE_ALLOW_MUTATIONS=true
export BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD=true
# Optional destructive Plaid proof only with a disposable connection:
# export BILLWATCH_PLAID_SMOKE_ALLOW_DISCONNECT=true
# export BILLWATCH_PLAID_SMOKE_DISCONNECT_CONNECTION_ID='<disposable-guid>'
```

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

The optional evidence file contains only the result, release SHA, UTC timestamps, and passed phase names. Its parent directory must already exist; the runner writes the file mode 600 and refuses a symlink or any path inside the deployment checkout. It never records credentials, tokens, statement contents, provider secrets, URLs, response bodies, or account identifiers.

A `complete` result proves only the automated Internal Beta 0 acceptance gates. It does **not** replace human completion/observation of Plaid institution authorization, provider-side backup immutability, external alert delivery, clean-host recovery, controlled reboot recovery, or qualified Terms/Privacy review. Those remain separate launch evidence.
