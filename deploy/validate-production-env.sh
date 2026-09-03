#!/bin/sh

set -eu

umask 077

fail()
{
    printf '%s\n' "Production configuration invalid: $1" >&2
    exit 64
}

env_file=${1:-.env.production}

[ -f "$env_file" ] || fail "environment file is missing."
[ ! -L "$env_file" ] || fail "environment file must not be a symbolic link."

owner_id=$(stat -c '%u' "$env_file") || fail "environment file ownership cannot be read."
mode=$(stat -c '%a' "$env_file") || fail "environment file permissions cannot be read."

[ "$owner_id" = "$(id -u)" ] || fail "environment file must be owned by the deployment account."

case "$mode" in
    ?00|??00) ;;
    *) fail "environment file must not grant any permissions to group/other users." ;;
esac

if grep -q "$(printf '\r')" "$env_file"; then
    fail "environment file must use Unix line endings."
fi

invalid_line=$(awk '
    /^[[:space:]]*$/ { next }
    /^[[:space:]]*#/ { next }
    /^[A-Z][A-Z0-9_]*=[^\r\n]*$/ { next }
    { print NR; exit }
' "$env_file")

[ -z "$invalid_line" ] || fail "line $invalid_line is not a KEY=value entry."

read_value()
{
    key=$1
    count=$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$env_file")
    [ "$count" -eq 1 ] || fail "$key must appear exactly once."
    awk -v prefix="$key=" 'index($0, prefix) == 1 { print substr($0, length(prefix) + 1); exit }' "$env_file"
}

read_optional_value()
{
    key=$1
    count=$(awk -F= -v key="$key" '$1 == key { count++ } END { print count + 0 }' "$env_file")
    [ "$count" -le 1 ] || fail "$key must not appear more than once."

    if [ "$count" -eq 0 ]; then
        printf '%s' ""
        return
    fi

    awk -v prefix="$key=" 'index($0, prefix) == 1 { print substr($0, length(prefix) + 1); exit }' "$env_file"
}

reject_placeholder()
{
    key=$1
    value=$2

    [ -n "$value" ] || fail "$key is empty."

    case "$value" in
        *replace-with*|*example.com*|*placeholder*|*change-me*|*changeme*)
            fail "$key still contains an example or placeholder value."
            ;;
    esac
}

reject_unsafe_env_value()
{
    key=$1
    value=$2

    case "$value" in
        *[!A-Za-z0-9._~!@%+=,:/-]*)
            fail "$key contains whitespace, quoting, interpolation, or unsupported characters."
            ;;
    esac
}

validate_public_hostname()
{
    host_key=$1
    candidate_host=$2

    case "$candidate_host" in
        localhost|*.localhost|*.local|*.internal|*[!A-Za-z0-9.-]*|.*|*..*|*.)
            fail "$host_key must be a public DNS hostname."
            ;;
    esac

    case "$candidate_host" in
        *.*) ;;
        *) fail "$host_key must contain a public DNS suffix." ;;
    esac

    case "$candidate_host" in
        *[!0-9.]*) ;;
        *) fail "$host_key must not be a numeric address." ;;
    esac
}

host=$(read_value BILLWATCH_HOST)
web_host=$(read_value BILLWATCH_WEB_HOST)
release_id=$(read_value BILLWATCH_RELEASE_ID)
acme_email=$(read_value ACME_EMAIL)
database_password=$(read_value BILLWATCH_DATABASE_PASSWORD)
plaid_client_id=$(read_value PLAID_CLIENT_ID)
plaid_secret=$(read_value PLAID_SECRET)
plaid_environment=$(read_value PLAID_ENVIRONMENT)
restic_repository=$(read_value RESTIC_REPOSITORY)
restic_password=$(read_value RESTIC_PASSWORD)
backup_work_size=$(read_value BILLWATCH_BACKUP_WORK_SIZE)

for required_pair in \
    "BILLWATCH_HOST:$host" \
    "BILLWATCH_WEB_HOST:$web_host" \
    "BILLWATCH_RELEASE_ID:$release_id" \
    "ACME_EMAIL:$acme_email" \
    "BILLWATCH_DATABASE_PASSWORD:$database_password" \
    "PLAID_CLIENT_ID:$plaid_client_id" \
    "PLAID_SECRET:$plaid_secret" \
    "RESTIC_REPOSITORY:$restic_repository" \
    "RESTIC_PASSWORD:$restic_password"
do
    reject_placeholder "${required_pair%%:*}" "${required_pair#*:}"
    reject_unsafe_env_value "${required_pair%%:*}" "${required_pair#*:}"
done

validate_public_hostname BILLWATCH_HOST "$host"
validate_public_hostname BILLWATCH_WEB_HOST "$web_host"

[ "$host" != "$web_host" ] ||
    fail "BILLWATCH_HOST and BILLWATCH_WEB_HOST must be different hostnames."

case "$release_id" in
    *[!0-9a-f]*|'')
        fail "BILLWATCH_RELEASE_ID must be a lowercase 40-character Git commit."
        ;;
esac

[ "${#release_id}" -eq 40 ] ||
    fail "BILLWATCH_RELEASE_ID must be a lowercase 40-character Git commit."

case "$acme_email" in
    *@*.*) ;;
    *)
        fail "ACME_EMAIL must be a valid operational email address."
        ;;
esac

[ "${#database_password}" -ge 32 ] ||
    fail "BILLWATCH_DATABASE_PASSWORD must contain at least 32 characters."

[ "${#restic_password}" -ge 24 ] ||
    fail "RESTIC_PASSWORD must contain at least 24 characters."

case "$plaid_environment" in
    sandbox|production) ;;
    *)
        fail "PLAID_ENVIRONMENT must be sandbox or production."
        ;;
esac

identity_email_enabled=$(read_optional_value BILLWATCH_IDENTITY_EMAIL_ENABLED)
[ -n "$identity_email_enabled" ] || identity_email_enabled=false

case "$identity_email_enabled" in
    true|false) ;;
    *)
        fail "BILLWATCH_IDENTITY_EMAIL_ENABLED must be true or false when present."
        ;;
esac

if [ "$identity_email_enabled" = "true" ]; then
    resend_api_key=$(read_optional_value RESEND_API_KEY)
    identity_from_address=$(read_optional_value BILLWATCH_IDENTITY_EMAIL_FROM_ADDRESS)
    identity_from_name=$(read_optional_value BILLWATCH_IDENTITY_EMAIL_FROM_NAME)

    reject_placeholder RESEND_API_KEY "$resend_api_key"
    reject_placeholder BILLWATCH_IDENTITY_EMAIL_FROM_ADDRESS "$identity_from_address"
    reject_placeholder BILLWATCH_IDENTITY_EMAIL_FROM_NAME "$identity_from_name"

    reject_unsafe_env_value RESEND_API_KEY "$resend_api_key"
    reject_unsafe_env_value BILLWATCH_IDENTITY_EMAIL_FROM_ADDRESS "$identity_from_address"
    reject_unsafe_env_value BILLWATCH_IDENTITY_EMAIL_FROM_NAME "$identity_from_name"

    case "$identity_from_address" in
        *@*.*) ;;
        *)
            fail "BILLWATCH_IDENTITY_EMAIL_FROM_ADDRESS must be a valid sender email address."
            ;;
    esac
fi

case "$restic_repository" in
    /*|./*|../*|[A-Za-z]:\\*|file:*|local:*)
        fail "RESTIC_REPOSITORY must be an off-host repository."
        ;;
    *:*) ;;
    *)
        fail "RESTIC_REPOSITORY must use an explicit remote backend."
        ;;
esac

printf '%s' "$backup_work_size" |
    grep -Eq '^[1-9][0-9]*[mMgG]$' ||
    fail "BILLWATCH_BACKUP_WORK_SIZE must be a positive value such as 8g."

case "$restic_repository" in
    s3:*)
        aws_access_key=$(read_value AWS_ACCESS_KEY_ID)
        aws_secret_key=$(read_value AWS_SECRET_ACCESS_KEY)
        aws_region=$(read_value AWS_DEFAULT_REGION)

        reject_placeholder AWS_ACCESS_KEY_ID "$aws_access_key"
        reject_placeholder AWS_SECRET_ACCESS_KEY "$aws_secret_key"
        reject_placeholder AWS_DEFAULT_REGION "$aws_region"

        reject_unsafe_env_value AWS_ACCESS_KEY_ID "$aws_access_key"
        reject_unsafe_env_value AWS_SECRET_ACCESS_KEY "$aws_secret_key"
        reject_unsafe_env_value AWS_DEFAULT_REGION "$aws_region"
        ;;
esac

printf '%s\n' \
    "Production configuration preflight passed for API $host and web $web_host at release $release_id."
