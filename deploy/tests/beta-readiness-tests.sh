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
$root_dir/deploy/check-backup-snapshot.sh
$root_dir/deploy/check-backup-timer.sh
$root_dir/deploy/smoke-admin-api.sh
$root_dir/deploy/smoke-authenticated-api.sh
$root_dir/deploy/verify-beta-admin.sh
$root_dir/deploy/verify-beta-readiness.sh
$root_dir/deploy/verify-identity-role-schema.sh
$root_dir/deploy/verify-owner-count.sh
$root_dir/deploy/verify-production-exposure.sh
$root_dir/deploy/verify-production-permissions.sh
$root_dir/deploy/verify-production-runtime.sh
$root_dir/deploy/verify-production.sh
$root_dir/deploy/verify-subscription-safety.sh
"

for script in $scripts
do
    [ -f "$script" ] ||
        fail "required script is missing: $script"

    sh -n "$script" ||
        fail "POSIX shell syntax validation failed: $script"
done

for smoke_script in \
    "$root_dir/deploy/smoke-authenticated-api.sh" \
    "$root_dir/deploy/smoke-admin-api.sh"
do
    grep -q \
        'stty -echo' \
        "$smoke_script" ||
        fail "smoke test must disable terminal echo for password input: $smoke_script"

    grep -q \
        'chmod 700 "$work_directory"' \
        "$smoke_script" ||
        fail "smoke test must protect its temporary directory: $smoke_script"

    grep -q \
        'chmod 600 "$auth_config"' \
        "$smoke_script" ||
        fail "smoke test must protect bearer-token curl configuration: $smoke_script"

    if grep -F \
        -- '--header "Authorization: Bearer $access_token"' \
        "$smoke_script" >/dev/null; then
        fail "smoke test must not place the bearer token directly in curl argv: $smoke_script"
    fi
done

grep -q \
    'snapshot) list_completed_snapshot ;;' \
    "$root_dir/deploy/backup/backup.sh" ||
    fail "backup entrypoint must expose the constrained completed-snapshot query."

grep -q \
    '        snapshot' \
    "$root_dir/deploy/check-backup-snapshot.sh" ||
    fail "backup snapshot checker must use the constrained backup snapshot command."

grep -q \
    'current_user_count <> 1' \
    "$root_dir/deploy/bootstrap-owner.sh" ||
    fail "Owner bootstrap must re-check the single-user invariant inside its transaction."

grep -q \
    'current_owner_count <> 0' \
    "$root_dir/deploy/bootstrap-owner.sh" ||
    fail "Owner bootstrap must fail after an Owner already exists."

grep -q \
    'current_owner_role_count <> 1' \
    "$root_dir/deploy/bootstrap-owner.sh" ||
    fail "Owner bootstrap must require exactly one seeded Owner role."

subscription_root="$temp_dir/subscription"
mkdir -p "$subscription_root"
: > "$subscription_root/compose.production.yml"

printf '%s\n' \
    'BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false' \
    > "$subscription_root/.env.production"

sh "$root_dir/deploy/verify-subscription-safety.sh" \
    "$subscription_root" >/dev/null

printf '%s\n' \
    'BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=true' \
    > "$subscription_root/.env.production"

if sh "$root_dir/deploy/verify-subscription-safety.sh" \
    "$subscription_root" >/dev/null 2>&1; then
    fail "subscription safety verifier accepted enabled enforcement."
fi

printf '%s\n' \
    'Beta readiness operation tests passed.'
