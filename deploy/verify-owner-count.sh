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

owner_count="$(
    compose exec -T database \
        psql \
        -U billwatch \
        -d billwatch \
        -Atqc \
        'SELECT COUNT(*)
         FROM "AspNetUserRoles" ur
         JOIN "AspNetRoles" r
           ON r."Id" = ur."RoleId"
         WHERE r."NormalizedName" = '"'"'OWNER'"'"';'
)"

if [ "$owner_count" != "1" ]; then
    fail "Expected exactly one BillWatch Owner, found $owner_count." 77
fi

echo "BillWatch Owner verification passed."
