# BillWatch first Owner bootstrap

The first production Owner is established out-of-band exactly once.

Use `deploy/bootstrap-owner.sh` only when all of the following are true:

- the deployment has exactly one BillWatch user;
- the database contains exactly one `Owner` role;
- no user currently holds the `Owner` role;
- the operator knows the email address of the sole account.

The script fails closed if any of those assumptions are false.

Run it from the deployment account that owns `.env.production`:

```sh
cd /opt/billwatch
sh deploy/bootstrap-owner.sh /opt/billwatch 'owner@example.com'
```

Do not put passwords, bearer tokens, Plaid credentials, database passwords, or access keys on this command line.

After a successful bootstrap, sign out of the Web application and sign back in so a fresh API bearer principal is issued with the Owner role claim.

The normal administration API intentionally cannot assign or remove the Owner role.
