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
- API, Web, and PostgreSQL are not publicly published by Docker;
- Caddy is the only public edge and publishes 80/443;
- database, API, Web, and edge containers are running;
- database, API, and Web health checks are healthy;
- `.billwatch-release` matches the checked-out Git commit;
- public API and Web readiness checks succeed.

## First Owner

See `deploy/README-FIRST-OWNER.md`.

The guarded bootstrap is intentionally unavailable after the first Owner exists.

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

Keep `BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false` until the protected and exempt endpoint matrix has been production-smoke-tested.

Administrative staff roles do not grant access to another user's financial evidence.
