# Subscription enforcement rollout gate

BillWatch private beta keeps `BILLWATCH_SUBSCRIPTION_ENFORCEMENT_ENABLED=false` by default. The preflight in this directory is intentionally non-mutating: it proves configuration prerequisites while enforcement is still off. It does not enable billing enforcement, restart services, change Stripe objects, or claim that webhook delivery has been observed in production.

Run it only against the protected production deployment after Stripe products/prices, Customer Portal, webhook delivery, and the intended rollout cohort have been verified:

```sh
BILLWATCH_SUBSCRIPTION_ROLLOUT_PREFLIGHT=true \
  sh deploy/verify-subscription-rollout-preflight.sh /opt/billwatch
```

The production `.env.production` must be a non-symlink mode-600 file. Preflight requires Stripe to be enabled, requires non-placeholder Stripe secret/webhook/price identifiers with expected prefixes, requires subscription enforcement to remain disabled, and requires the configured cohort to equal `InternalTester` unless an operator deliberately supplies `BILLWATCH_SUBSCRIPTION_ROLLOUT_EXPECTED_COHORT`.

Passing this preflight is only a configuration checkpoint. Before any real enforcement change, use controlled paid/unpaid tester accounts to prove checkout, webhook-driven entitlement updates, Customer Portal access, cancellation/expiration behavior, and the expected allow/deny experience. Then make the enforcement change as a separate reviewed production action with rollback planning. Do not broaden the cohort and enable enforcement in the same unverified step.
