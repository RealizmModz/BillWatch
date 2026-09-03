#!/bin/sh

set -eu

deployment_directory="${1:-}"

if [ -z "$deployment_directory" ]; then
    echo "Usage: $0 <deployment-directory>" >&2
    exit 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"

sh "$deployment_directory/deploy/verify-production.sh" \
    "$deployment_directory"

sh "$deployment_directory/deploy/verify-beta-admin.sh" \
    "$deployment_directory"

sh "$deployment_directory/deploy/check-backup-timer.sh"

sh "$deployment_directory/deploy/check-backup-snapshot.sh" \
    "$deployment_directory"

echo "BillWatch automated private-beta host prerequisites passed."
echo "Browser, Plaid, statement, restore-drill, reboot, and alert-delivery checks remain operator-verification gates."
