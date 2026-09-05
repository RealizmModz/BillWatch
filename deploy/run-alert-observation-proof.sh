#!/bin/sh

set -eu

umask 077

phase=${1:-}
deployment_directory=${2:-}
pending_file=${BILLWATCH_ALERT_PROOF_PENDING_FILE:-}
evidence_file=${BILLWATCH_ALERT_PROOF_EVIDENCE_FILE:-}
confirm_phrase='I observed both BillWatch alert proof messages'

fail()
{
    printf '%s\n' "Alert observation proof refused: $1" >&2
    exit "${2:-64}"
}

[ "$phase" = send ] || [ "$phase" = confirm ] || fail "usage: $0 <send|confirm> <deployment-directory>"
[ -n "$deployment_directory" ] || fail "deployment directory is required."
[ -d "$deployment_directory" ] || fail "deployment directory does not exist: $deployment_directory" 66
deployment_directory=$(cd "$deployment_directory" && pwd -P)

check_external_path()
{
    path=$1
    label=$2
    [ -n "$path" ] || fail "$label path is required."
    case "$path" in
        /*) ;;
        *) fail "$label path must be absolute." ;;
    esac
    case "$path" in
        "$deployment_directory"|"$deployment_directory"/*) fail "$label must live outside the deployment checkout." ;;
    esac
    [ ! -L "$path" ] || fail "$label must not be a symbolic link." 73
    parent=$(dirname "$path")
    [ -d "$parent" ] || fail "$label directory does not exist: $parent" 66
}

check_external_path "$pending_file" "pending proof"
check_external_path "$evidence_file" "alert proof evidence"
[ "$pending_file" != "$evidence_file" ] || fail "pending and evidence paths must be different."
[ ! -L "$deployment_directory/.billwatch-release" ] || fail "verified release marker must not be a symbolic link." 73
[ -f "$deployment_directory/.billwatch-release" ] || fail "verified release marker is missing." 66
release_sha=$(cat "$deployment_directory/.billwatch-release")
printf '%s\n' "$release_sha" | grep -Eq '^[0-9a-f]{40}$' || fail "verified release marker must contain one full lowercase Git SHA." 65
head_sha=$(git -C "$deployment_directory" rev-parse HEAD)
[ "$head_sha" = "$release_sha" ] || fail "deployment checkout HEAD does not match the verified release marker." 65
[ -z "$(git -C "$deployment_directory" status --porcelain --untracked-files=no)" ] || fail "deployment checkout has tracked modifications." 65

read_value()
{
    file=$1
    key=$2
    count=$(grep -c "^${key}=" "$file" || true)
    [ "$count" = 1 ] || fail "pending proof must contain exactly one $key field." 65
    sed -n "s/^${key}=//p" "$file"
}

if [ "$phase" = send ]; then
    [ "${BILLWATCH_ALERT_PROOF_ALLOW_SEND:-false}" = true ] || fail "set BILLWATCH_ALERT_PROOF_ALLOW_SEND=true to send proof alerts." 77
    [ ! -e "$pending_file" ] || fail "pending proof already exists; refusing to overwrite it." 73
    [ ! -e "$evidence_file" ] || fail "alert proof evidence already exists; refusing to overwrite it." 73

    environment_file="$deployment_directory/.env.production"
    [ -f "$environment_file" ] || fail ".env.production is required for operations alert delivery." 66
    [ ! -L "$environment_file" ] || fail ".env.production must not be a symbolic link." 73
    [ "$(stat -c '%u' "$environment_file")" -eq "$(id -u)" ] || fail ".env.production must be owned by the deployment account." 77
    [ "$(( $(stat -c '%a' "$environment_file") % 100 ))" -eq 0 ] || fail ".env.production must be inaccessible to group/other users." 77

    operations_webhook=$(awk -F= '$1 == "BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL" { count++; value=substr($0, index($0, "=") + 1) } END { if (count == 1) print value }' "$environment_file")
    [ -n "$operations_webhook" ] || fail "exactly one operations alert webhook must be configured." 78
    case "${BILLWATCH_READINESS_ALERT_WEBHOOK_URL:-}" in
        https://*) ;;
        *) fail "BILLWATCH_READINESS_ALERT_WEBHOOK_URL must be an HTTPS URL." 78 ;;
    esac
    [ "$operations_webhook" != "$BILLWATCH_READINESS_ALERT_WEBHOOK_URL" ] || fail "operations and external readiness proofs must use independent webhook destinations." 78

    operations_sender="$deployment_directory/deploy/send-operations-alert.sh"
    readiness_sender="$deployment_directory/deploy/send-readiness-alert.sh"
    [ -f "$operations_sender" ] || fail "operations alert sender is missing." 66
    [ -f "$readiness_sender" ] || fail "external readiness alert sender is missing." 66

    challenge=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
    printf '%s\n' "$challenge" | grep -Eq '^[0-9a-f]{32}$' || fail "could not generate a valid alert proof challenge." 70

    sh "$operations_sender" "$deployment_directory" private-beta-alert-proof "$challenge"
    BILLWATCH_READINESS_ALERT_WEBHOOK_URL="$BILLWATCH_READINESS_ALERT_WEBHOOK_URL" \
        sh "$readiness_sender" private-beta-alert-proof "$challenge" "$release_sha"

    temporary=$(mktemp "${pending_file}.tmp.XXXXXX")
    trap 'rm -f "${temporary:-}"' EXIT HUP INT TERM
    {
        printf 'VERSION=1\n'
        printf 'RESULT=pending-observation\n'
        printf 'RELEASE_SHA=%s\n' "$release_sha"
        printf 'CHALLENGE=%s\n' "$challenge"
        printf 'SENT_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
        printf 'SENT_PHASES=operations-alert,external-readiness-alert\n'
    } > "$temporary"
    chmod 600 "$temporary"
    ln "$temporary" "$pending_file" || fail "could not publish pending proof without overwriting an existing file." 73
    rm -f "$temporary"
    trap - EXIT HUP INT TERM

    printf 'Alert proof probes sent. Confirm only after observing challenge %s in both independent destinations.\n' "$challenge"
    exit 0
fi

[ -f "$pending_file" ] || fail "pending proof does not exist; run the send phase first." 66
[ ! -L "$pending_file" ] || fail "pending proof must not be a symbolic link." 73
[ "$(stat -c '%a' "$pending_file" 2>/dev/null || true)" = 600 ] || fail "pending proof must have mode 600." 77
[ ! -e "$evidence_file" ] || fail "alert proof evidence already exists; refusing to overwrite it." 73
[ "$(read_value "$pending_file" VERSION)" = 1 ] || fail "pending proof has an unsupported version." 65
[ "$(read_value "$pending_file" RESULT)" = pending-observation ] || fail "pending proof is not awaiting observation." 65
[ "$(read_value "$pending_file" RELEASE_SHA)" = "$release_sha" ] || fail "pending proof belongs to a different release." 65
[ "$(read_value "$pending_file" SENT_PHASES)" = 'operations-alert,external-readiness-alert' ] || fail "pending proof does not contain both required sent phases." 65
challenge=$(read_value "$pending_file" CHALLENGE)
printf '%s\n' "$challenge" | grep -Eq '^[0-9a-f]{32}$' || fail "pending proof challenge is malformed." 65
[ "${BILLWATCH_ALERT_PROOF_CHALLENGE:-}" = "$challenge" ] || fail "BILLWATCH_ALERT_PROOF_CHALLENGE must exactly match the observed challenge." 77
[ "${BILLWATCH_ALERT_PROOF_CONFIRMATION:-}" = "$confirm_phrase" ] || fail "set BILLWATCH_ALERT_PROOF_CONFIRMATION to the exact observation confirmation phrase." 77

temporary=$(mktemp "${evidence_file}.tmp.XXXXXX")
trap 'rm -f "${temporary:-}"' EXIT HUP INT TERM
{
    printf 'VERSION=1\n'
    printf 'RESULT=complete\n'
    printf 'RELEASE_SHA=%s\n' "$release_sha"
    printf 'COMPLETED_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    printf 'PASSED_PHASES=operations-alert-observed,external-readiness-alert-observed\n'
} > "$temporary"
chmod 600 "$temporary"
ln "$temporary" "$evidence_file" || fail "could not publish alert proof evidence without overwriting an existing file." 73
rm -f "$temporary"
trap - EXIT HUP INT TERM
rm -f "$pending_file"

printf 'Release-pinned operator-observed alert evidence recorded for %s.\n' "$release_sha"
printf '%s\n' 'This evidence records an explicit human observation attestation; it does not independently prove downstream notification delivery semantics.'
