# BillWatch production operations

Run production commands as the deployment account that owns `/opt/billwatch/.env.production`.

Never paste production secrets into chat, issue trackers, shell history, or logs.

## Verify a deployed release

```sh
cd /opt/billwatch
sh deploy/verify-production.sh /opt/billwatch
```

This verifies:

- `.env.production` is mode 600, deployment-account owned, ignored by Git, and not tracked;
- API, Web, and PostgreSQL publish no host ports;
- Caddy is the only public edge and publishes 80/443;
- database, API, Web, and edge containers are running;
- database, API, and Web health checks are healthy;
- `.billwatch-release` matches the checked-out Git commit;
- public API and Web readiness checks succeed.

## Private-beta host verification

After a guarded production deployment, run:

```sh
cd /opt/billwatch
sh deploy/verify-beta-readiness.sh /opt/billwatch
```

This composes the production verification with:

- Identity role-schema verification;
- exactly-one-Owner verification;
- confirmation that subscription enforcement remains disabled;
- enabled/active daily backup timer verification;
- enabled/active runtime-readiness watchdog verification;
- existence of a completed encrypted `billwatch-complete` Restic snapshot;
- enabled backup retention at or above BillWatch's minimum retention floors;
- installed backup/runtime-failure alert routing with an HTTPS external webhook configured.

Passing this command does **not** prove the browser, Plaid, provider-statement, clean-host restore, an actual controlled reboot, storage-provider immutability, independent external monitoring, or actual external alert delivery. Those remain explicit operator checks in `deploy/README-BETA-CHECKLIST.md`.

## First Owner

See `deploy/README-FIRST-OWNER.md`.

The guarded bootstrap is intentionally unavailable after the first Owner exists. It requires exactly one application user, exactly one seeded Owner role, zero existing Owners, and an exact match to the supplied account email.

## Authentication smoke tests

The smoke scripts never accept passwords, bearer tokens, Plaid credentials, database passwords, or access keys as command-line arguments. Password input is read interactively with terminal echo disabled. Temporary authentication material is stored only in a private temporary directory and deleted on exit.

General authenticated API reads:

```sh
cd /opt/billwatch
sh deploy/smoke-authenticated-api.sh https://api.billbeacon.net
```

Owner/Admin policy check:

```sh
cd /opt/billwatch
sh deploy/smoke-admin-api.sh https://api.billbeacon.net
```

The admin smoke test proves that a fresh bearer session can satisfy `AdminOrOwner`; it does not mutate roles or access keys.

Command-line smoke tests do not replace browser verification of login/logout, Overview, Bills, Activity, Account, Subscription, Admin, Plaid Hosted Link/update mode, statement upload/download, or responsive theme behavior.

## Systemd production units

Install the backup, runtime-watchdog, and alert units together so no `OnFailure` route can point at a missing template:

```sh
sudo cp \
  deploy/systemd/billwatch-backup.service \
  deploy/systemd/billwatch-backup.timer \
  deploy/systemd/billwatch-runtime-readiness.service \
  deploy/systemd/billwatch-runtime-readiness.timer \
  deploy/systemd/billwatch-operations-alert@.service \
  /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now \
  billwatch-backup.timer \
  billwatch-runtime-readiness.timer
```

The runtime watchdog waits briefly after boot and then executes the same guarded production verifier every five minutes. It verifies the checked-out source, protected release marker, running release revisions, exposure rules, container health, and public readiness. It **does not** automatically deploy, roll back, restart PostgreSQL, or rewrite the verified release marker. A failure is surfaced through the metadata-only operations alert path.

Verify the watchdog locally with:

```sh
sh deploy/check-runtime-watchdog.sh
```

This proves installation and scheduling; an actual controlled VPS reboot must still be performed once before beta invitations to prove Docker/systemd/network ordering on the real host.

## Backups

The production backup timer should remain enabled:

```sh
systemctl list-timers billwatch-backup.timer --all
```

A manual backup can be requested with:

```sh
sudo systemctl start billwatch-backup.service
```

Then inspect its status without printing secrets:

```sh
sudo systemctl status billwatch-backup.service --no-pager
```

Verify the timer with:

```sh
sh deploy/check-backup-timer.sh
```

Verify a completed encrypted snapshot with:

```sh
sh deploy/check-backup-snapshot.sh /opt/billwatch
```

### Retention

Retention is intentionally opt-in and applies only to completed `billwatch-complete` snapshots. Configure at least:

```text
BILLWATCH_BACKUP_RETENTION_ENABLED=true
BILLWATCH_BACKUP_KEEP_DAILY=14
BILLWATCH_BACKUP_KEEP_WEEKLY=8
BILLWATCH_BACKUP_KEEP_MONTHLY=12
BILLWATCH_BACKUP_KEEP_YEARLY=3
```

The backup container refuses a lower policy before any `restic forget --prune` operation. Each successful backup applies the enabled retention policy only after the new snapshot has passed `restic check` and been promoted from the candidate tag to `billwatch-complete`.

Verify configuration non-destructively with:

```sh
sh deploy/check-backup-policy.sh /opt/billwatch
```

This repository-level policy does **not** provide immutability against a compromised host that possesses delete-capable storage credentials. Configure provider-side Object Lock/WORM/append-only retention where supported, and use separate backup-write versus retention-delete credentials when possible.

### Operations alerts

Configure a private HTTPS webhook in `.env.production`:

```text
BILLWATCH_OPERATIONS_ALERTING_ENABLED=true
BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL=https://your-private-alert-endpoint.example/path
```

`billwatch-backup.service` and `billwatch-runtime-readiness.service` use systemd `OnFailure` to invoke the dedicated alert unit. The alert payload contains only a fixed BillWatch source identifier, event name, systemd unit name, hostname, and UTC timestamp. It does not attach service logs, financial data, request bodies, credentials, or tokens.

Verify local wiring without sending an alert:

```sh
sh deploy/check-operations-alerting.sh /opt/billwatch
```

Then prove real external delivery once before beta invitations:

```sh
sh deploy/send-operations-alert.sh /opt/billwatch readiness-test manual
```

Confirm the external system received the test event. A configured-but-unproven webhook does not close the external alert-delivery gate.

Do not remove a backup lock unless you have first confirmed no backup process is active.

## Deployment

Use only the guarded deployment script:

```sh
cd /opt/billwatch
RELEASE_ID="$(git rev-parse HEAD)"
sed -i "s/^BILLWATCH_RELEASE_ID=.*/BILLWATCH_RELEASE_ID=$RELEASE_ID/" .env.production
sh deploy/deploy-production.sh .env.production
```

Do not use `docker compose down --volumes` in production.

Database migrations currently run at API startup, so production must remain at one API instance until migration ownership is redesigned.

## Subscription rollout

Keep `BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false` until the protected/exempt endpoint matrix has been production-smoke-tested and the separate rollout gate is explicitly approved.

Administrative staff roles do not grant access to another user's financial evidence.

## Admin/access-key rollout smoke sequence

Only after the role-aware API release passes build/tests/CI and has been guarded-deployed:

1. Run `sh deploy/verify-beta-admin.sh /opt/billwatch`.
2. Sign out of the Web application and sign back in so the API issues a fresh role-aware bearer session.
3. Run `sh deploy/smoke-admin-api.sh https://api.billbeacon.net`.
4. Open `/app/admin` in the browser.
5. Create one short-lived, single-redemption beta access key. Plaintext must appear once only.
6. Redeem it through `/app/subscription` using the intended test account.
7. Verify the entitlement state changes as expected.
8. Revoke or retire temporary test material when the smoke test is complete.

Do not enable global subscription enforcement as part of this sequence.
