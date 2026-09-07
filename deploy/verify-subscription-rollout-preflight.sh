#!/bin/sh

set -eu

deployment_directory="${1:-}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

read_env_value()
{
    key="$1"
    sed -n "s/^${key}=//p" "$environment_file" | tail -n 1
}

is_placeholder()
{
    case "$1" in
        ""|*replace*|*example.com*) return 0 ;;
        *) return 1 ;;
    esac
}

[ "${BILLWATCH_SUBSCRIPTION_ROLLOUT_PREFLIGHT:-}" = "true" ] ||
    fail "Set BILLWATCH_SUBSCRIPTION_ROLLOUT_PREFLIGHT=true to run the subscription rollout preflight." 77

[ -n "$deployment_directory" ] || fail "A BillWatch production deployment directory is required." 64
[ -d "$deployment_directory" ] || fail "The BillWatch production deployment directory does not exist." 64

environment_file="$deployment_directory/.env.production"
[ -f "$environment_file" ] || fail "The production environment file is missing." 64
[ ! -L "$environment_file" ] || fail "The production environment file must not be a symbolic link." 77

mode="$(stat -c '%a' "$environment_file" 2>/dev/null || true)"
[ "$mode" = "600" ] || fail "The production environment file must have mode 600." 77

enforcement="$(read_env_value BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED | tr '[:upper:]' '[:lower:]')"
[ "$enforcement" = "false" ] || fail "Subscription enforcement must remain disabled during preflight." 77

stripe_enabled="$(read_env_value BILLWATCH_STRIPE_ENABLED | tr '[:upper:]' '[:lower:]')"
[ "$stripe_enabled" = "true" ] || fail "Stripe must be enabled and verified before subscription enforcement rollout." 77

cohort="$(read_env_value BILLWATCH_SUBSCRIPTION_ENFORCEMENT_COHORT)"
expected_cohort="${BILLWATCH_SUBSCRIPTION_ROLLOUT_EXPECTED_COHORT:-InternalTester}"
[ -n "$cohort" ] || fail "A non-empty subscription enforcement cohort is required." 77
[ "$cohort" = "$expected_cohort" ] || fail "The configured subscription enforcement cohort does not match the approved rollout cohort." 77

for key in STRIPE_SECRET_KEY STRIPE_WEBHOOK_SECRET STRIPE_MONTHLY_PRICE_ID STRIPE_YEARLY_PRICE_ID
do
    value="$(read_env_value "$key")"
    if is_placeholder "$value"; then
        fail "$key is missing or still contains a placeholder value." 77
    fi

done

case "$(read_env_value STRIPE_SECRET_KEY)" in
    sk_live_*|sk_test_*) ;;
    *) fail "STRIPE_SECRET_KEY does not have a recognized Stripe secret-key prefix." 77 ;;
esac

case "$(read_env_value STRIPE_WEBHOOK_SECRET)" in
    whsec_*) ;;
    *) fail "STRIPE_WEBHOOK_SECRET does not have a recognized Stripe webhook-secret prefix." 77 ;;
esac

case "$(read_env_value STRIPE_MONTHLY_PRICE_ID)" in
    price_*) ;;
    *) fail "STRIPE_MONTHLY_PRICE_ID does not have a recognized Stripe price identifier prefix." 77 ;;
esac

case "$(read_env_value STRIPE_YEARLY_PRICE_ID)" in
    price_*) ;;
    *) fail "STRIPE_YEARLY_PRICE_ID does not have a recognized Stripe price identifier prefix." 77 ;;
esac

printf '%s\n' "Subscription rollout preflight passed with enforcement still disabled for cohort: $cohort"
