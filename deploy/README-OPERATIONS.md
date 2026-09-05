# BillWatch production operations

Run production commands as the deployment account that owns `/opt/billwatch/.env.production`.

Never paste production secrets into chat, issue trackers, shell history, or logs.

## Verify a deployed release

```sh
cd /opt/billwatch
sh deploy/verify-production.sh /opt/billwatch
```

This verifies the protected production environment, container exposure/health, exact release marker, checked-out commit, and public API/Web readiness.

## Private-beta host verification

After a guarded production deployment, run:

```sh
cd /opt/billwatch
sh deploy/verify-beta-readiness.sh /opt/billwatch
```

This composes production verification with role/Owner checks, subscription-enforcement state, daily backup/runtime-watchdog timers, completed encrypted backup evidence, configured retention floors, and operations-alert wiring.

Passing this command does **not** prove browser behavior, Plaid provider behavior, representative statement semantics, clean-host restore, a controlled reboot, storage-provider immutability, independent external monitoring, or actual external alert delivery. Those remain explicit operator gates in `deploy/README-BETA-CHECKLIST.md`.

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

The admin smoke test proves that a fresh bearer session can satisfy `AdminOrOwner`; it does not mutate roles or access keys. Command-line smoke tests do not replace the guarded private-beta/BFF/Plaid/statement/Internal Beta 0 flows documented elsewhere in `deploy/`.

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

The runtime watchdog waits briefly after boot and then executes the guarded production verifier every five minutes. It verifies checked-out source, protected release marker, running release revisions, exposure rules, container health, and public readiness. It does **not** automatically deploy, roll back, restart PostgreSQL, or rewrite the verified release marker. A failure is surfaced through the metadata-only operations alert path.

Verify local watchdog installation/scheduling with:

```sh
sh deploy/check-runtime-watchdog.sh
```

An actual controlled VPS reboot must still be performed once before beta invitations to prove Docker/systemd/network ordering on the real host. Use `deploy/run-controlled-reboot-drill.sh` for the release-pinned preflight/postflight proof; the runner never initiates reboot itself.

## Backups

The production backup timer should remain enabled:

```sh
systemctl list-timers billwatch-backup.timer --all
```

A manual backup can be requested with:

```sh
sudo systemctl start billwatch-backup.service
sudo systemctl status billwatch-backup.service --no-pager
```

Verify the timer and latest completed encrypted snapshot with:

```sh
sh deploy/check-backup-timer.sh
sh deploy/check-backup-snapshot.sh /opt/billwatch
```

### Backup/maintenance trust separation

Production `.env.production` must contain:

```text
BILLWATCH_BACKUP_CLIENT_MODE=append-only
```

Routine backup capture is deliberately non-destructive at the BillWatch application boundary. The `backup` command refuses maintenance mode and **never** invokes `restic forget` or `restic prune`, even when retention is enabled. The production storage credential should independently be restricted by the provider so it cannot delete/overwrite protected backup data where that backend supports separate permissions.

Retention deletion is a separate trusted-host operation. Do **not** place delete-capable maintenance credentials or a maintenance environment on the production VPS. See `deploy/README-BACKUP-TRUST.md`.

### Clean-host/off-host recovery drill

Run this gate from a separate clean recovery host, not from the live production host. Check out the exact release commit, ensure the checkout is clean, and create a Git-ignored mode-`600` `.env.recovery` owned by the operator. It must contain only the recovery credentials/configuration needed by the isolated drill, including:

```text
BILLWATCH_RECOVERY_DRILL_ALLOW=true
BILLWATCH_RELEASE_ID=<exact-lowercase-40-character-release-sha>
BILLWATCH_DATABASE_PASSWORD=<temporary-isolated-postgres-password>
RESTIC_REPOSITORY=<off-host-restic-repository>
RESTIC_PASSWORD=<protected-restic-password>
```

Include only storage-provider credentials required by that Restic backend. Do not copy Plaid, Stripe, identity-email, Web, or other unrelated production secrets to the recovery host.

Run:

```sh
cd /opt/billwatch-recovery
sh deploy/run-clean-host-recovery-drill.sh .env.recovery
```

The runner refuses dirty or release-mismatched source, symlinked/wrong-owner/non-`600` recovery files, missing explicit opt-in, and local Restic repository paths. It uses `compose.recovery-drill.yml`, which contains only an ephemeral PostgreSQL restore target and hardened backup verifier. No host ports are published and no production database, statement, Data Protection, Caddy, API, or Web volumes are mounted.

A pass proves that the selected off-host encrypted snapshot can be decrypted, checksum-validated, restored into fresh PostgreSQL, and reconciled with migration history, statement manifest/files, and Data Protection keys. Delete the temporary recovery credentials according to the operator/provider procedure afterward.

Provider-side immutability remains a separate proof: recovery must also be demonstrated from storage that is actually protected by Object Lock/WORM/append-only or an equivalent provider-enforced design.

### Retention maintenance

Configure the desired policy in production and the trusted maintenance environment at no less than:

```text
BILLWATCH_BACKUP_RETENTION_ENABLED=true
BILLWATCH_BACKUP_KEEP_DAILY=14
BILLWATCH_BACKUP_KEEP_WEEKLY=8
BILLWATCH_BACKUP_KEEP_MONTHLY=12
BILLWATCH_BACKUP_KEEP_YEARLY=3
```

Inspect the configured production policy non-destructively with:

```sh
sh deploy/check-backup-policy.sh /opt/billwatch
```

Normal backups do not apply deletion. On the separate trusted maintenance host, create a mode-`600` environment file outside the checkout using a delete-capable maintenance principal and include:

```text
BILLWATCH_RELEASE_ID=<exact-release-sha>
BILLWATCH_BACKUP_CLIENT_MODE=maintenance
BILLWATCH_BACKUP_MAINTENANCE_ALLOW=true
BILLWATCH_BACKUP_RETENTION_ENABLED=true
```

plus the same policy floors, encrypted repository location/password, and only the provider credentials needed for maintenance. Then run from a clean checkout of the exact release:

```sh
sh deploy/run-backup-maintenance.sh /secure/path/billwatch-backup-maintenance.env
```

The runner refuses weak file permissions, repository-local maintenance secrets, release mismatch/dirty source, local repositories, append-only mode, missing explicit maintenance opt-in, or disabled retention. It builds the exact release's backup image and runs only the retention command in a read-only, capability-dropped container without mounting production data.

The backup tool itself additionally refuses `retention` unless both maintenance mode and the explicit maintenance opt-in are present. It validates the retention floors before `restic forget --prune` and runs `restic check` afterward.

This separation reduces the impact of a production-host compromise; it does **not** prove provider immutability. The provider-side protection and recovery proof remain launch gates.

### Operations alerts

Configure a private HTTPS webhook in `.env.production`:

```text
BILLWATCH_OPERATIONS_ALERTING_ENABLED=true
BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL=https://your-private-alert-endpoint.example/path
```

`billwatch-backup.service` and `billwatch-runtime-readiness.service` use systemd `OnFailure` to invoke the dedicated alert unit. The payload contains only a fixed BillWatch source identifier, event name, systemd unit name, hostname, and UTC timestamp. It does not attach service logs, financial data, request bodies, credentials, or tokens.

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
