#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Backup trust-boundary test failed: $1" >&2
    exit 1
}

expect_failure()
{
    if "$@" >/dev/null 2>&1; then
        fail "expected command to fail: $*"
    fi
}

backup_script="$root_dir/deploy/backup/backup.sh"
maintenance_runner="$root_dir/deploy/run-backup-maintenance.sh"
sh -n "$backup_script" || fail "backup script has invalid POSIX shell syntax."
sh -n "$maintenance_runner" || fail "maintenance runner has invalid POSIX shell syntax."

create_backup_body=$(awk '/^create_backup\(\)/,/^}/' "$backup_script")
if printf '%s\n' "$create_backup_body" | grep -q 'apply_retention_policy'; then
    fail "normal backup capture must never invoke destructive retention maintenance."
fi
printf '%s\n' "$create_backup_body" | grep -q 'require_append_only_backup_role' ||
    fail "normal backup capture must require the append-only role."

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
restic_log="$temp_dir/restic.log"
cat > "$fake_bin/restic" <<'EOF'
#!/bin/sh
set -eu
printf '%s\n' "$*" >> "$BILLWATCH_TEST_RESTIC_LOG"
case "${1:-} ${2:-}" in
    'cat config') exit 0 ;;
    *) exit 0 ;;
esac
EOF
chmod 700 "$fake_bin/restic"

common_env()
{
    env \
        PATH="$fake_bin:$PATH" \
        BILLWATCH_TEST_RESTIC_LOG="$restic_log" \
        RESTIC_REPOSITORY='rest:https://backup.example.test/billwatch' \
        RESTIC_PASSWORD='restic-password-with-more-than-24-characters' \
        BILLWATCH_BACKUP_RETENTION_ENABLED=true \
        BILLWATCH_BACKUP_KEEP_DAILY=14 \
        BILLWATCH_BACKUP_KEEP_WEEKLY=8 \
        BILLWATCH_BACKUP_KEEP_MONTHLY=12 \
        BILLWATCH_BACKUP_KEEP_YEARLY=3 \
        "$@"
}

: > "$restic_log"
expect_failure common_env BILLWATCH_BACKUP_CLIENT_MODE=append-only sh "$backup_script" retention
[ ! -s "$restic_log" ] || fail "append-only retention refusal must occur before any Restic call."

: > "$restic_log"
expect_failure common_env BILLWATCH_BACKUP_CLIENT_MODE=maintenance BILLWATCH_BACKUP_MAINTENANCE_ALLOW=false sh "$backup_script" retention
[ ! -s "$restic_log" ] || fail "maintenance opt-in refusal must occur before any Restic call."

: > "$restic_log"
common_env BILLWATCH_BACKUP_CLIENT_MODE=maintenance BILLWATCH_BACKUP_MAINTENANCE_ALLOW=true sh "$backup_script" retention >/dev/null
grep -q '^cat config$' "$restic_log" || fail "trusted maintenance did not verify the encrypted repository."
grep -q '^forget --host billwatch-production --tag billwatch-complete --keep-daily 14 --keep-weekly 8 --keep-monthly 12 --keep-yearly 3 --prune$' "$restic_log" ||
    fail "trusted maintenance did not apply the intended completed-snapshot retention policy."
grep -q '^check$' "$restic_log" || fail "trusted maintenance did not verify repository integrity after pruning."

common_env BILLWATCH_BACKUP_CLIENT_MODE=append-only sh "$backup_script" policy >/dev/null ||
    fail "append-only production role must be able to inspect retention policy non-destructively."

maintenance_env="$temp_dir/maintenance.env"
cat > "$maintenance_env" <<'EOF'
BILLWATCH_RELEASE_ID=1111111111111111111111111111111111111111
RESTIC_REPOSITORY=rest:https://backup.example.test/billwatch
RESTIC_PASSWORD=restic-password-with-more-than-24-characters
BILLWATCH_BACKUP_CLIENT_MODE=maintenance
BILLWATCH_BACKUP_MAINTENANCE_ALLOW=true
BILLWATCH_BACKUP_RETENTION_ENABLED=true
BILLWATCH_BACKUP_KEEP_DAILY=14
BILLWATCH_BACKUP_KEEP_WEEKLY=8
BILLWATCH_BACKUP_KEEP_MONTHLY=12
BILLWATCH_BACKUP_KEEP_YEARLY=3
EOF
chmod 600 "$maintenance_env"

docker_log="$temp_dir/docker.log"
cat > "$fake_bin/git" <<'EOF'
#!/bin/sh
set -eu
if [ "${1:-}" = -C ]; then shift 2; fi
case "$*" in
    'rev-parse HEAD') printf '%s\n' '1111111111111111111111111111111111111111' ;;
    'status --porcelain --untracked-files=normal') : ;;
    *) exit 2 ;;
esac
EOF
cat > "$fake_bin/docker" <<'EOF'
#!/bin/sh
set -eu
printf '%s\n' "$*" >> "$BILLWATCH_TEST_DOCKER_LOG"
EOF
chmod 700 "$fake_bin/git" "$fake_bin/docker"

PATH="$fake_bin:$PATH" BILLWATCH_TEST_DOCKER_LOG="$docker_log" sh "$maintenance_runner" "$maintenance_env" >/dev/null
grep -q '^build --build-arg BILLWATCH_RELEASE_ID=1111111111111111111111111111111111111111 --tag billwatch-backup-maintenance:1111111111111111111111111111111111111111 ' "$docker_log" ||
    fail "maintenance runner did not build the exact release backup image."
grep -q '^run --rm --read-only --user 1654:1654 --cap-drop ALL --security-opt no-new-privileges:true ' "$docker_log" ||
    fail "maintenance runner did not preserve the hardened container boundary."
grep -q ' --env-file .* billwatch-backup-maintenance:1111111111111111111111111111111111111111 retention$' "$docker_log" ||
    fail "maintenance runner did not execute only the retention command."
if grep -Eq '(^| )(--volume|-v)( |$)' "$docker_log"; then
    fail "maintenance runner must not mount production host data."
fi

chmod 644 "$maintenance_env"
expect_failure env PATH="$fake_bin:$PATH" BILLWATCH_TEST_DOCKER_LOG="$docker_log" sh "$maintenance_runner" "$maintenance_env"
chmod 600 "$maintenance_env"
sed -i 's/BILLWATCH_BACKUP_CLIENT_MODE=maintenance/BILLWATCH_BACKUP_CLIENT_MODE=append-only/' "$maintenance_env"
expect_failure env PATH="$fake_bin:$PATH" BILLWATCH_TEST_DOCKER_LOG="$docker_log" sh "$maintenance_runner" "$maintenance_env"

printf '%s\n' 'Backup trust-boundary tests passed.'
