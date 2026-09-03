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

for service in api web database
do
    container_id="$(compose ps -q "$service")"

    if [ -z "$container_id" ]; then
        fail "Could not resolve production container: $service" 69
    fi

    published_bindings="$(
        docker inspect \
            --format '{{range $port, $bindings := .NetworkSettings.Ports}}{{range $bindings}}{{printf "%s:%s->%s " .HostIp .HostPort $port}}{{end}}{{end}}' \
            "$container_id"
    )"

    if [ -n "$published_bindings" ]; then
        fail "$service unexpectedly publishes a host port: $published_bindings" 77
    fi
done

edge_container_id="$(compose ps -q edge)"

if [ -z "$edge_container_id" ]; then
    fail "BillWatch edge container is not running." 69
fi

edge_ports="$(
    docker inspect \
        --format '{{range $port, $bindings := .NetworkSettings.Ports}}{{range $bindings}}{{printf "%s:%s->%s\n" .HostIp .HostPort $port}}{{end}}{{end}}' \
        "$edge_container_id"
)"

if ! printf '%s\n' "$edge_ports" | grep -Eq ':(80)->80/tcp$'; then
    fail "Caddy is not publishing TCP port 80." 69
fi

if ! printf '%s\n' "$edge_ports" | grep -Eq ':(443)->443/tcp$'; then
    fail "Caddy is not publishing TCP port 443." 69
fi

if ! printf '%s\n' "$edge_ports" | grep -Eq ':(443)->443/udp$'; then
    fail "Caddy is not publishing UDP port 443." 69
fi

echo "BillWatch production exposure verification passed."
