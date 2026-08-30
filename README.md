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

- An x86-64 Linux server with Docker Engine and the Docker Compose plugin.
- Ports 80 and 443 open to the internet.
- A DNS record for the API hostname pointing to the server.
- Plaid credentials. Use `sandbox` until production access has been approved and verified.
- Encrypted off-host backups or provider-managed volume snapshots.

Deploy:

1. Copy `.env.production.example` to `.env.production` on the server.
2. Replace every placeholder and restrict the file so only the deployment account can read it.
3. Keep all AI flags disabled. No OpenAI key is required for the current runtime.
4. Start the stack:

```bash
docker compose --env-file .env.production --file compose.production.yml up --detach --build
```

5. Confirm both health endpoints over the public HTTPS hostname.
6. Build the MAUI release with the exact deployed origin:

```powershell
dotnet build BillWatch.csproj --configuration Release -p:BillWatchApiBaseUrl=https://api.example.com/
```

The API applies EF Core migrations during startup in this single-instance deployment. Do not scale the API above one instance while startup migration is enabled; a multi-instance platform should run migrations as a separate one-time release job.

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

## Operations and recovery

- `/health/live` proves the process is running.
- `/health/ready` proves the database is reachable, migrations are current, and both sensitive persistent directories are writable. It never returns connection strings or physical paths.
- Monitor both endpoints externally and alert on repeated readiness failure.
- Snapshot or export the PostgreSQL, statement, Data Protection, and Caddy volumes off-host. Database data without the matching Data Protection key volume cannot decrypt protected Plaid credentials.
- Test restore procedures before inviting beta users.

Production credentials, `.env.production`, raw statements, extracted statement text, database dumps, and AI evaluation corpora must never be committed.
