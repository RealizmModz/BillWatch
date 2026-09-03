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

compose()
{
    docker compose \
        --env-file "$environment_file" \
        --file "$deployment_directory/compose.production.yml" \
        "$@"
}

role_counts="$(
    compose exec -T database \
        psql \
        -U billwatch \
        -d billwatch \
        -Atqc \
        'SELECT "NormalizedName", COUNT(*)
         FROM "AspNetRoles"
         WHERE "NormalizedName" IN ('"'"'OWNER'"'"', '"'"'ADMIN'"'"', '"'"'MODERATOR'"'"')
         GROUP BY "NormalizedName"
         ORDER BY "NormalizedName";'
)"

for role in OWNER ADMIN MODERATOR
do
    count="$(
        printf '%s\n' "$role_counts" |
        awk -F'|' -v role="$role" '$1 == role { print $2 }'
    )"

    if [ "$count" != "1" ]; then
        fail "Expected exactly one $role role, found ${count:-0}." 77
    fi
done

echo "BillWatch Identity role schema verification passed."
