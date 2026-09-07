#!/bin/sh

set -eu

deployment_directory="${1:-}"

if [ -z "$deployment_directory" ]; then
    echo "Usage: $0 <deployment-directory>" >&2
    exit 64
fi

deployment_directory="$(cd "$deployment_directory" && pwd -P)"

sh "$deployment_directory/deploy/validate-production-env.sh" \
    "$deployment_directory/.env.production"

sh "$deployment_directory/deploy/verify-production-permissions.sh" \
    "$deployment_directory"

sh "$deployment_directory/deploy/verify-release-integrity.sh" \
    "$deployment_directory"

sh "$deployment_directory/deploy/verify-production-exposure.sh" \
    "$deployment_directory"

sh "$deployment_directory/deploy/verify-production-runtime.sh" \
    "$deployment_directory"

. "$deployment_directory/.env.production"

sh "$deployment_directory/deploy/monitor-readiness.sh" \
    "https://$BILLWATCH_HOST"

sh "$deployment_directory/deploy/monitor-readiness.sh" \
    "https://$BILLWATCH_WEB_HOST"

echo "BillWatch production verification passed."
