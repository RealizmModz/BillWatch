# BillWatch controlled reboot recovery drill

This drill proves that a production BillWatch host can survive a real VPS reboot without silently changing the deployed release or losing the services required for private-beta operation.

The harness deliberately **does not reboot the host itself**. Rebooting production is an operator-controlled infrastructure action and must never be triggered by CI, a scheduled development run, or an unattended verification script.

## What the drill proves

The preflight phase requires the deployment checkout HEAD to exactly match `.billwatch-release`, requires Docker to be enabled and active, and runs the complete `deploy/verify-beta-readiness.sh` host prerequisite gate. It then records only non-secret evidence in a mode-600 state file outside the deployment checkout:

- exact release SHA;
- deployment directory;
- Linux boot ID;
- UTC observation time.

After the operator performs the controlled reboot, postflight refuses to pass unless:

- the Linux boot ID is different, proving a distinct host boot actually occurred;
- `.billwatch-release` is unchanged;
- the checkout HEAD still equals that exact release;
- Docker is enabled and active again;
- the complete beta-readiness host prerequisite gate passes after reboot.

The state file is removed only after a successful postflight. A failed postflight preserves it for diagnosis.

## Run the drill

Use the deployed checkout, normally `/opt/billwatch`. The default state file is `/var/lib/billwatch/reboot-drill.state`; it is intentionally outside the Git checkout. A different absolute path can be supplied with `BILLWATCH_REBOOT_DRILL_STATE_FILE`.

Before reboot:

```sh
sudo BILLWATCH_REBOOT_DRILL_ALLOW=true \
  sh /opt/billwatch/deploy/run-controlled-reboot-drill.sh \
  preflight /opt/billwatch
```

Only after preflight succeeds, perform the VPS reboot through the normal operator/provider control path. Do not deploy another commit, edit `.billwatch-release`, or replace the checkout between phases.

After the host returns:

```sh
sudo BILLWATCH_REBOOT_DRILL_ALLOW=true \
  sh /opt/billwatch/deploy/run-controlled-reboot-drill.sh \
  postflight /opt/billwatch
```

A green postflight is the evidence for the private-beta controlled reboot/recovery gate. Record the release SHA and observation time in the private operator record; do not record credentials, provider tokens, statement data, database contents, or private webhook URLs.

## Failure handling

Do not delete the state file merely to make a failed postflight disappear. A failure means the reboot gate is unresolved. Diagnose the failed prerequisite first. If the release marker or checkout changed unexpectedly, treat that as a release-integrity incident rather than bypassing the comparison.

Starting a new preflight while a prior state file exists is rejected. Intentionally remove stale state only after the prior drill has been investigated and formally abandoned.
