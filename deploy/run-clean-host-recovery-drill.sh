#!/bin/sh

set -eu

umask 077

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
env_file=${1:-"$root_dir/.env.recovery"}
compose_file="$root_dir/compose.recovery-drill.yml"
project_name="billwatch-recovery-drill-$$"
started=false

fail()
{
    printf '%s\n' "Recovery drill refused: $1" >&2
    exit 64
}

read_required_env_value()
{
    key=$1
    file=$2

    value=$(awk -v key="$key" '
        BEGIN { count = 0 }
        index($0, key "=") == 1 {
            count++
            value = substr($0, length(key) + 2)
            sub(/\r$/, "", value)
        }
        END {
            if (count != 1 || value == "") exit 65
            print value
        }
    ' "$file") || fail "$key must appear exactly once with a non-empty value in the protected recovery environment file."

    printf '%s' "$value"
}

cleanup()
{
    exit_code=$?
    trap - EXIT HUP INT TERM

    if [ "$started" = true ]; then
        docker compose \
            --project-name "$project_name" \
            --env-file "$env_file" \
            --file "$compose_file" \
            down --volumes --remove-orphans >/dev/null 2>&1 || true
    fi

    exit "$exit_code"
}

trap cleanup EXIT
trap 'exit 130' HUP INT TERM

[ -f "$compose_file" ] || fail "the isolated recovery compose file is missing."
[ -f "$env_file" ] || fail "the protected recovery environment file is missing: $env_file"
[ ! -L "$env_file" ] || fail "the recovery environment file must not be a symbolic link."

mode=$(stat -c '%a' "$env_file")
[ "$mode" = 600 ] || fail "the recovery environment file must have mode 600."

owner_uid=$(stat -c '%u' "$env_file")
[ "$owner_uid" = "$(id -u)" ] || fail "the recovery environment file must be owned by the current deployment operator."

case "$env_file" in
    "$root_dir"/*)
        relative_env=${env_file#"$root_dir"/}
        git -C "$root_dir" check-ignore -q -- "$relative_env" || fail "a repository-local recovery environment file must be Git-ignored."
        if git -C "$root_dir" ls-files --error-unmatch -- "$relative_env" >/dev/null 2>&1; then
            fail "the recovery environment file must never be tracked by Git."
        fi
        ;;
esac

[ -z "$(git -C "$root_dir" status --porcelain --untracked-files=normal)" ] || fail "the recovery drill requires a clean Git checkout."

release_id=$(read_required_env_value BILLWATCH_RELEASE_ID "$env_file")
allow_drill=$(read_required_env_value BILLWATCH_RECOVERY_DRILL_ALLOW "$env_file")
repository=$(read_required_env_value RESTIC_REPOSITORY "$env_file")

[ "$allow_drill" = true ] || fail "set BILLWATCH_RECOVERY_DRILL_ALLOW=true explicitly before running a clean-host recovery drill."

case "$release_id" in
    [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]) ;;
    *) fail "BILLWATCH_RELEASE_ID must be an exact lowercase 40-character Git commit SHA." ;;
esac

head_sha=$(git -C "$root_dir" rev-parse HEAD)
[ "$head_sha" = "$release_id" ] || fail "BILLWATCH_RELEASE_ID must match the clean-host checkout exactly."

case "$repository" in
    s3:*|b2:*|azure:*|gs:*|rclone:*|rest:*|sftp:*|swift:*) ;;
    *) fail "the recovery drill requires an explicitly off-host Restic repository; local repository paths are refused." ;;
esac

read_required_env_value RESTIC_PASSWORD "$env_file" >/dev/null
read_required_env_value BILLWATCH_DATABASE_PASSWORD "$env_file" >/dev/null

command -v docker >/dev/null 2>&1 || fail "Docker is required on the clean recovery host."
docker compose version >/dev/null 2>&1 || fail "Docker Compose v2 is required on the clean recovery host."

started=true

docker compose \
    --project-name "$project_name" \
    --env-file "$env_file" \
    --file "$compose_file" \
    build verifier

docker compose \
    --project-name "$project_name" \
    --env-file "$env_file" \
    --file "$compose_file" \
    up --detach --wait restore-database

docker compose \
    --project-name "$project_name" \
    --env-file "$env_file" \
    --file "$compose_file" \
    run --rm --no-deps verifier verify

echo "Clean-host encrypted recovery drill passed for release $release_id."
