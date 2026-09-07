# Subscription enforcement rollout gate

BillWatch private beta keeps `BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false` by default. The preflight in this directory is intentionally non-mutating: it proves configuration prerequisites while enforcement is still off. It does not enable billing enforcement, restart services, change Stripe objects, or claim that webhook delivery has been observed in production.

Run it only against the protected production deployment after Stripe products/prices, Customer Portal, webhook delivery, and the intended rollout cohort have been verified:

```sh
BILLWATCH_SUBSCRIPTION_ROLLOUT_PREFLIGHT=true \
  sh deploy/verify-subscription-rollout-preflight.sh /opt/billwatch
```

The production `.env.production` must be a non-symlink mode-600 file. Preflight requires Stripe to be enabled, requires non-placeholder Stripe secret/webhook/price identifiers with expected prefixes, requires subscription enforcement to remain disabled, and requires the configured cohort to equal `InternalTester` unless an operator deliberately supplies `BILLWATCH_SUBSCRIPTION_ROLLOUT_EXPECTED_COHORT`.

## Controlled subscription lifecycle proof

After preflight passes, use `deploy/smoke-subscription-lifecycle.sh` with dedicated tester accounts. The harness is read-only by default: it authenticates, verifies the current subscription status, checks that the plans surface is healthy, validates expected paid/active state when supplied, and rejects responses that expose credentials or internal-storage fields.

Credentials must be supplied through a mode-600, non-symlink password file so the password is not placed in curl process arguments:

```sh
BILLWATCH_SUBSCRIPTION_SMOKE_EMAIL='unpaid-beta@example.com' \
BILLWATCH_SUBSCRIPTION_SMOKE_PASSWORD_FILE='/root/billwatch-subscription-smoke.password' \
BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE=false \
BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID=false \
  sh deploy/smoke-subscription-lifecycle.sh https://api.billwatch.example
```

Run the same read-only proof against a controlled paid tester with `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_ACTIVE=true` and `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PAID=true`. When a known provider state is part of the acceptance case, `BILLWATCH_SUBSCRIPTION_SMOKE_EXPECT_PROVIDER_STATUS` can require the exact state returned by BillWatch.

Stripe mutation-bearing probes remain independently disabled. Enable only the exact controlled action being proved:

```sh
BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_CHECKOUT=true
BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_PORTAL=true
BILLWATCH_SUBSCRIPTION_SMOKE_ALLOW_SYNC=true
```

Checkout-session creation requires a redirect on `https://checkout.stripe.com/`; Customer Portal creation requires `https://billing.stripe.com/`. The harness does not follow those redirects. Enabling checkout creates a Stripe Checkout Session but does not complete payment; an operator must complete the controlled purchase in Stripe's hosted UI. Provider sync can then be enabled to verify BillWatch's local entitlement state after the webhook/provider state exists.

For cancellation/expiration proof, perform the controlled change in Stripe, allow the signed webhook to arrive, then rerun the read-only status expectation. Do not treat session creation by itself as proof of payment, webhook delivery, cancellation, expiration, or entitlement enforcement.

Passing preflight and lifecycle smoke is still only an evidence checkpoint. The actual enforcement change remains a separate reviewed production action with rollback planning. Do not broaden the cohort and enable enforcement in the same unverified step.
