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

if [ -L "$environment_file" ]; then
    fail ".env.production must not be a symbolic link." 77
fi

environment_owner="$(stat -c '%u' "$environment_file")"
environment_permissions="$(stat -c '%a' "$environment_file")"

if [ "$environment_owner" -ne "$(id -u)" ]; then
    fail ".env.production must be owned by the deployment account." 77
fi

if [ "$environment_permissions" != "600" ]; then
    fail ".env.production must have mode 600." 77
fi

if git -C "$deployment_directory" ls-files --error-unmatch .env.production >/dev/null 2>&1; then
    fail ".env.production must never be tracked by Git." 77
fi

if ! git -C "$deployment_directory" check-ignore -q .env.production; then
    fail ".env.production must be covered by Git ignore rules." 77
fi

echo "BillWatch production permission verification passed."
