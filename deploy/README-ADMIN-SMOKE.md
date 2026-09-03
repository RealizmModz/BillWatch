# Admin rollout smoke sequence

Use this only after the role-aware API release has passed build/tests/CI and has been guarded-deployed.

1. Verify host prerequisites:

```sh
cd /opt/billwatch
sh deploy/verify-beta-admin.sh /opt/billwatch
```

2. Sign out of the Web application and sign back in. Role membership is evaluated on the fresh API bearer principal.

3. Verify the Owner/Admin API policy without printing data:

```sh
sh deploy/smoke-admin-api.sh https://api.billbeacon.net
```

4. Open `/app/admin` in the browser.

5. Create one short-lived single-redemption beta access key from the Admin console. Plaintext should appear once only.

6. Redeem it through `/app/subscription` using the intended test account.

7. Verify the entitlement state changes as expected.

8. Revoke or retire temporary test material when the smoke test is finished.

Do not enable global subscription enforcement as part of this sequence.
