#!/bin/sh

set -eu

deployment_directory="${1:-}"
expected_email="${2:-}"

fail()
{
    message="$1"
    exit_code="${2:-64}"

    echo "$message" >&2
    exit "$exit_code"
}

if [ -z "$deployment_directory" ] ||
   [ ! -f "$deployment_directory/compose.production.yml" ]; then
    fail "A BillWatch deployment directory is required."
fi

if [ -z "$expected_email" ]; then
    fail "The expected email address for the sole BillWatch account is required."
fi

deployment_directory="$(
    cd "$deployment_directory"
    pwd -P
)"

environment_file="$deployment_directory/.env.production"

if [ ! -f "$environment_file" ]; then
    fail ".env.production was not found." 66
fi

if [ -L "$environment_file" ]; then
    fail ".env.production must not be a symbolic link." 77
fi

environment_owner="$(
    stat -c '%u' "$environment_file"
)"

environment_permissions="$(
    stat -c '%a' "$environment_file"
)"

if [ "$environment_owner" -ne "$(id -u)" ]; then
    fail ".env.production must be owned by the deployment account." 77
fi

if [ "$((environment_permissions % 100))" -ne 0 ]; then
    fail ".env.production must be inaccessible to group and other users." 77
fi

compose()
{
    docker compose \
        --env-file "$environment_file" \
        --file "$deployment_directory/compose.production.yml" \
        "$@"
}

compose up \
    --detach \
    --wait \
    database >/dev/null

user_count="$(
    compose exec -T database \
        psql \
        -U billwatch \
        -d billwatch \
        -Atqc \
        'SELECT COUNT(*) FROM "AspNetUsers";' |
        tr -d '\r'
)"

if [ "$user_count" != "1" ]; then
    fail "Owner bootstrap requires exactly one BillWatch user. Found: $user_count." 77
fi

sole_email="$(
    compose exec -T database \
        psql \
        -U billwatch \
        -d billwatch \
        -Atqc \
        'SELECT COALESCE("Email", '\''\'') FROM "AspNetUsers" LIMIT 1;' |
        tr -d '\r'
)"

if [ "$sole_email" != "$expected_email" ]; then
    fail "The supplied email does not match the sole BillWatch account." 77
fi

owner_role_count="$(
    compose exec -T database \
        psql \
        -U billwatch \
        -d billwatch \
        -Atqc \
        'SELECT COUNT(*) FROM "AspNetRoles" WHERE "NormalizedName" = '\''OWNER'\'';' |
        tr -d '\r'
)"

if [ "$owner_role_count" != "1" ]; then
    fail "BillWatch must contain exactly one Owner role before bootstrap." 77
fi

existing_owner_count="$(
    compose exec -T database \
        psql \
        -U billwatch \
        -d billwatch \
        -Atqc \
        'SELECT COUNT(*)
         FROM "AspNetUserRoles" ur
         JOIN "AspNetRoles" r
           ON r."Id" = ur."RoleId"
         WHERE r."NormalizedName" = '\''OWNER'\'';' |
        tr -d '\r'
)"

if [ "$existing_owner_count" != "0" ]; then
    fail "An Owner already exists. Bootstrap is intentionally unavailable." 77
fi

compose exec -T database \
    psql \
    -U billwatch \
    -d billwatch \
    -v ON_ERROR_STOP=1 \
    -c '
BEGIN;

DO $bootstrap$
DECLARE
    current_user_count integer;
    current_owner_count integer;
    current_owner_role_count integer;
BEGIN
    SELECT COUNT(*)
    INTO current_user_count
    FROM "AspNetUsers";

    SELECT COUNT(*)
    INTO current_owner_role_count
    FROM "AspNetRoles"
    WHERE "NormalizedName" = '\''OWNER'\'';

    SELECT COUNT(*)
    INTO current_owner_count
    FROM "AspNetUserRoles" ur
    JOIN "AspNetRoles" r
      ON r."Id" = ur."RoleId"
    WHERE r."NormalizedName" = '\''OWNER'\'';

    IF current_user_count <> 1 THEN
        RAISE EXCEPTION
            '\''Owner bootstrap requires exactly one user.'\'';
    END IF;

    IF current_owner_role_count <> 1 THEN
        RAISE EXCEPTION
            '\''Owner bootstrap requires exactly one Owner role.'\'';
    END IF;

    IF current_owner_count <> 0 THEN
        RAISE EXCEPTION
            '\''Owner bootstrap cannot run after an Owner exists.'\'';
    END IF;

    INSERT INTO "AspNetUserRoles"
        ("UserId", "RoleId")
    SELECT
        u."Id",
        r."Id"
    FROM "AspNetUsers" u
    CROSS JOIN "AspNetRoles" r
    WHERE r."NormalizedName" = '\''OWNER'\'';
END
$bootstrap$;

COMMIT;
' >/dev/null

verified_owner_count="$(
    compose exec -T database \
        psql \
        -U billwatch \
        -d billwatch \
        -Atqc \
        'SELECT COUNT(*)
         FROM "AspNetUserRoles" ur
         JOIN "AspNetRoles" r
           ON r."Id" = ur."RoleId"
         WHERE r."NormalizedName" = '\''OWNER'\'';' |
        tr -d '\r'
)"

if [ "$verified_owner_count" != "1" ]; then
    fail "Owner bootstrap did not produce exactly one Owner." 70
fi

echo "BillWatch Owner bootstrap completed successfully."
