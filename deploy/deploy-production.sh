#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
env_file=${1:-"$root_dir/.env.production"}
lock_dir="$root_dir/.billwatch-deploy.lock"
release_file="$root_dir/.billwatch-release"
release_temp=
deployment_started=false

fail()
{
    printf '%s\n' "Production deployment failed: $1" >&2
    exit 1
}

cleanup()
{
    status=$?
    trap - EXIT HUP INT TERM
    if [ -n "$release_temp" ]; then
        rm -f "$release_temp"
    fi
    rmdir "$lock_dir" 2>/dev/null || true

    if [ "$status" -ne 0 ] && [ "$deployment_started" = true ]; then
        printf '%s\n' \
            "The release marker was not changed. Inspect sanitized service logs before retrying:" \
            "docker compose --env-file .env.production --file compose.production.yml logs --no-color --tail 200 api edge database" >&2
    fi

    exit "$status"
}

trap cleanup EXIT
trap 'exit 130' HUP INT TERM

[ -f "$root_dir/compose.production.yml" ] || fail "compose.production.yml is missing."
[ -x "$root_dir/deploy/validate-production-env.sh" ] || fail "production preflight is not executable."
[ -x "$root_dir/deploy/monitor-readiness.sh" ] || fail "readiness monitor is not executable."
[ -x "$root_dir/deploy/run-backup.sh" ] || fail "backup wrapper is not executable."

[ -f "$env_file" ] || fail "the production environment file is missing."
env_dir=$(CDPATH= cd -- "$(dirname -- "$env_file")" && pwd)
env_file="$env_dir/$(basename -- "$env_file")"
[ "$env_file" = "$root_dir/.env.production" ] ||
    fail "deployment requires the host-local .env.production file in the repository root."

"$root_dir/deploy/validate-production-env.sh" "$env_file"

release_id=$(awk -F= '$1 == "BILLWATCH_RELEASE_ID" { print substr($0, length($1) + 2); exit }' "$env_file")
host=$(awk -F= '$1 == "BILLWATCH_HOST" { print substr($0, length($1) + 2); exit }' "$env_file")

command -v git >/dev/null 2>&1 || fail "git is required."
command -v docker >/dev/null 2>&1 || fail "Docker is required."

current_release=$(git -C "$root_dir" rev-parse HEAD 2>/dev/null) || fail "the deployment directory is not a Git checkout."
[ "$current_release" = "$release_id" ] || fail "BILLWATCH_RELEASE_ID does not match the checked-out commit."
[ -z "$(git -C "$root_dir" status --porcelain --untracked-files=normal)" ] || fail "the deployment checkout has uncommitted files."

if ! mkdir "$lock_dir" 2>/dev/null; then
    fail "another deployment is active or a stale deployment lock requires operator review."
fi

compose()
{
    docker compose --env-file "$env_file" --file "$root_dir/compose.production.yml" "$@"
}

compose config --quiet
compose --profile operations build api backup

if compose ps --status running --services | grep -qx api; then
    printf '%s\n' "Creating a verified encrypted recovery point before replacing the running API."
    "$root_dir/deploy/run-backup.sh" "$root_dir"
fi

deployment_started=true
compose up --detach --wait --wait-timeout 180 --no-build database api edge

BILLWATCH_PRODUCTION_URL="https://$host" "$root_dir/deploy/monitor-readiness.sh"

release_temp="$release_file.tmp.$$"
printf '%s\n' "$release_id" > "$release_temp"
chmod 600 "$release_temp"
mv -f "$release_temp" "$release_file"

printf '%s\n' "Production deployment completed and verified at release $release_id."
