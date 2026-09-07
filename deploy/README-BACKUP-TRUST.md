# BillWatch backup trust separation

BillWatch separates routine production backup capture from destructive repository maintenance.

## Production backup role

Production `.env.production` must use:

```text
BILLWATCH_BACKUP_CLIENT_MODE=append-only
```

The backup command accepts only that role and never runs `restic forget` or `restic prune`, even when a retention policy is enabled. A successful backup may report that retention maintenance is due, but deletion is a separate operation.

The production storage credential should independently be restricted by the storage provider so it cannot delete or overwrite protected backup objects. The application-level role is defense in depth; it is not proof that the provider actually enforced immutability.

## Trusted maintenance role

Retention requires a separate trusted host and a protected mode-600 environment file outside the BillWatch checkout. That file must contain the encrypted repository credentials plus:

```text
BILLWATCH_RELEASE_ID=<exact-release-sha>
BILLWATCH_BACKUP_CLIENT_MODE=maintenance
BILLWATCH_BACKUP_MAINTENANCE_ALLOW=true
BILLWATCH_BACKUP_RETENTION_ENABLED=true
BILLWATCH_BACKUP_KEEP_DAILY=14
BILLWATCH_BACKUP_KEEP_WEEKLY=8
BILLWATCH_BACKUP_KEEP_MONTHLY=12
BILLWATCH_BACKUP_KEEP_YEARLY=3
```

Run from a clean checkout of the exact release:

```sh
sh deploy/run-backup-maintenance.sh /secure/path/billwatch-backup-maintenance.env
```

The runner refuses a symlink, anything other than mode 600/current-user ownership, any maintenance env stored inside the repository checkout, a dirty or release-mismatched checkout, a local Restic backend, non-maintenance client mode, missing explicit maintenance opt-in, or disabled retention. It builds the backup tool from the exact release and runs only the retention command in a read-only, capability-dropped container with no production data volumes mounted.

Never place the delete-capable maintenance environment or credentials on the production VPS. Normal production backup/recovery credentials and maintenance credentials should be different provider principals when the backend supports separate permissions.

## Provider immutability

This trust split fixes BillWatch's own unsafe coupling between routine backup capture and delete-capable retention. It does **not** by itself close the provider-immutability launch gate.

Before trusted external beta, configure and test provider-side Object Lock/WORM/append-only protection or an equivalent design. Recovery must be proven from the protected storage path. Restic maintenance for append-only repositories must be performed from a separately secured administrative client; do not grant routine production backup capture unrestricted delete authority.
