#!/bin/sh

set -eu

api_base_url="${1:-}"
email="${BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL:-}"
password_file="${BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE:-}"
expected_active="${BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE:-skip}"
expected_paid="${BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID:-skip}"
expected_provider_status="${BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PROVIDER_STATUS:-skip}"
allow_checkout="${BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_CHECKOUT:-false}"
checkout_interval="${BILLWATCH_SUBSCRIPTION_SMOKE_CHECKOUT_INTERVAL:-monthly}"
allow_portal="${BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_PORTAL:-false}"
allow_sync="${BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_SYNC:-false}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

require_boolean_or_skip()
{
    value="$1"
    label="$2"

    case "$value" in
        true|false|skip) ;;
        *) fail "$label must be true, false, or skip." 64 ;;
    esac
}

require_boolean()
{
    value="$1"
    label="$2"

    case "$value" in
        true|false) ;;
        *) fail "$label must be true or false." 64 ;;
    esac
}

require_https_url()
{
    value="$1"
    label="$2"

    case "$value" in
        https://*) ;;
        *) fail "$label must use HTTPS." 64 ;;
    esac

    case "$value" in
        *[[:space:]]*) fail "$label must not contain whitespace." 64 ;;
    esac
}

require_stripe_redirect()
{
    value="$1"
    kind="$2"

    case "$kind:$value" in
        checkout:https://checkout.stripe.com/*) ;;
        portal:https://billing.stripe.com/*) ;;
        *) fail "The $kind response returned an unexpected redirect host." 70 ;;
    esac
}

json_string()
{
    key="$1"
    file="$2"

    sed -n "s/.*\"${key}\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$file" |
        head -n 1
}

json_boolean()
{
    key="$1"
    file="$2"

    sed -n "s/.*\"${key}\"[[:space:]]*:[[:space:]]*\(true\|false\).*/\1/p" "$file" |
        head -n 1
}

assert_no_sensitive_fields()
{
    file="$1"

    if grep -Eiq \
        '"(accessToken|refreshToken|protectedAccessToken|encryptedAccessToken|plaidAccessToken|storagePath|storedFilePath|passwordHash|securityStamp|secret|webhookSecret)"[[:space:]]*:' \
        "$file"; then
        fail "Subscription response contained a forbidden secret or internal-storage field." 70
    fi
}

require_boolean_or_skip "$expected_active" "BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE"
require_boolean_or_skip "$expected_paid" "BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID"
require_boolean "$allow_checkout" "BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_CHECKOUT"
require_boolean "$allow_portal" "BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_PORTAL"
require_boolean "$allow_sync" "BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_SYNC"

case "$checkout_interval" in
    monthly|yearly) ;;
    *) fail "BILLWATCH_SUBSCRIPTION_SMOKE_CHECKOUT_INTERVAL must be monthly or yearly." 64 ;;
esac

[ -n "$api_base_url" ] ||
    fail "Usage: $0 <https-api-base-url>" 64
require_https_url "$api_base_url" "The subscription smoke-test API base URL"
api_base_url="${api_base_url%/}"

[ -n "$email" ] ||
    fail "BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL is required." 64
[ -n "$password_file" ] ||
    fail "BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE is required." 64
[ -f "$password_file" ] ||
    fail "BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE must reference a regular file." 64
[ ! -L "$password_file" ] ||
    fail "BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE must not be a symbolic link." 64

password_mode="$(stat -c '%a' "$password_file" 2>/dev/null || true)"
[ "$password_mode" = "600" ] ||
    fail "BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE must have mode 600." 64

IFS= read -r password < "$password_file" || true
[ -n "${password:-}" ] ||
    fail "The subscription smoke-test password file is empty." 64

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"

cleanup()
{
    rm -rf "$work_directory"
}

trap cleanup EXIT HUP INT TERM

login_payload="$work_directory/login.json"
login_response="$work_directory/login-response.json"
auth_config="$work_directory/auth.curl"
status_response="$work_directory/status.json"
plans_response="$work_directory/plans.json"
redirect_response="$work_directory/redirect.json"
sync_response="$work_directory/sync.json"

json_escape()
{
    printf '%s' "$1" |
        sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/\\t/g'
}

printf '{"email":"%s","password":"%s"}' \
    "$(json_escape "$email")" \
    "$(json_escape "$password")" \
    > "$login_payload"
chmod 600 "$login_payload"
unset password

login_code="$(
    curl \
        --silent \
        --show-error \
        --output "$login_response" \
        --write-out '%{http_code}' \
        --request POST \
        --header 'Content-Type: application/json' \
        --data-binary "@$login_payload" \
        "$api_base_url/api/auth/login"
)"
rm -f "$login_payload"

[ "$login_code" = "200" ] ||
    fail "Subscription smoke authentication failed with HTTP $login_code." 69

access_token="$(json_string accessToken "$login_response")"
rm -f "$login_response"

[ -n "$access_token" ] ||
    fail "Authentication response did not contain an access token." 69

printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

get_subscription_status()
{
    output_file="$1"

    code="$(
        curl \
            --silent \
            --show-error \
            --output "$output_file" \
            --write-out '%{http_code}' \
            --config "$auth_config" \
            "$api_base_url/api/subscription"
    )"

    [ "$code" = "200" ] ||
        fail "Subscription status probe failed with HTTP $code." 69

    assert_no_sensitive_fields "$output_file"
}

assert_subscription_expectations()
{
    file="$1"

    active="$(json_boolean isActive "$file")"
    paid="$(json_boolean isPaid "$file")"
    billing_available="$(json_boolean billingAvailable "$file")"
    provider_status="$(json_string providerStatus "$file")"

    [ -n "$active" ] ||
        fail "Subscription status response did not contain isActive." 70
    [ -n "$paid" ] ||
        fail "Subscription status response did not contain isPaid." 70
    [ -n "$billing_available" ] ||
        fail "Subscription status response did not contain billingAvailable." 70

    if [ "$expected_active" != "skip" ] && [ "$active" != "$expected_active" ]; then
        fail "Subscription active-state expectation failed: expected $expected_active, received $active." 69
    fi

    if [ "$expected_paid" != "skip" ] && [ "$paid" != "$expected_paid" ]; then
        fail "Subscription paid-state expectation failed: expected $expected_paid, received $paid." 69
    fi

    if [ "$expected_provider_status" != "skip" ] &&
       [ "$provider_status" != "$expected_provider_status" ]; then
        fail "Subscription provider-status expectation failed." 69
    fi

    printf 'PASS subscription status (active=%s paid=%s billingAvailable=%s)\n' \
        "$active" "$paid" "$billing_available"
}

get_subscription_status "$status_response"
assert_subscription_expectations "$status_response"
billing_available="$(json_boolean billingAvailable "$status_response")"

plans_code="$(
    curl \
        --silent \
        --show-error \
        --output "$plans_response" \
        --write-out '%{http_code}' \
        --config "$auth_config" \
        "$api_base_url/api/subscription/plans"
)"

[ "$plans_code" = "200" ] ||
    fail "Subscription plans probe failed with HTTP $plans_code." 69
assert_no_sensitive_fields "$plans_response"

if [ "$billing_available" = "true" ]; then
    grep -Eq '"billingInterval"[[:space:]]*:[[:space:]]*"(monthly|yearly)"' "$plans_response" ||
        fail "Billing is available but no recognized paid plan was returned." 70
fi

printf '%s\n' 'PASS subscription plans response'

if [ "$allow_checkout" = "true" ]; then
    checkout_payload="$work_directory/checkout.json"
    printf '{"billingInterval":"%s"}' "$checkout_interval" > "$checkout_payload"
    chmod 600 "$checkout_payload"

    checkout_code="$(
        curl \
            --silent \
            --show-error \
            --output "$redirect_response" \
            --write-out '%{http_code}' \
            --request POST \
            --header 'Content-Type: application/json' \
            --data-binary "@$checkout_payload" \
            --config "$auth_config" \
            "$api_base_url/api/subscription/checkout"
    )"
    rm -f "$checkout_payload"

    [ "$checkout_code" = "200" ] ||
        fail "Controlled Stripe checkout-session probe failed with HTTP $checkout_code." 69
    assert_no_sensitive_fields "$redirect_response"
    checkout_url="$(json_string url "$redirect_response")"
    [ -n "$checkout_url" ] ||
        fail "Checkout-session response did not contain a redirect URL." 70
    require_stripe_redirect "$checkout_url" checkout
    printf '%s\n' 'PASS controlled Stripe checkout-session creation'
else
    printf '%s\n' 'SKIP Stripe checkout-session creation (safe default)'
fi

if [ "$allow_portal" = "true" ]; then
    portal_code="$(
        curl \
            --silent \
            --show-error \
            --output "$redirect_response" \
            --write-out '%{http_code}' \
            --request POST \
            --config "$auth_config" \
            "$api_base_url/api/subscription/billing-portal"
    )"

    [ "$portal_code" = "200" ] ||
        fail "Controlled Stripe billing-portal probe failed with HTTP $portal_code." 69
    assert_no_sensitive_fields "$redirect_response"
    portal_url="$(json_string url "$redirect_response")"
    [ -n "$portal_url" ] ||
        fail "Billing-portal response did not contain a redirect URL." 70
    require_stripe_redirect "$portal_url" portal
    printf '%s\n' 'PASS controlled Stripe billing-portal session creation'
else
    printf '%s\n' 'SKIP Stripe billing-portal session creation (safe default)'
fi

if [ "$allow_sync" = "true" ]; then
    sync_code="$(
        curl \
            --silent \
            --show-error \
            --output "$sync_response" \
            --write-out '%{http_code}' \
            --request POST \
            --config "$auth_config" \
            "$api_base_url/api/subscription/sync"
    )"

    [ "$sync_code" = "200" ] ||
        fail "Controlled Stripe subscription sync failed with HTTP $sync_code." 69
    assert_no_sensitive_fields "$sync_response"
    grep -Eq '"succeeded"[[:space:]]*:[[:space:]]*true' "$sync_response" ||
        fail "Subscription sync response did not report success." 70

    get_subscription_status "$status_response"
    assert_subscription_expectations "$status_response"
    printf '%s\n' 'PASS controlled subscription sync and post-sync status'
else
    printf '%s\n' 'SKIP provider subscription sync (safe default)'
fi

printf '%s\n' 'BillWatch subscription lifecycle smoke harness passed.'
