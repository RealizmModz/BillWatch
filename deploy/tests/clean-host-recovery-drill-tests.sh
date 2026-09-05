#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
runner="$root_dir/deploy/run-clean-host-recovery-drill.sh"
compose_file="$root_dir/compose.recovery-drill.yml"
temp_dir=$(mktemp -d)
env_file="$root_dir/.env.recovery-test-$$"

after_test()
{
    rm -f "$env_file"
    rm -rf "$temp_dir"
}

trap after_test EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Clean-host recovery drill test failed: $1" >&2
    exit 1
}

[ -f "$runner" ] || fail "recovery drill runner is missing."
[ -f "$compose_file" ] || fail "isolated recovery compose file is missing."
sh -n "$runner" || fail "recovery drill runner has invalid POSIX shell syntax."

if grep -Eq '^[[:space:]]*ports:' "$compose_file"; then
    fail "recovery drill topology must not publish host ports."
fi

for forbidden in postgres_data statement_files data_protection_keys web_data_protection_keys caddy_data
 do
    if grep -Fq "$forbidden" "$compose_file"; then
        fail "recovery drill topology references production state volume: $forbidden"
    fi
 done

if grep -Eq '^[[:space:]]+(api|web|edge|database):[[:space:]]*$' "$compose_file"; then
    fail "recovery drill topology must not define production application services."
fi

grep -Fq 'internal: true' "$compose_file" || fail "recovery drill network must remain isolated from external ingress."
grep -Fq 'BILLWATCH_ALLOW_LOCAL_BACKUP_REPOSITORY: "false"' "$compose_file" || fail "recovery verifier must force local repositories off."
grep -Fq 'run --rm --no-deps verifier verify' "$runner" || fail "runner must invoke the existing cryptographic/database/file verifier."
grep -Fq 'down --volumes --remove-orphans' "$runner" || fail "runner must tear down isolated recovery state."

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
docker_log="$temp_dir/docker.log"
cat > "$fake_bin/docker" <<'EOF'
#!/bin/sh
printf '%s\n' "$*" >> "${BILLWATCH_TEST_DOCKER_LOG:?}"
exit 0
EOF
chmod 755 "$fake_bin/docker"

head_sha=$(git -C "$root_dir" rev-parse HEAD)

write_env()
{
    allow_value=$1
    repository_value=$2
    release_value=$3

    cat > "$env_file" <<EOF
BILLWATCH_RECOVERY_DRILL_ALLOW=$allow_value
BILLWATCH_RELEASE_ID=$release_value
RESTIC_REPOSITORY=$repository_value
RESTIC_PASSWORD=ci-recovery-password-with-32-characters
BILLWATCH_DATABASE_PASSWORD=ci-isolated-restore-password
EOF
    chmod 600 "$env_file"
}

run_runner()
{
    PATH="$fake_bin:$PATH" BILLWATCH_TEST_DOCKER_LOG="$docker_log" sh "$runner" "$env_file"
}

write_env false 's3:https://backup.example.invalid/billwatch' "$head_sha"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill ran without explicit mutation/restore opt-in."
fi

write_env true '/repository' "$head_sha"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a local Restic repository."
fi

write_env true 's3:https://backup.example.invalid/billwatch' '0000000000000000000000000000000000000000'
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a release that does not match the checkout."
fi

write_env true 's3:https://backup.example.invalid/billwatch' "$head_sha"
chmod 644 "$env_file"
if run_runner >/dev/null 2>&1; then
    fail "recovery drill accepted a world-readable recovery environment file."
fi

write_env true 's3:https://backup.example.invalid/billwatch' "$head_sha"
: > "$docker_log"
run_runner >/dev/null || fail "valid isolated recovery drill configuration was rejected."

if grep -Fq 'compose.production.yml' "$docker_log"; then
    fail "recovery drill invoked the production compose topology."
fi

grep -Fq "$compose_file" "$docker_log" || fail "recovery drill did not use the isolated compose topology."
grep -Fq 'up --detach --wait restore-database' "$docker_log" || fail "recovery drill did not start the isolated PostgreSQL restore target."
grep -Fq 'run --rm --no-deps verifier verify' "$docker_log" || fail "recovery drill did not run encrypted snapshot verification."
grep -Fq 'down --volumes --remove-orphans' "$docker_log" || fail "recovery drill did not clean up isolated state."

sh "$root_dir/deploy/tests/private-beta-technical-evidence-tests.sh" || fail "private-beta technical evidence regression suite failed."

printf '%s\n' 'Clean-host recovery drill tests passed.'
