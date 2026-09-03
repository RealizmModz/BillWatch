#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)

trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Production operation test failed: $1" >&2
    exit 1
}

valid_env="$temp_dir/valid.env"

write_valid_env()
{
    file=$1

    sed \
        -e 's/^BILLWATCH_HOST=.*/BILLWATCH_HOST=api.billwatch.test/' \
        -e 's/^BILLWATCH_WEB_HOST=.*/BILLWATCH_WEB_HOST=app.billwatch.test/' \
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

    chmod 600 \
        "$file"
}

expect_failure()
{
    if "$@" >/dev/null 2>&1; then
        printf '%s\n' \
            "Expected command to fail: $*" >&2

        exit 1
    fi
}

write_valid_env "$valid_env"

backup_service="$root_dir/deploy/systemd/billwatch-backup.service"

[ -f "$backup_service" ] ||
    fail "production backup systemd service is missing."

grep -qx \
    'User=deploy' \
    "$backup_service" ||
    fail "production backup systemd service must run as the deploy account."

"$root_dir/deploy/validate-production-env.sh" \
    "$valid_env" >/dev/null

placeholder_env="$temp_dir/placeholder.env"

write_valid_env "$placeholder_env"

sed -i \
    's/test-plaid-secret/replace-with-plaid-secret/' \
    "$placeholder_env"

expect_failure \
    "$root_dir/deploy/validate-production-env.sh" \
    "$placeholder_env"

weak_env="$temp_dir/weak.env"

write_valid_env "$weak_env"

sed -i \
    's/database-password-with-more-than-32-characters/short/' \
    "$weak_env"

expect_failure \
    "$root_dir/deploy/validate-production-env.sh" \
    "$weak_env"

same_host_env="$temp_dir/same-host.env"

write_valid_env "$same_host_env"

sed -i \
    's/app\.billwatch\.test/api.billwatch.test/' \
    "$same_host_env"

expect_failure \
    "$root_dir/deploy/validate-production-env.sh" \
    "$same_host_env"

invalid_plaid_env="$temp_dir/invalid-plaid.env"

write_valid_env "$invalid_plaid_env"

sed -i \
    's/PLAID_ENVIRONMENT=sandbox/PLAID_ENVIRONMENT=development/' \
    "$invalid_plaid_env"

expect_failure \
    "$root_dir/deploy/validate-production-env.sh" \
    "$invalid_plaid_env"

local_backup_env="$temp_dir/local-backup.env"

write_valid_env "$local_backup_env"

sed -i \
    's#s3:https://objects.billwatch.test/production#/srv/backups#' \
    "$local_backup_env"

expect_failure \
    "$root_dir/deploy/validate-production-env.sh" \
    "$local_backup_env"

expect_failure \
    "$root_dir/deploy/monitor-readiness.sh" \
    'http://api.billwatch.test'

expect_failure \
    "$root_dir/deploy/monitor-readiness.sh" \
    'https://localhost'

expect_failure \
    "$root_dir/deploy/monitor-readiness.sh" \
    'https://127.0.0.1'

fake_bin="$temp_dir/bin"

mkdir "$fake_bin"

cat > "$fake_bin/getent" <<'SCRIPT'
#!/bin/sh
printf '%s\n' "93.184.216.34 STREAM $2"
SCRIPT

cat > "$fake_bin/curl" <<'SCRIPT'
#!/bin/sh

output=

while [ "$#" -gt 0 ]; do
    if [ "$1" = '--output' ]; then
        shift
        output=$1
    fi

    shift
done

if [ -n "${BILLWATCH_TEST_CURL_COUNT_FILE:-}" ]; then
    count=0

    if [ -f "$BILLWATCH_TEST_CURL_COUNT_FILE" ]; then
        count=$(cat "$BILLWATCH_TEST_CURL_COUNT_FILE")
    fi

    count=$((count + 1))

    printf '%s\n' \
        "$count" > "$BILLWATCH_TEST_CURL_COUNT_FILE"

    if [ "$count" -lt "${BILLWATCH_TEST_CURL_SUCCEED_ON:-1}" ]; then
        exit 35
    fi
fi

printf '%s\n' \
    '{"status":"ready"}' > "$output"
SCRIPT

cat > "$fake_bin/sleep" <<'SCRIPT'
#!/bin/sh
printf '%s\n' "$1" >> "$BILLWATCH_TEST_SLEEP_LOG"
SCRIPT

chmod 755 \
    "$fake_bin/getent" \
    "$fake_bin/curl" \
    "$fake_bin/sleep"

PATH="$fake_bin:$PATH" \
    "$root_dir/deploy/monitor-readiness.sh" \
    'https://api.billwatch.test' >/dev/null

PATH="$fake_bin:$PATH" \
    "$root_dir/deploy/monitor-readiness.sh" \
    'https://app.billwatch.test' >/dev/null

curl_count_file="$temp_dir/readiness-curl-count"
sleep_log="$temp_dir/readiness-sleeps.log"

PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_CURL_COUNT_FILE="$curl_count_file" \
    BILLWATCH_TEST_CURL_SUCCEED_ON=4 \
    BILLWATCH_TEST_SLEEP_LOG="$sleep_log" \
    "$root_dir/deploy/monitor-readiness.sh" \
    'https://api.billwatch.test' >/dev/null 2>&1

[ "$(cat "$curl_count_file")" = 4 ] ||
    fail "readiness monitor did not retry until the endpoint recovered."

[ "$(wc -l < "$sleep_log" | tr -d ' ')" = 3 ] ||
    fail "readiness monitor did not back off between failed attempts."

if grep -vx '5' "$sleep_log" >/dev/null; then
    fail "readiness monitor used an unexpected retry delay."
fi

deployment_root="$temp_dir/deployment"

mkdir -p \
    "$deployment_root/deploy"

cp \
    "$root_dir/compose.production.yml" \
    "$deployment_root/compose.production.yml"

cp \
    "$root_dir/.env.production.example" \
    "$deployment_root/.env.production.example"

cp \
    "$root_dir/deploy/validate-production-env.sh" \
    "$deployment_root/deploy/validate-production-env.sh"

cp \
    "$root_dir/deploy/monitor-readiness.sh" \
    "$deployment_root/deploy/monitor-readiness.sh"

cp \
    "$root_dir/deploy/run-backup.sh" \
    "$deployment_root/deploy/run-backup.sh"

cp \
    "$root_dir/deploy/deploy-production.sh" \
    "$deployment_root/deploy/deploy-production.sh"

: > "$deployment_root/Dockerfile"
: > "$deployment_root/Dockerfile.web"

chmod 755 \
    "$deployment_root/deploy/"*.sh

write_valid_env \
    "$deployment_root/.env.production"

command_log="$temp_dir/deployment-commands.log"
readiness_log="$temp_dir/deployment-readiness.log"

cat > "$fake_bin/git" <<'SCRIPT'
#!/bin/sh

case "$*" in
    *'rev-parse HEAD'*)
        printf '%s\n' \
            '0123456789abcdef0123456789abcdef01234567'
        ;;

    *'status --porcelain --untracked-files=normal'*)
        :
        ;;

    *)
        exit 1
        ;;
esac
SCRIPT

cat > "$fake_bin/docker" <<'SCRIPT'
#!/bin/sh

printf '%s\n' \
    "$*" >> "$BILLWATCH_TEST_COMMAND_LOG"

case "$*" in
    *'ps --status running --services'*)
        exit 0
        ;;

    *'up --detach --wait --wait-timeout 240 --no-build database api web edge'*)
        [ "${BILLWATCH_TEST_FAIL_UP:-false}" != true ] ||
            exit 1
        ;;
esac
SCRIPT

cat > "$deployment_root/deploy/monitor-readiness.sh" <<'SCRIPT'
#!/bin/sh
set -eu
printf '%s\n' "$1" >> "$BILLWATCH_TEST_READINESS_LOG"
SCRIPT

cat > "$deployment_root/deploy/run-backup.sh" <<'SCRIPT'
#!/bin/sh
set -eu
exit 0
SCRIPT

chmod 755 \
    "$fake_bin/git" \
    "$fake_bin/docker" \
    "$deployment_root/deploy/monitor-readiness.sh" \
    "$deployment_root/deploy/run-backup.sh"

PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_COMMAND_LOG="$command_log" \
    BILLWATCH_TEST_READINESS_LOG="$readiness_log" \
    "$deployment_root/deploy/deploy-production.sh" \
    "$deployment_root/.env.production" >/dev/null

[ "$(cat "$deployment_root/.billwatch-release")" = '0123456789abcdef0123456789abcdef01234567' ] ||
    fail "deployment did not record the verified release."

[ ! -d "$deployment_root/.billwatch-deploy.lock" ] ||
    fail "deployment lock was not removed after success."

grep -q \
    'config --quiet' \
    "$command_log" ||
    fail "deployment did not validate Compose configuration."

grep -q \
    -- '--profile operations build api web backup' \
    "$command_log" ||
    fail "deployment did not build API, web, and backup release images."

grep -q \
    'up --detach --wait --wait-timeout 240 --no-build database api web edge' \
    "$command_log" ||
    fail "deployment did not wait for the full production service set."

grep -qx \
    'https://api.billwatch.test' \
    "$readiness_log" ||
    fail "deployment did not verify API readiness."

grep -qx \
    'https://app.billwatch.test' \
    "$readiness_log" ||
    fail "deployment did not verify web readiness."

rm -f \
    "$deployment_root/.billwatch-release"

if PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_COMMAND_LOG="$command_log" \
    BILLWATCH_TEST_READINESS_LOG="$readiness_log" \
    BILLWATCH_TEST_FAIL_UP=true \
    "$deployment_root/deploy/deploy-production.sh" \
    "$deployment_root/.env.production" >/dev/null 2>&1; then

    fail "deployment unexpectedly succeeded after the production stack failed to start."
fi

[ ! -e "$deployment_root/.billwatch-release" ] ||
    fail "failed deployment recorded a successful release."

[ ! -d "$deployment_root/.billwatch-deploy.lock" ] ||
    fail "deployment lock was not removed after failure."

printf '%s\n' \
    'Production operation script tests passed.'
