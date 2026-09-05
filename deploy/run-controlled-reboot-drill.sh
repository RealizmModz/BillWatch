#!/bin/sh

set -eu

umask 077

phase="${1:-}"
deployment_directory="${2:-}"
state_file="${BILLWATCH_REBOOT_DRILL_STATE_FILE:-/var/lib/billwatch/reboot-drill.state}"
allow_drill="${BILLWATCH_REBOOT_DRILL_ALLOW:-false}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

usage()
{
    printf '%s\n' "Usage: BILLWATCH_REBOOT_DRILL_ALLOW=true $0 <preflight|postflight> <deployment-directory>" >&2
    exit 64
}

[ "$phase" = "preflight" ] || [ "$phase" = "postflight" ] || usage
[ -n "$deployment_directory" ] || usage
[ "$allow_drill" = "true" ] || fail "Controlled reboot drill requires explicit BILLWATCH_REBOOT_DRILL_ALLOW=true opt-in." 77
[ -d "$deployment_directory" ] || fail "Deployment directory does not exist: $deployment_directory" 66

deployment_directory="$(cd "$deployment_directory" && pwd -P)"

case "$state_file" in
    /*) ;;
    *) fail "BILLWATCH_REBOOT_DRILL_STATE_FILE must be an absolute path outside the deployment checkout." 64 ;;
esac

case "$state_file" in
    "$deployment_directory"|"$deployment_directory"/*)
        fail "Reboot-drill state must live outside the deployment checkout." 64
        ;;
esac

command -v git >/dev/null 2>&1 || fail "git is required for the controlled reboot drill." 69
command -v systemctl >/dev/null 2>&1 || fail "systemctl is required for the controlled reboot drill." 69
command -v stat >/dev/null 2>&1 || fail "stat is required for the controlled reboot drill." 69
command -v sed >/dev/null 2>&1 || fail "sed is required for the controlled reboot drill." 69

release_file="$deployment_directory/.billwatch-release"
[ -f "$release_file" ] || fail "Verified release marker is missing: $release_file" 66

read_release()
{
    release="$(cat "$release_file")"
    printf '%s' "$release"
}

read_boot_id()
{
    boot_id="$(cat /proc/sys/kernel/random/boot_id)"
    printf '%s' "$boot_id"
}

validate_release()
{
    value="$1"
    printf '%s\n' "$value" | grep -Eq '^[0-9a-f]{40}$' ||
        fail "Verified release marker must contain one full lowercase Git SHA." 65
}

validate_boot_id()
{
    value="$1"
    printf '%s\n' "$value" | grep -Eq '^[0-9a-f-]{32,64}$' ||
        fail "Host boot ID is missing or malformed." 65
}

verify_pinned_release()
{
    expected_release="$1"
    current_release="$(read_release)"
    validate_release "$current_release"
    [ "$current_release" = "$expected_release" ] ||
        fail "Verified release marker changed during the controlled reboot drill." 65

    current_head="$(git -C "$deployment_directory" rev-parse HEAD)"
    [ "$current_head" = "$expected_release" ] ||
        fail "Deployment checkout HEAD does not match the verified release marker." 65
}

verify_host_prerequisites()
{
    systemctl is-enabled --quiet docker ||
        fail "Docker is not enabled to return automatically after reboot." 69
    systemctl is-active --quiet docker ||
        fail "Docker is not active." 69

    sh "$deployment_directory/deploy/verify-beta-readiness.sh" "$deployment_directory" ||
        fail "BillWatch private-beta readiness verification failed." 1
}

state_value()
{
    key="$1"
    count="$(grep -c "^${key}=" "$state_file" || true)"
    [ "$count" = "1" ] || fail "Reboot-drill state is missing or duplicates $key." 65
    sed -n "s/^${key}=//p" "$state_file"
}

if [ "$phase" = "preflight" ]; then
    [ ! -L "$state_file" ] || fail "Refusing symlinked reboot-drill state path." 73
    [ ! -e "$state_file" ] ||
        fail "A reboot-drill state file already exists. Complete or intentionally remove the prior drill before starting another." 73

    release="$(read_release)"
    validate_release "$release"
    head_sha="$(git -C "$deployment_directory" rev-parse HEAD)"
    [ "$head_sha" = "$release" ] ||
        fail "Deployment checkout HEAD does not match the verified release marker." 65

    boot_id="$(read_boot_id)"
    validate_boot_id "$boot_id"

    verify_host_prerequisites

    state_directory="$(dirname "$state_file")"
    if [ ! -d "$state_directory" ]; then
        mkdir -p "$state_directory"
        chmod 700 "$state_directory"
    fi
    [ -d "$state_directory" ] || fail "Reboot-drill state directory is unavailable." 73

    temporary_state="$(mktemp "${state_file}.tmp.XXXXXX")"
    trap 'rm -f "${temporary_state:-}"' EXIT HUP INT TERM
    {
        printf 'VERSION=1\n'
        printf 'DEPLOYMENT_DIRECTORY=%s\n' "$deployment_directory"
        printf 'RELEASE_SHA=%s\n' "$release"
        printf 'BOOT_ID=%s\n' "$boot_id"
        printf 'RECORDED_AT_UTC=%s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
    } > "$temporary_state"
    chmod 600 "$temporary_state"
    mv "$temporary_state" "$state_file"
    trap - EXIT HUP INT TERM

    printf '%s\n' "BillWatch reboot-drill preflight passed for release $release."
    printf '%s\n' "Perform the controlled host reboot manually. Do not deploy, modify the release marker, or replace the checkout between phases."
    printf '%s\n' "After the host returns, run the postflight phase against the same deployment directory."
    exit 0
fi

[ ! -L "$state_file" ] || fail "Refusing symlinked reboot-drill state path." 73
[ -f "$state_file" ] || fail "Reboot-drill preflight state is missing." 66
[ "$(stat -c '%a' "$state_file")" = "600" ] || fail "Reboot-drill state file must be mode 600." 77

version="$(state_value VERSION)"
recorded_directory="$(state_value DEPLOYMENT_DIRECTORY)"
recorded_release="$(state_value RELEASE_SHA)"
recorded_boot_id="$(state_value BOOT_ID)"
recorded_at="$(state_value RECORDED_AT_UTC)"

[ "$version" = "1" ] || fail "Unsupported reboot-drill state version." 65
[ "$recorded_directory" = "$deployment_directory" ] ||
    fail "Postflight deployment directory differs from the preflight checkout." 65
validate_release "$recorded_release"
validate_boot_id "$recorded_boot_id"
printf '%s\n' "$recorded_at" | grep -Eq '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' ||
    fail "Reboot-drill timestamp is malformed." 65

current_boot_id="$(read_boot_id)"
validate_boot_id "$current_boot_id"
[ "$current_boot_id" != "$recorded_boot_id" ] ||
    fail "Host boot ID did not change; a real reboot has not been proven." 65

verify_pinned_release "$recorded_release"
verify_host_prerequisites

rm -f "$state_file"
printf '%s\n' "BillWatch controlled reboot recovery passed for unchanged release $recorded_release."
printf '%s\n' "Docker returned automatically and the complete private-beta host prerequisite gate passed after a distinct host boot."
