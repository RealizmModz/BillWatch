# BillWatch

BillWatch is a transaction-first bill-intelligence product built with .NET 10, .NET MAUI, ASP.NET Core, PostgreSQL, and Plaid.

Its core promise is: **Know when your bills change — and why.**

## Start the API locally

The local API requires PostgreSQL plus Plaid sandbox credentials stored in .NET user secrets. Never commit credentials to this repository.

From PowerShell:

```powershell
dotnet run --launch-profile https --project "C:\Users\brist\source\repos\BillWatch\BillWatch.API\BillWatch.API.csproj"
```

Local endpoints:

- API: `https://localhost:7243`
- Liveness: `https://localhost:7243/health/live`
- Readiness: `https://localhost:7243/health/ready`

## Validate the backend

```powershell
dotnet build BillWatch.Tests\BillWatch.Tests.csproj --configuration Release
dotnet test BillWatch.Tests\BillWatch.Tests.csproj --configuration Release --no-build
```

GitHub Actions repeats the backend build and tests on every push and pull request. It also builds the Linux production container, including the native Tesseract/Leptonica OCR dependencies.

## Production deployment candidate

The included production stack runs four security boundaries on one Linux Docker host:

- Caddy terminates HTTPS and automatically manages TLS certificates.
- The ASP.NET Core API is reachable only through Caddy.
- PostgreSQL is reachable only on the private container network.
- Data Protection keys, statement files, database data, and TLS state use separate persistent volumes.

Requirements:

- A Linux server with Docker Engine and the Docker Compose plugin.
- Ports 80 and 443 open to the internet.
- A DNS record for the API hostname pointing to the server.
- Plaid credentials. Use `sandbox` until production access has been approved and verified.
- A private off-host Restic repository and backup-only credentials.

Deploy:

1. Copy `.env.production.example` to `.env.production` on the server.
2. Replace every placeholder, set `BILLWATCH_RELEASE_ID` to the exact deployed commit, and run `chmod 600 .env.production`. The backup wrapper refuses an environment file owned by another account or readable by group/other users.
3. Run the production preflight before Docker receives any configuration:

```sh
sh deploy/validate-production-env.sh .env.production
```

The preflight rejects linked or over-permissioned environment files, placeholders, weak database/backup passwords, local backup destinations, invalid Plaid environments, non-public hostnames, and release identifiers that are not exact lowercase 40-character Git commits. It never prints secret values.
3. Configure `RESTIC_REPOSITORY` as a private off-host destination and use a separate, randomly generated `RESTIC_PASSWORD`. Losing that password makes every backup unrecoverable.
4. Keep all AI flags disabled. No OpenAI key is required for the current runtime.
5. Initialize the encrypted repository once:

```bash
docker compose --env-file .env.production --file compose.production.yml --profile operations build backup
docker compose --env-file .env.production --file compose.production.yml --profile operations run --rm backup init
```

6. Deploy from a clean checkout whose `HEAD` exactly matches `BILLWATCH_RELEASE_ID`:

```bash
sh deploy/deploy-production.sh .env.production
```

The deployment command re-runs the fail-closed configuration preflight, rejects a dirty or mismatched checkout, prevents overlapping deploys, validates Compose, builds immutable release-tagged API and recovery images, and creates a verified encrypted recovery point before replacing an already-running API. It waits for every production service and requires the exact external HTTPS readiness response before atomically recording the deployed release. It never performs an automatic database rollback.

7. Confirm both health endpoints over the public HTTPS hostname. If deployment fails after service replacement begins, inspect the bounded sanitized logs printed by the command before retrying; the last verified release marker remains unchanged.
8. Build the MAUI release with the exact deployed origin:

```powershell
dotnet build BillWatch.csproj --configuration Release -p:BillWatchApiBaseUrl=https://api.example.com/
```

The API applies EF Core migrations during startup in this single-instance deployment. Do not scale the API above one instance while startup migration is enabled; a multi-instance platform should run migrations as a separate one-time release job.

The API and recovery images are tagged with `BILLWATCH_RELEASE_ID`, and every encrypted backup records that same release. Keep that image and source revision available until the next backup and recovery verification pass so rollback does not depend on rebuilding a floating tag.

## Required production configuration

The application fails closed outside Development unless these settings are present:

- `ConnectionStrings__BillWatchDatabase`
- `DataProtection__KeysPath`
- `BillStatementStorage__RootPath`
- `Plaid__ClientId`
- `Plaid__Secret`
- `Plaid__Environment`
- `AllowedHosts`

When TLS terminates at a reverse proxy, configure only its trusted address under `ReverseProxy__KnownProxies`. The included Compose network pins Caddy to `172.28.0.10` and trusts only that address.

Subscription enforcement is controlled by `BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED` and defaults to `false`. `BILLWATCH_SUBSCRIPTION_ENFORCEMENT_COHORT` accepts `InternalTester`, `BetaTester`, or `All`; unknown values fail closed as `All`. Enable it only after validating the entitlement and access-key flow. When enabled, targeted users need an active entitlement for financial routes, while subscription recovery, staff administration, data export, bank disconnection, and account deletion remain available through explicit endpoint metadata.

## Operations and recovery

- `/health/live` proves the process is running.
- `/health/ready` proves the database is reachable, migrations are current, and both sensitive persistent directories are writable. It never returns connection strings or physical paths.
- Monitor both endpoints externally and alert on repeated readiness failure.
- Docker logs are size- and count-limited so a runaway process cannot consume the host disk indefinitely.
- Caddy cannot reach PostgreSQL: edge traffic and database traffic use separate container networks.
- Never run `docker compose down --volumes` against production. It deletes the database, statements, Data Protection keys, local backup-test repository, and TLS state.

### Encrypted backups

`deploy/run-backup.sh` briefly stops the API, creates a PostgreSQL custom-format dump, and sends that dump plus the matching statement files and Data Protection key ring to Restic in one encrypted snapshot. A restart trap brings the API back even when backup fails. The backup container receives the sensitive volumes read-only and drops every Linux capability.

Create a manual backup:

```bash
sh deploy/run-backup.sh /opt/billwatch
```

Prove the latest encrypted snapshot can be read and restored:

```bash
docker compose --env-file .env.production --file compose.production.yml --profile operations up --detach --wait restore-database
docker compose --env-file .env.production --file compose.production.yml --profile operations run --rm backup verify
docker compose --env-file .env.production --file compose.production.yml --profile operations stop restore-database
```

Verification selects only a snapshot that completed repository integrity checking, validates SHA-256 manifests, restores into disposable storage, loads the dump into a separate temporary PostgreSQL server, checks EF migration history, and reconciles every database statement record with its restored file and size. It never connects to the live database server for restore work and never overwrites live files.

For a standard `/opt/billwatch` installation, install and enable the supplied daily systemd timer:

```bash
sudo cp deploy/systemd/billwatch-backup.service deploy/systemd/billwatch-backup.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now billwatch-backup.timer
sudo systemctl start billwatch-backup.service
sudo systemctl status billwatch-backup.service
```

The first real-host recovery drill must still be performed before beta invitations. Restore to a separate clean host, keep public traffic disabled, use the matching application release, verify protected Plaid data can be decrypted and statement files can be downloaded, and only then treat the backup gate as closed. Never restore directly over a running production stack.

Keep the Restic password and backend recovery credentials in a separate password vault or recovery escrow, not only in `.env.production` on the server. Configure immutable or append-only retention at the off-host storage provider and retain at least 7 daily, 5 weekly, and 12 monthly recovery points. Use separate backup-write and retention-delete credentials where the provider supports them, so compromise of the application host cannot erase every recovery point.

A restored snapshot represents the state at its recovery timestamp. Before reopening traffic, reconcile account and statement deletions that occurred after that timestamp against an external deletion/audit record so recovery does not unintentionally resurrect data a user asked BillWatch to remove.

Production credentials, `.env.production`, raw statements, extracted statement text, database dumps, and AI evaluation corpora must never be committed.

## External readiness monitoring

The `BillWatch Production Readiness` GitHub Actions workflow probes production from outside the deployment host every 15 minutes. It remains skipped until the repository variable `BILLWATCH_PRODUCTION_URL` is set to the hostname-only HTTPS origin, for example `https://api.billwatch.com`.

The probe rejects credentials, ports, paths, redirects, local/internal hostnames, and DNS results in private, loopback, or link-local address ranges. It performs three bounded HTTPS attempts and accepts only BillWatch's exact readiness response. No application credential or API key is sent.

After the hostname is configured:

1. Set the repository Actions variable `BILLWATCH_PRODUCTION_URL`.
2. Run `BillWatch Production Readiness` manually and confirm it passes.
3. Temporarily stop the API or make readiness fail, run the workflow again, and confirm GitHub records a failed run and the operations account receives its configured Actions notification.
4. Restore the API and confirm the next manual probe passes.

The same probe can be run from any separate monitoring host:

```sh
BILLWATCH_PRODUCTION_URL=https://api.billwatch.com sh deploy/monitor-readiness.sh
```
