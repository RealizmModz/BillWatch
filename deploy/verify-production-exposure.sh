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

published_ports="$(compose ps --format json | tr -d '\r')"

if printf '%s\n' "$published_ports" | grep -Eq '(^|[^0-9])8080([^0-9]|$)'; then
    fail "A production service appears to publish port 8080. API/Web must remain private behind Caddy." 77
fi

if docker ps --format '{{.Names}} {{.Ports}}' |
   grep -E '^billwatch-(api|web|database)-' |
   grep -Eq '0\.0\.0\.0:|\[::\]:'; then
    fail "API, Web, or PostgreSQL is publicly published by Docker." 77
fi

edge_ports="$(
    docker ps --format '{{.Names}} {{.Ports}}' |
    grep '^billwatch-edge-' || true
)"

if [ -z "$edge_ports" ]; then
    fail "BillWatch edge container is not running." 69
fi

if ! printf '%s\n' "$edge_ports" | grep -q ':80->80/tcp'; then
    fail "Caddy is not publishing TCP port 80." 69
fi

if ! printf '%s\n' "$edge_ports" | grep -q ':443->443/tcp'; then
    fail "Caddy is not publishing TCP port 443." 69
fi

if ! printf '%s\n' "$edge_ports" | grep -q ':443->443/udp'; then
    fail "Caddy is not publishing UDP port 443." 69
fi

echo "BillWatch production exposure verification passed."
