#!/bin/sh

set -eu

fail()
{
    printf '%s\n' "BillWatch readiness monitor failed: $1" >&2
    exit 1
}

url=${1:-${BILLWATCH_PRODUCTION_URL:-}}
[ -n "$url" ] || fail "BILLWATCH_PRODUCTION_URL is not configured."

case "$url" in
    https://*) ;;
    *) fail "the monitor target must use HTTPS." ;;
esac

origin=${url#https://}
origin=${origin%/}

case "$origin" in
    */*|*\?*|*\#*|*@*|*:*|'')
        fail "the monitor target must be a hostname-only HTTPS origin."
        ;;
    localhost|*.localhost|*.local|*.internal|*[!A-Za-z0-9.-]*|.*|*..*|*.)
        fail "the monitor target must use a public DNS hostname."
        ;;
esac

case "$origin" in
    *.*) ;;
    *) fail "the monitor target must contain a public DNS suffix." ;;
esac

case "$origin" in
    *[!0-9.]*) ;;
    *) fail "the monitor target must not be a numeric address." ;;
esac

addresses=$(getent ahosts "$origin" 2>/dev/null | awk '{ print $1 }' | sort -u)
[ -n "$addresses" ] || fail "the monitor hostname did not resolve."

for address in $addresses
do
    case "$address" in
        10.*|127.*|169.254.*|192.168.*|0.*|::1|fc*|fd*|fe8*|fe9*|fea*|feb*)
            fail "the monitor hostname resolves to a non-public address."
            ;;
        172.*)
            second_octet=$(printf '%s' "$address" | cut -d. -f2)
            if [ "$second_octet" -ge 16 ] 2>/dev/null && [ "$second_octet" -le 31 ]; then
                fail "the monitor hostname resolves to a non-public address."
            fi
            ;;
        100.*)
            second_octet=$(printf '%s' "$address" | cut -d. -f2)
            if [ "$second_octet" -ge 64 ] 2>/dev/null && [ "$second_octet" -le 127 ]; then
                fail "the monitor hostname resolves to a non-public address."
            fi
            ;;
    esac
done

response_file=$(mktemp)
trap 'rm -f "$response_file"' EXIT HUP INT TERM

attempt=1
while [ "$attempt" -le 3 ]
do
    if curl \
        --connect-timeout 5 \
        --fail \
        --max-redirs 0 \
        --max-time 10 \
        --output "$response_file" \
        --proto '=https' \
        --silent \
        --show-error \
        --tlsv1.2 \
        "https://$origin/health/ready"
    then
        normalized=$(tr -d '[:space:]' < "$response_file")
        [ "$normalized" = '{"status":"ready"}' ] || fail "the readiness response was not the expected bounded contract."
        printf '%s\n' "BillWatch readiness monitor passed for $origin."
        exit 0
    fi

    attempt=$((attempt + 1))
done

fail "the production readiness endpoint failed three bounded attempts."
