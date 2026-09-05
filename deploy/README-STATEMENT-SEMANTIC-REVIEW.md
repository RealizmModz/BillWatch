# Controlled Statement Semantic Review

Use `deploy/review-statement-semantics.sh` after a controlled statement fixture has already been uploaded and processed. This gate is intentionally read-only: it does not upload, modify, acknowledge, delete, synchronize, or otherwise mutate BillWatch data.

Its purpose is to close the gap between “the statement processed” and “the persisted financial facts are actually correct.” The operator supplies expected facts from a known controlled fixture, and the gate verifies the authenticated Bill Stream detail response against those expectations.

## Required setup

- Run only against HTTPS.
- Use a controlled beta account and Bill Stream that you own.
- Use the exact persisted statement ID produced by the controlled fixture. Do not rely on “latest statement” ordering.
- Put the account password in a regular, non-symlink file with mode `600` and point `BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE` to it.
- Install `jq` on the operator host.
- Do not put real statement text, account numbers, credentials, or provider secrets into shell history or committed files.

Required expectations:

```sh
export BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID='...'
export BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID='...'
export BILLWATCH_SEMANTIC_REVIEW_EMAIL='controlled-beta@example.com'
export BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE='/secure/path/billwatch-semantic-review.password'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER='Example Electric'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY='Utility'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START='2026-07-01'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END='2026-07-31'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT='125.45'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY='USD'
```

Optional date expectations may be an exact ISO date, `null`, or omitted/`any`:

```sh
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_STATEMENT_DATE='2026-08-01'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_DUE_DATE='2026-08-20'
```

Run:

```sh
sh deploy/review-statement-semantics.sh https://api.billbeacon.net
```

The script prints only PASS labels; it does not print the expected values or API response bodies.

## Optional bill-change proof

If the controlled fixture is expected to produce a deterministic `BillChange`, set the exact change ID and all core expected financial fields:

```sh
export BILLWATCH_SEMANTIC_REVIEW_CHANGE_ID='...'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_TYPE='AmountChanged'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CONFIDENCE='High'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_PREVIOUS_AMOUNT='110.00'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CURRENT_AMOUNT='125.45'
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_DIFFERENCE='15.45'
```

An optional private expected phrase can also be checked without printing it:

```sh
export BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_DESCRIPTION_CONTAINS='Usage charge increased'
```

Do not commit that phrase if it contains real customer information.

## Optional ownership probe

A controlled Bill Stream ID belonging to a different beta user can be supplied as:

```sh
export BILLWATCH_SEMANTIC_REVIEW_FOREIGN_BILL_STREAM_ID='...'
```

The authenticated account must receive HTTP `404` for that detail request.

## What this proves

A successful run proves, for the selected controlled fixture and persisted IDs, that:

- the authenticated Bill Stream identity/provider/category are the expected ones;
- the expected statement exists on that stream;
- persisted period start/end, total amount, currency, and any supplied statement/due dates match the operator-known fixture facts;
- when configured, the selected BillChange type, confidence, previous/current amounts, difference, and optional explanation phrase match expectation;
- the public detail response does not expose known secret/internal-storage/raw-statement-text fields;
- optional cross-user detail access still returns `404`.

This is evidence for representative-fixture semantic correctness. It is not a claim that all providers/layouts are correct, it does not replace deterministic parser/unit coverage, and it does not authorize AI-derived persistence. Repeat it across representative PDF, scanned PDF, JPG, and PNG fixtures before treating extraction semantics as broadly beta-ready.
