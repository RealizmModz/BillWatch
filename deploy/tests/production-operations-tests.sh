#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

valid_env="$temp_dir/valid.env"

write_valid_env()
{
    file=$1
    sed \
        -e 's/api\.example\.com/api.billwatch.test/' \
        -e 's/replace-with-the-deployed-git-commit/0123456789abcdef0123456789abcdef01234567/' \
        -e 's/owner@example\.com/ops@billwatch.test/' \
        -e 's/replace-with-a-long-random-password/database-password-with-more-than-32-characters/' \
        -e 's/replace-with-plaid-client-id/test-plaid-client/' \
        -e 's/replace-with-plaid-secret/test-plaid-secret/' \
        -e 's#s3:https://s3\.example\.com/billwatch-production#s3:https://objects.billwatch.test/production#' \
        -e 's/replace-with-a-separate-long-random-backup-password/restic-password-with-more-than-24-characters/' \
        -e 's/replace-with-backup-only-access-key/test-backup-access-key/' \
        -e 's/replace-with-backup-only-secret-key/test-backup-secret-key/' \
        "$root_dir/.env.production.example" > "$file"
    chmod 600 "$file"
}

expect_failure()
{
    if "$@" >/dev/null 2>&1; then
        printf '%s\n' "Expected command to fail: $*" >&2
        exit 1
    fi
}

write_valid_env "$valid_env"
"$root_dir/deploy/validate-production-env.sh" "$valid_env" >/dev/null

placeholder_env="$temp_dir/placeholder.env"
write_valid_env "$placeholder_env"
sed -i 's/test-plaid-secret/replace-with-plaid-secret/' "$placeholder_env"
expect_failure "$root_dir/deploy/validate-production-env.sh" "$placeholder_env"

weak_env="$temp_dir/weak.env"
write_valid_env "$weak_env"
sed -i 's/database-password-with-more-than-32-characters/short/' "$weak_env"
expect_failure "$root_dir/deploy/validate-production-env.sh" "$weak_env"

local_backup_env="$temp_dir/local-backup.env"
write_valid_env "$local_backup_env"
sed -i 's#s3:https://objects.billwatch.test/production#/srv/backups#' "$local_backup_env"
expect_failure "$root_dir/deploy/validate-production-env.sh" "$local_backup_env"

expect_failure "$root_dir/deploy/monitor-readiness.sh" 'http://api.billwatch.test'
expect_failure "$root_dir/deploy/monitor-readiness.sh" 'https://localhost'
expect_failure "$root_dir/deploy/monitor-readiness.sh" 'https://127.0.0.1'

fake_bin="$temp_dir/bin"
mkdir "$fake_bin"

cat > "$fake_bin/getent" <<'EOF'
#!/bin/sh
printf '%s\n' '93.184.216.34 STREAM api.billwatch.test'
EOF

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
output=
while [ "$#" -gt 0 ]; do
    if [ "$1" = '--output' ]; then
        shift
        output=$1
    fi
    shift
done
printf '%s\n' '{"status":"ready"}' > "$output"
EOF

chmod 755 "$fake_bin/getent" "$fake_bin/curl"
PATH="$fake_bin:$PATH" "$root_dir/deploy/monitor-readiness.sh" 'https://api.billwatch.test' >/dev/null

printf '%s\n' 'Production operation script tests passed.'
