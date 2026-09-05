#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Subscription rollout preflight test failed: $1" >&2
    exit 1
}

script="$root_dir/deploy/verify-subscription-rollout-preflight.sh"
[ -f "$script" ] || fail "preflight script is missing"
sh -n "$script" || fail "preflight script has invalid POSIX shell syntax"

deployment="$temp_dir/deployment"
mkdir -p "$deployment"
env_file="$deployment/.env.production"

write_valid_env()
{
    cat > "$env_file" <<'EOF'
BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false
BILLWATCH_SUBSCRIPTION_ENFORCEMENT_COHORT=InternalTester
BILLWATCH_STRIPE_ENABLED=true
STRIPE_SECRET_KEY=sk_test_billwatch_preflight_fixture
STRIPE_WEBHOOK_SECRET=whsec_billwatch_preflight_fixture
STRIPE_MONTHLY_PRICE_ID=price_monthly_billwatch_fixture
STRIPE_YEARLY_PRICE_ID=price_yearly_billwatch_fixture
EOF
    chmod 600 "$env_file"
}

run_preflight()
{
    BILLWATCH_SUBSCRIPTION_ROLLOUT_PREFLIGHT=true sh "$script" "$deployment"
}

write_valid_env
run_preflight >/dev/null || fail "valid guarded preflight was rejected"

grep -q '^BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false$' "$env_file" ||
    fail "preflight mutated subscription enforcement"

if sh "$script" "$deployment" >/dev/null 2>&1; then
    fail "preflight ran without explicit opt-in"
fi

write_valid_env
chmod 644 "$env_file"
if run_preflight >/dev/null 2>&1; then
    fail "preflight accepted a weakly protected production environment file"
fi

write_valid_env
printf '%s\n' 'BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=true' >> "$env_file"
if run_preflight >/dev/null 2>&1; then
    fail "preflight accepted enabled subscription enforcement"
fi

write_valid_env
sed -i 's/^BILLWATCH_STRIPE_ENABLED=true$/BILLWATCH_STRIPE_ENABLED=false/' "$env_file"
if run_preflight >/dev/null 2>&1; then
    fail "preflight accepted disabled Stripe integration"
fi

write_valid_env
sed -i 's/^BILLWATCH_SUBSCRIPTION_ENFORCEMENT_COHORT=InternalTester$/BILLWATCH_SUBSCRIPTION_ENFORCEMENT_COHORT=AllUsers/' "$env_file"
if run_preflight >/dev/null 2>&1; then
    fail "preflight accepted an unapproved rollout cohort"
fi

write_valid_env
sed -i 's/^STRIPE_SECRET_KEY=.*/STRIPE_SECRET_KEY=replace-with-stripe-secret-key/' "$env_file"
if run_preflight >/dev/null 2>&1; then
    fail "preflight accepted a placeholder Stripe secret"
fi

write_valid_env
sed -i 's/^STRIPE_WEBHOOK_SECRET=.*/STRIPE_WEBHOOK_SECRET=not-a-webhook-secret/' "$env_file"
if run_preflight >/dev/null 2>&1; then
    fail "preflight accepted an invalid Stripe webhook secret identifier"
fi

write_valid_env
BILLWATCH_SUBSCRIPTION_ROLLOUT_PREFLIGHT=true BILLWATCH_SUBSCRIPTION_ROLLOUT_EXPECTED_COHORT=TrustedBeta sh "$script" "$deployment" >/dev/null 2>&1 &&
    fail "preflight ignored an explicitly approved cohort mismatch"

sh "$root_dir/deploy/tests/subscription-lifecycle-smoke-tests.sh" ||
    fail "subscription lifecycle smoke harness regression suite failed"

printf '%s\n' 'Subscription rollout preflight tests passed.'
