#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Subscription lifecycle smoke test failed: $1" >&2
    exit 1
}

script="$root_dir/deploy/smoke-subscription-lifecycle.sh"
[ -f "$script" ] || fail "smoke script is missing"
sh -n "$script" || fail "smoke script has invalid POSIX shell syntax"

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
curl_log="$temp_dir/curl.log"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu

output=""
url=""

while [ "$#" -gt 0 ]
do
    case "$1" in
        --output)
            output="$2"
            shift 2
            ;;
        --write-out|--request|--header|--data-binary|--config)
            shift 2
            ;;
        --silent|--show-error)
            shift
            ;;
        https://*)
            url="$1"
            shift
            ;;
        *)
            shift
            ;;
    esac
done

printf '%s\n' "$url" >> "$BILLWATCH_TEST_CURL_LOG"

case "$url" in
    */api/auth/login)
        printf '%s' '{"accessToken":"test-access-token"}' > "$output"
        ;;
    */api/subscription/plans)
        printf '%s' '[{"billingInterval":"monthly","unitAmount":999,"currency":"usd"},{"billingInterval":"yearly","unitAmount":9999,"currency":"usd"}]' > "$output"
        ;;
    */api/subscription/checkout)
        printf '{"url":"%s"}' "${BILLWATCH_TEST_CHECKOUT_URL:-https://checkout.stripe.com/c/pay/billwatch-test}" > "$output"
        ;;
    */api/subscription/billing-portal)
        printf '{"url":"%s"}' "${BILLWATCH_TEST_PORTAL_URL:-https://billing.stripe.com/p/session/billwatch-test}" > "$output"
        ;;
    */api/subscription/sync)
        printf '%s' '{"succeeded":true}' > "$output"
        ;;
    */api/subscription)
        printf '%s' '{"isActive":false,"tier":null,"startsAtUtc":null,"endsAtUtc":null,"source":null,"billingAvailable":true,"isPaid":false,"billingInterval":null,"cancelAtPeriodEnd":false,"providerStatus":null}' > "$output"
        ;;
    *)
        printf '%s' '{}' > "$output"
        ;;
esac

printf '%s' '200'
EOF
chmod +x "$fake_bin/curl"

password_file="$temp_dir/password"
printf '%s\n' 'BillWatch!SubscriptionSmoke123' > "$password_file"
chmod 600 "$password_file"

run_smoke()
{
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_CURL_LOG="$curl_log" \
    BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL="subscription-smoke@billwatch.local" \
    BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE="$password_file" \
    BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE=false \
    BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID=false \
    sh "$script" https://billwatch.test
}

: > "$curl_log"
run_smoke >/dev/null ||
    fail "valid read-only smoke run failed"

grep -q '/api/subscription$' "$curl_log" ||
    fail "read-only run did not probe subscription status"
grep -q '/api/subscription/plans$' "$curl_log" ||
    fail "read-only run did not probe subscription plans"

if grep -Eq '/api/subscription/(checkout|billing-portal|sync)$' "$curl_log"; then
    fail "read-only defaults invoked a mutation-bearing subscription endpoint"
fi

if grep -q 'BillWatch!SubscriptionSmoke123' "$curl_log"; then
    fail "password leaked into curl URL/arguments"
fi

: > "$curl_log"
PATH="$fake_bin:$PATH" \
BILLWATCH_TEST_CURL_LOG="$curl_log" \
BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL="subscription-smoke@billwatch.local" \
BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE="$password_file" \
BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE=false \
BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID=false \
BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_CHECKOUT=true \
BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_PORTAL=true \
BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_SYNC=true \
sh "$script" https://billwatch.test >/dev/null ||
    fail "explicitly enabled controlled lifecycle probes failed"

for path in checkout billing-portal sync
do
    grep -q "/api/subscription/$path$" "$curl_log" ||
        fail "explicit opt-in did not invoke $path"
done

: > "$curl_log"
if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_TEST_CHECKOUT_URL="https://evil.example.test/session" \
   BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL="subscription-smoke@billwatch.local" \
   BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE="$password_file" \
   BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_CHECKOUT=true \
   sh "$script" https://billwatch.test >/dev/null 2>&1; then
    fail "checkout probe accepted a non-Stripe redirect host"
fi

if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL="subscription-smoke@billwatch.local" \
   BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE="$password_file" \
   BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_CHECKOUT=yes \
   sh "$script" https://billwatch.test >/dev/null 2>&1; then
    fail "smoke accepted an invalid checkout opt-in value"
fi

if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL="subscription-smoke@billwatch.local" \
   BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE="$password_file" \
   sh "$script" http://billwatch.test >/dev/null 2>&1; then
    fail "smoke accepted a non-HTTPS API URL"
fi

chmod 644 "$password_file"
if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL="subscription-smoke@billwatch.local" \
   BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE="$password_file" \
   sh "$script" https://billwatch.test >/dev/null 2>&1; then
    fail "smoke accepted weak password-file permissions"
fi
chmod 600 "$password_file"

if grep -q -- '--location' "$script"; then
    fail "smoke follows redirects; redirect host validation must remain explicit"
fi

grep -q 'checkout:https://checkout.stripe.com/' "$script" ||
    fail "checkout redirect host boundary is missing"
grep -q 'portal:https://billing.stripe.com/' "$script" ||
    fail "billing portal redirect host boundary is missing"

printf '%s\n' 'Subscription lifecycle smoke tests passed.'
