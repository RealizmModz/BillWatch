#!/bin/sh

set -eu

deployment_directory="${1:-}"

fail()
{
    echo "$1" >&2
    exit "${2:-1}"
}

if [ -z "$deployment_directory" ] ||
   [ ! -f "$deployment_directory/.env.production" ]; then
    fail "A BillWatch production deployment directory is required." 64
fi

environment_file="$deployment_directory/.env.production"

value="$(
    sed -n 's/^BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=//p' \
        "$environment_file" |
    tail -n 1 |
    tr '[:upper:]' '[:lower:]'
)"

case "$value" in
    ""|false)
        echo "BillWatch subscription enforcement is safely disabled."
        ;;
    true)
        fail "Subscription enforcement is enabled. Beta-readiness verification requires it to remain disabled." 77
        ;;
    *)
        fail "Subscription enforcement has an unrecognized value." 77
        ;;
esac
