# BillWatch production smoke tests

These scripts intentionally avoid accepting passwords, bearer tokens, Plaid credentials, database passwords, or access keys as command-line arguments.

## General authenticated API smoke test

```sh
cd /opt/billwatch
sh deploy/smoke-authenticated-api.sh https://api.billbeacon.net
```

The script prompts for the account password with terminal echo disabled and deletes its temporary authentication material before exiting.

It verifies authenticated read access to the core user-owned API surfaces without printing response bodies.

## Owner/Admin authorization smoke test

After the first Owner exists and the role-aware API release is deployed:

```sh
cd /opt/billwatch
sh deploy/smoke-admin-api.sh https://api.billbeacon.net
```

A successful result proves a fresh bearer session can satisfy the `AdminOrOwner` policy.

This is an authorization smoke test only. It does not create/revoke access keys or mutate user roles.

## Browser verification still required

Command-line smoke tests do not replace browser verification of:

- login/logout;
- Overview/Bills/Activity/Account navigation;
- Subscription page;
- Admin console;
- Plaid Hosted Link and update mode;
- statement upload;
- downloads;
- responsive/dark/light presentation.
