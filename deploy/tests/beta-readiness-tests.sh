#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)

trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Beta readiness test failed: $1" >&2
    exit 1
}

scripts="
$root_dir/deploy/backup/backup.sh
$root_dir/deploy/bootstrap-owner.sh
$root_dir/deploy/check-backup-policy.sh
$root_dir/deploy/check-backup-snapshot.sh
$root_dir/deploy/check-backup-timer.sh
$root_dir/deploy/check-operations-alerting.sh
$root_dir/deploy/send-operations-alert.sh
$root_dir/deploy/smoke-admin-api.sh
$root_dir/deploy/smoke-authenticated-api.sh
$root_dir/deploy/smoke-private-beta.sh
$root_dir/deploy/smoke-access-key-lifecycle.sh
$root_dir/deploy/smoke-web-bff.sh
$root_dir/deploy/smoke-plaid-lifecycle.sh
$root_dir/deploy/verify-beta-admin.sh
$root_dir/deploy/verify-beta-readiness.sh
$root_dir/deploy/verify-identity-role-schema.sh
$root_dir/deploy/verify-owner-count.sh
$root_dir/deploy/verify-production-exposure.sh
$root_dir/deploy/verify-production-permissions.sh
$root_dir/deploy/verify-release-integrity.sh
$root_dir/deploy/verify-production-runtime.sh
$root_dir/deploy/verify-production.sh
$root_dir/deploy/verify-subscription-safety.sh
"

for script in $scripts
do
    [ -f "$script" ] || fail "required script is missing: $script"
    sh -n "$script" || fail "POSIX shell syntax validation failed: $script"
done

grep -Fq 'verify-release-integrity.sh' "$root_dir/deploy/verify-production.sh" ||
    fail "production verification must enforce release integrity before declaring the host healthy."

for smoke_script in "$root_dir/deploy/smoke-authenticated-api.sh" "$root_dir/deploy/smoke-admin-api.sh"
do
    grep -q 'stty -echo' "$smoke_script" || fail "smoke test must disable terminal echo for password input: $smoke_script"
    grep -q 'chmod 700 "$work_directory"' "$smoke_script" || fail "smoke test must protect its temporary directory: $smoke_script"
    grep -q 'chmod 600 "$auth_config"' "$smoke_script" || fail "smoke test must protect bearer-token curl configuration: $smoke_script"
    if grep -F -- '--header "Authorization: Bearer $access_token"' "$smoke_script" >/dev/null; then
        fail "smoke test must not place the bearer token directly in curl argv: $smoke_script"
    fi
done

sh "$root_dir/deploy/tests/private-beta-smoke-tests.sh" || fail "private-beta smoke harness regression suite failed."
sh "$root_dir/deploy/tests/access-key-smoke-tests.sh" || fail "access-key lifecycle smoke harness regression suite failed."
sh "$root_dir/deploy/tests/web-bff-smoke-tests.sh" || fail "authenticated Web/BFF smoke harness regression suite failed."
sh "$root_dir/deploy/tests/plaid-lifecycle-smoke-tests.sh" || fail "guarded Plaid lifecycle smoke harness regression suite failed."

grep -q 'snapshot) list_completed_snapshot ;;' "$root_dir/deploy/backup/backup.sh" || fail "backup entrypoint must expose the constrained completed-snapshot query."
grep -q 'retention) apply_retention_policy ;;' "$root_dir/deploy/backup/backup.sh" || fail "backup entrypoint must expose guarded retention application."
grep -q 'policy) print_retention_policy ;;' "$root_dir/deploy/backup/backup.sh" || fail "backup entrypoint must expose non-destructive retention policy verification."
grep -q -- '--keep-daily "$retention_keep_daily"' "$root_dir/deploy/backup/backup.sh" || fail "backup retention must preserve an explicit daily window."
grep -q -- '--prune' "$root_dir/deploy/backup/backup.sh" || fail "enabled backup retention must prune unreachable repository data."
grep -q '        snapshot' "$root_dir/deploy/check-backup-snapshot.sh" || fail "backup snapshot checker must use the constrained backup snapshot command."
grep -q '        policy' "$root_dir/deploy/check-backup-policy.sh" || fail "backup policy checker must use the non-destructive policy command."

retention_env="
RESTIC_REPOSITORY=/repository
RESTIC_PASSWORD=ci-restic-password-with-32-characters
PGPASSWORD=ci-database-password
BILLWATCH_RELEASE_ID=ci
BILLWATCH_ALLOW_LOCAL_BACKUP_REPOSITORY=true
BILLWATCH_BACKUP_RETENTION_ENABLED=true
BILLWATCH_BACKUP_KEEP_DAILY=14
BILLWATCH_BACKUP_KEEP_WEEKLY=8
BILLWATCH_BACKUP_KEEP_MONTHLY=12
BILLWATCH_BACKUP_KEEP_YEARLY=3
"

if ! env $retention_env sh "$root_dir/deploy/backup/backup.sh" policy >/dev/null; then
    fail "safe minimum backup retention policy was rejected."
fi

if env $retention_env BILLWATCH_BACKUP_KEEP_DAILY=13 sh "$root_dir/deploy/backup/backup.sh" policy >/dev/null 2>&1; then
    fail "backup retention accepted a daily window below the safety floor."
fi

grep -Fq 'OnFailure=billwatch-operations-alert@%n.service' "$root_dir/deploy/systemd/billwatch-backup.service" || fail "backup service must route failures to the operations alert unit."
[ -f "$root_dir/deploy/systemd/billwatch-operations-alert@.service" ] || fail "operations alert systemd template is missing."
grep -Fq 'BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL' "$root_dir/deploy/send-operations-alert.sh" || fail "operations alert sender must read the protected webhook configuration."
grep -Fq 'chmod 600 "$curl_config"' "$root_dir/deploy/send-operations-alert.sh" || fail "operations alert sender must protect its temporary curl configuration."
grep -Fq -- '--config "$curl_config"' "$root_dir/deploy/send-operations-alert.sh" || fail "operations alert sender must keep the private webhook URL out of curl argv."
if grep -F -- 'curl "$webhook_url"' "$root_dir/deploy/send-operations-alert.sh" >/dev/null; then fail "operations alert sender must not expose the private webhook URL in process arguments."; fi
if grep -Eq 'docker compose .*logs|journalctl' "$root_dir/deploy/send-operations-alert.sh"; then fail "operations alert sender must not attach service logs to external alerts."; fi

grep -q 'current_user_count <> 1' "$root_dir/deploy/bootstrap-owner.sh" || fail "Owner bootstrap must re-check the single-user invariant inside its transaction."
grep -q 'current_owner_count <> 0' "$root_dir/deploy/bootstrap-owner.sh" || fail "Owner bootstrap must fail after an Owner already exists."
grep -q 'current_owner_role_count <> 1' "$root_dir/deploy/bootstrap-owner.sh" || fail "Owner bootstrap must require exactly one seeded Owner role."

subscription_root="$temp_dir/subscription"
mkdir -p "$subscription_root"
: > "$subscription_root/compose.production.yml"
printf '%s\n' 'BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false' > "$subscription_root/.env.production"
sh "$root_dir/deploy/verify-subscription-safety.sh" "$subscription_root" >/dev/null
printf '%s\n' 'BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=true' > "$subscription_root/.env.production"
if sh "$root_dir/deploy/verify-subscription-safety.sh" "$subscription_root" >/dev/null 2>&1; then
    fail "subscription safety verifier accepted enabled enforcement."
fi

printf '%s\n' 'Beta readiness operation tests passed.'
