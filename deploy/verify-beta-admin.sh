#!/bin/sh

set -eu

deployment_directory="${1:-}"

if [ -z "$deployment_directory" ]; then
    echo "Usage: $0 <deployment-directory>" >&2
    exit 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"

sh "$deployment_directory/deploy/verify-identity-role-schema.sh" \
    "$deployment_directory"

sh "$deployment_directory/deploy/verify-owner-count.sh" \
    "$deployment_directory"

sh "$deployment_directory/deploy/verify-subscription-safety.sh" \
    "$deployment_directory"

echo "BillWatch beta admin prerequisites passed."
