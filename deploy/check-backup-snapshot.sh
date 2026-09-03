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

snapshot_output="$(
    compose --profile operations run \
        --rm \
        backup \
        snapshots \
        --tag billwatch-complete \
        --latest 1
)"

if ! printf '%s\n' "$snapshot_output" |
   grep -q 'billwatch-complete'; then
    fail "No completed BillWatch backup snapshot was found." 69
fi

printf '%s\n' "$snapshot_output"
