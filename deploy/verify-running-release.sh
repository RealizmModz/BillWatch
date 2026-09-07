#!/bin/sh

set -eu

deployment_directory=${1:-}
environment_file=${2:-}

fail()
{
    printf '%s\n' "Running production release invalid: $1" >&2
    exit "${2:-1}"
}

[ -n "$deployment_directory" ] ||
    fail "a BillWatch deployment directory is required." 64

[ -d "$deployment_directory" ] ||
    fail "the deployment directory does not exist." 66

deployment_directory=$(cd "$deployment_directory" && pwd -P)

if [ -z "$environment_file" ]; then
    environment_file="$deployment_directory/.env.production"
fi

[ -f "$environment_file" ] ||
    fail "the production environment file does not exist." 66

environment_file=$(cd "$(dirname -- "$environment_file")" && pwd -P)/$(basename -- "$environment_file")
release_file="$deployment_directory/.billwatch-release"

[ -f "$release_file" ] ||
    fail ".billwatch-release is missing while BillWatch is already running." 78

[ ! -L "$release_file" ] ||
    fail ".billwatch-release must not be a symbolic link." 77

release_owner=$(stat -c '%u' "$release_file") ||
    fail ".billwatch-release ownership cannot be read." 77

release_permissions=$(stat -c '%a' "$release_file") ||
    fail ".billwatch-release permissions cannot be read." 77

[ "$release_owner" -eq "$(id -u)" ] ||
    fail ".billwatch-release must be owned by the deployment account." 77

[ "$release_permissions" = 600 ] ||
    fail ".billwatch-release must have mode 600." 77

[ "$(wc -l < "$release_file" | tr -d ' ')" -eq 1 ] ||
    fail ".billwatch-release must contain exactly one line." 65

recorded_release=$(cat "$release_file")

case "$recorded_release" in
    *[!0-9a-f]*|'')
        fail ".billwatch-release must contain one lowercase 40-character Git commit." 65
        ;;
esac

[ "${#recorded_release}" -eq 40 ] ||
    fail ".billwatch-release must contain one lowercase 40-character Git commit." 65

command -v docker >/dev/null 2>&1 ||
    fail "Docker is required." 69

compose()
{
    docker compose \
        --env-file "$environment_file" \
        --file "$deployment_directory/compose.production.yml" \
        "$@"
}

for service in api web
do
    container_id=$(compose ps -q "$service") ||
        fail "the running $service container could not be resolved." 78

    [ -n "$container_id" ] ||
        fail "the $service service is expected to be running but has no container." 78

    running=$(docker inspect --format '{{.State.Running}}' "$container_id" 2>/dev/null) ||
        fail "the $service container state could not be inspected." 78

    [ "$running" = true ] ||
        fail "the $service container is not running." 78

    revision=$(docker inspect \
        --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' \
        "$container_id" 2>/dev/null) ||
        fail "the $service container release revision could not be inspected." 78

    [ "$revision" = "$recorded_release" ] ||
        fail "the running $service container does not match the last verified release marker." 78
done

printf '%s\n' "Running BillWatch release matches the last verified release $recorded_release."