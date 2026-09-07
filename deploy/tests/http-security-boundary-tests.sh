#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "HTTP security boundary test failed: $1" >&2
    exit 1
}

expect_failure()
{
    if "$@" >/dev/null 2>&1; then
        printf '%s\n' "Expected command to fail: $*" >&2
        exit 1
    fi
}

fake_bin="$temp_dir/bin"
mkdir "$fake_bin"

cat > "$fake_bin/curl" <<'SCRIPT'
#!/bin/sh
set -eu

headers=
body=
url=
method=GET
cookie_jar=

while [ "$#" -gt 0 ]; do
    case "$1" in
        --dump-header)
            shift
            headers=$1
            ;;
        --output)
            shift
            body=$1
            ;;
        --request)
            shift
            method=$1
            ;;
        --cookie-jar)
            shift
            cookie_jar=$1
            ;;
        https://*)
            url=$1
            ;;
    esac
    shift
done

[ -n "$headers" ] || exit 2
[ -n "$body" ] || exit 2

case "$url:$method" in
    https://api.billwatch.test/api/account/export:GET)
        cat > "$headers" <<EOF_HEADERS
HTTP/2 401
Strict-Transport-Security: max-age=31536000
Cache-Control: no-store, no-cache, max-age=0, must-revalidate
Pragma: no-cache
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
EOF_HEADERS
        [ "${BILLWATCH_TEST_EXPOSE_SERVER:-false}" != true ] || printf '%s\n' 'Server: Caddy' >> "$headers"
        : > "$body"
        printf '401'
        ;;
    https://app.billwatch.test/register:GET)
        cat > "$headers" <<EOF_HEADERS
HTTP/2 200
Strict-Transport-Security: max-age=31536000
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Content-Security-Policy: frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'
EOF_HEADERS
        [ "${BILLWATCH_TEST_EXPOSE_SERVER:-false}" != true ] || printf '%s\n' 'Server: Caddy' >> "$headers"
        printf '%s\n' '<input name="__RequestVerificationToken" type="hidden" value="test-antiforgery-token" />' > "$body"
        [ -z "$cookie_jar" ] || printf '%s\n' '# test cookie jar' > "$cookie_jar"
        printf '200'
        ;;
    https://app.billwatch.test/auth/logout:POST)
        cat > "$headers" <<EOF_HEADERS
HTTP/2 302
Cache-Control: no-store, no-cache, max-age=0, must-revalidate
Location: /
EOF_HEADERS
        [ "${BILLWATCH_TEST_EXPOSE_SERVER:-false}" != true ] || printf '%s\n' 'Server: Caddy' >> "$headers"
        : > "$body"
        printf '302'
        ;;
    *)
        printf '%s\n' "Unexpected fake curl request: $method $url" >&2
        exit 3
        ;;
esac
SCRIPT

chmod 755 "$fake_bin/curl"

PATH="$fake_bin:$PATH" \
    sh "$root_dir/deploy/check-http-security-boundaries.sh" \
    'https://api.billwatch.test' \
    'https://app.billwatch.test' >/dev/null

expect_failure env \
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_EXPOSE_SERVER=true \
    sh "$root_dir/deploy/check-http-security-boundaries.sh" \
    'https://api.billwatch.test' \
    'https://app.billwatch.test'

expect_failure sh \
    "$root_dir/deploy/check-http-security-boundaries.sh" \
    'http://api.billwatch.test' \
    'https://app.billwatch.test'

expect_failure env \
    BILLWATCH_HTTP_SECURITY_ALLOW_INSECURE=maybe \
    sh "$root_dir/deploy/check-http-security-boundaries.sh" \
    'https://api.billwatch.test' \
    'https://app.billwatch.test'

grep -F 'check-http-security-boundaries.sh' "$root_dir/deploy/deploy-production.sh" >/dev/null ||
    fail "production deployment does not invoke the HTTP security boundary verifier."

grep -F -- '-Server' "$root_dir/deploy/Caddyfile" >/dev/null ||
    fail "Caddy does not strip its public Server header."

grep -F 'Strict-Transport-Security "max-age=31536000"' "$root_dir/deploy/Caddyfile" >/dev/null ||
    fail "Caddy does not enforce one-year HSTS at the public TLS boundary."

if grep -F '/auth/register' "$root_dir/deploy/check-http-security-boundaries.sh" >/dev/null; then
    fail "HTTP security probe must not create persistent user data."
fi

grep -F '/auth/logout' "$root_dir/deploy/check-http-security-boundaries.sh" >/dev/null ||
    fail "HTTP security probe does not exercise an antiforgery-protected auth mutation."

printf '%s\n' 'HTTP security boundary script tests passed.'
