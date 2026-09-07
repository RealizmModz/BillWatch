# BillWatch Internal Beta 0 acceptance

`deploy/run-internal-beta0.sh` composes the guarded production smoke/review harnesses into one release-pinned acceptance run. It is intended for controlled private-beta accounts on the exact deployed release; it is not a CI substitute and it does not bypass any child harness safety control.

## Complete-run boundary

The runner requires `BILLWATCH_BETA0_ALLOW=true`, an exact `.billwatch-release`/Git HEAD match, and a clean tracked worktree. A **complete** result requires all of the following:

- release-matched disposable account-deletion evidence;
- direct authenticated API verification;
- Web/BFF verification;
- Owner/Admin authorization with a distinct non-staff denial check;
- access-key lifecycle verification;
- Plaid lifecycle verification;
- statement upload/process/download integrity;
- controlled persisted statement semantic review; and
- subscription lifecycle verification.

Disabling a required live phase, or omitting the account-deletion evidence, fails before testing unless `BILLWATCH_BETA0_ALLOW_PARTIAL=true` is explicitly set. Partial runs are labeled `partial` and must never be treated as completed Beta 0 evidence.

## Disposable account-deletion proof

Account deletion is intentionally proven **outside** the repeatable Beta 0 runner because it destroys the tested identity. Run `deploy/smoke-account-deletion.sh` once for the exact release using a dedicated throwaway non-staff account, then supply its evidence file to Beta 0.

The deletion harness requires all of these safeguards before any request is made:

```sh
export BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW=true
export BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM=DELETE-THROWAWAY-ACCOUNT
export BILLWATCH_ACCOUNT_DELETE_SMOKE_EMAIL='delete-only@example.test'
export BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL='delete-only@example.test'
export BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE=/secure/operator/delete-password
export BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE=/var/lib/billwatch/account-delete-proof.state

sh /opt/billwatch/deploy/smoke-account-deletion.sh \
  /opt/billwatch \
  https://api.billbeacon.net
```

The password file must be an absolute, mode-600, non-symlink file outside the deployment checkout. The evidence destination must also be an absolute path outside the checkout and must not already exist. The throwaway identity is rejected if it matches another configured BillWatch smoke identity.

The harness proves that the disposable account exists and can export its own data, deletes it through `DELETE /api/account`, verifies the old bearer identity no longer resolves through account export, and verifies the deleted credentials receive HTTP 401 on a new login attempt. A 2FA requirement, staff-role protection, Plaid revocation failure, or statement-storage failure is treated as a deletion failure; the harness does not bypass or weaken those protections.

Successful evidence contains only format version, result, release SHA, UTC timestamps, and `PASSED_PHASES=account-deletion`. It never records the email, password, bearer token, response bodies, bank identifiers, or statement contents.

Before a complete Beta 0 run:

```sh
export BILLWATCH_BETA0_ACCOUNT_DELETE_EVIDENCE_FILE=/var/lib/billwatch/account-delete-proof.state
```

The runner requires that evidence to be a regular mode-600 non-symlink file whose `RELEASE_SHA` exactly matches the deployed `.billwatch-release`.

## Controlled credentials and fixtures

Configure the environment required by each child harness before starting. Keep password and 2FA files mode 600 and outside the repository. Use distinct controlled identities where the harnesses require them.

For the admin boundary:

```sh
export BILLWATCH_ADMIN_SMOKE_EMAIL='owner-controlled@example.test'
export BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE=/secure/operator/admin-password
export BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL='beta-controlled@example.test'
export BILLWATCH_ADMIN_SMOKE_NONSTAFF_PASSWORD_FILE=/secure/operator/nonstaff-password
```

The two identities must differ. The admin harness is read-only and proves HTTP 200 for Owner/Admin plus HTTP 403 for the non-staff identity.

For statement semantics, configure the `BILLWATCH_SEMANTIC_REVIEW_*` expectations documented in `README-STATEMENT-SEMANTIC-REVIEW.md`. Use exact controlled Bill Stream and persisted statement IDs and independently known expected financial facts.

For subscription correctness, configure explicit `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE`, `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID`, and `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PROVIDER_STATUS` expectations whenever the expected state is known. Stripe checkout creation, Customer Portal creation, and provider sync remain independently disabled unless their existing `BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_*` opt-ins are set.

Mutation-bearing child phases retain their own safeguards:

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

## Run Internal Beta 0

From the deployed checkout, normally `/opt/billwatch`:

```sh
export BILLWATCH_BETA0_ALLOW=true
export BILLWATCH_BETA0_ACCOUNT_DELETE_EVIDENCE_FILE=/var/lib/billwatch/account-delete-proof.state
export BILLWATCH_BETA0_EVIDENCE_FILE=/var/lib/billwatch/beta0-last-pass.state

sh /opt/billwatch/deploy/run-internal-beta0.sh \
  /opt/billwatch \
  https://api.billbeacon.net \
  https://billbeacon.net
```

The optional Beta 0 evidence file contains only the result, release SHA, UTC timestamps, and passed phase names. A complete record begins its phase list with `account-deletion` and also includes `admin-authz`, `statement-semantics`, and `subscription`. The runner refuses a symlink or evidence path inside the deployment checkout.

## What completion means

A `complete` result proves the automated Internal Beta 0 gates on the exact deployed release, including release-matched account deletion, Owner/Admin versus non-staff authorization, lifecycle integrity, controlled statement semantic correctness, and configured subscription assertions.

It does **not** prove checkout payment completion, webhook delivery, cancellation/expiration transitions, every provider/layout, or global subscription enforcement. It also does not authorize AI-derived persistence or replace human Plaid institution authorization/provider observation, provider-side backup immutability, external alert delivery, clean-host recovery, controlled reboot recovery, or qualified Terms/Privacy review. Those remain separate launch evidence.
