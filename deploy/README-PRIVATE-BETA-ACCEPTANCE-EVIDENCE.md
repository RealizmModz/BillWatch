# Private-beta acceptance evidence

BillWatch keeps machine proofs and human observations distinct, then allows them to be correlated to one exact deployed release without overstating what they prove.

## Plaid Hosted Link observation

Use a controlled private-beta account and an existing owned Plaid connection. Store the password in a non-symlink mode-600 file outside the checkout, and choose protected pending/completed evidence paths outside the checkout.

Set `BILLWATCH_PLAID_OBSERVATION_EMAIL`, `BILLWATCH_PLAID_OBSERVATION_PASSWORD_FILE`, `BILLWATCH_PLAID_OBSERVATION_CONNECTION_ID`, `BILLWATCH_PLAID_OBSERVATION_PENDING_FILE`, and `BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE`. Then explicitly opt in with `BILLWATCH_PLAID_OBSERVATION_ALLOW_PREPARE=true` and run:

```sh
sh /opt/billwatch/deploy/run-plaid-observation-proof.sh prepare /opt/billwatch https://api.billwatch.example
```

The command authenticates the controlled tester, verifies ownership of the configured connection, creates a real update-mode Hosted Link session, rejects non-HTTPS/non-Plaid destinations, and writes a protected release-pinned pending record. The Hosted Link URL is printed for the operator but is never stored in completed evidence.

Complete the flow in Plaid Hosted Link. Then set the exact confirmation phrase shown by the script contract:

```sh
export BILLWATCH_PLAID_OBSERVATION_CONFIRMATION='I completed the BillWatch Plaid update flow in Plaid Hosted Link'
sh /opt/billwatch/deploy/run-plaid-observation-proof.sh confirm /opt/billwatch https://api.billwatch.example
```

Confirmation calls the real server-side Hosted Link completion endpoint, runs connection-scoped account and transaction syncs, and requires the owned connection to be Active with a successful sync timestamp before publishing metadata-only evidence. Completed evidence contains no password, bearer token, Hosted Link URL, session ID, connection ID, institution name, or provider response body.

## Same-release acceptance bundle

After the existing machine technical proof, alert-observation proof, and Plaid-observation proof are complete for the same release, set their evidence paths plus a new output path and run:

```sh
sh /opt/billwatch/deploy/verify-private-beta-acceptance-evidence.sh /opt/billwatch
```

The verifier refuses cross-release, incomplete, symlinked, weakly permissioned, or in-checkout evidence and can atomically publish a mode-600 metadata-only acceptance record.

This bundle still does **not** claim provider-enforced immutable/Object-Lock/WORM backup protection or qualified Terms/Privacy review. Those remain separate launch gates and must not be inferred from a successful acceptance evidence file.
