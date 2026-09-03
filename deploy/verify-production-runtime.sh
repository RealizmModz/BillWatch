#!/bin/sh

set -eu

deployment_directory="${1:-}"

fail()
{
    echo "$1" >&2
    exit "${2:-1}"
}

if [ -z "$deployment_directory" ] ||
   [ ! -f "$deployment_directory/compose.production.yml" ]; then
    fail "A BillWatch deployment directory is required." 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
environment_file="$deployment_directory/.env.production"

if [ ! -f "$environment_file" ]; then
    fail ".env.production was not found." 66
fi

compose()
{
    docker compose \
        --env-file "$environment_file" \
        --file "$deployment_directory/compose.production.yml" \
        "$@"
}

required_services="database api web edge"

for service in $required_services
do
    if ! compose ps --status running --services |
       grep -qx "$service"; then
        fail "Required production service is not running: $service" 69
    fi
done

for service in database api web
do
    container_id="$(compose ps -q "$service")"

    if [ -z "$container_id" ]; then
        fail "Could not resolve production container: $service" 69
    fi

    health_status="$(
        docker inspect \
            --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
            "$container_id"
    )"

    if [ "$health_status" != "healthy" ]; then
        fail "Production container is not healthy: $service ($health_status)" 69
    fi
done

release_id="$(git -C "$deployment_directory" rev-parse HEAD)"
recorded_release="$(cat "$deployment_directory/.billwatch-release" 2>/dev/null || true)"

if [ "$recorded_release" != "$release_id" ]; then
    fail "Recorded production release does not match the deployed Git checkout." 77
fi

echo "BillWatch production runtime verification passed for $release_id."
