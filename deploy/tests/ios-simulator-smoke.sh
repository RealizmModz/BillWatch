#!/bin/sh

set -eu

app_path=${1:-}
expected_bundle_id=${2:-}

fail()
{
    printf '%s\n' "iOS simulator smoke failed: $1" >&2
    exit 1
}

run_with_timeout()
{
    timeout_seconds=$1
    shift

    python3 - "$timeout_seconds" "$@" <<'PY'
import subprocess
import sys

timeout_seconds = float(sys.argv[1])
command = sys.argv[2:]

try:
    completed = subprocess.run(
        command,
        timeout=timeout_seconds,
        check=False)
except subprocess.TimeoutExpired:
    sys.exit(124)

sys.exit(completed.returncode)
PY
}

[ -n "$app_path" ] ||
    fail "app path is required."
[ -n "$expected_bundle_id" ] ||
    fail "expected bundle ID is required."
[ -d "$app_path" ] ||
    fail "app bundle does not exist."

for command_name in xcrun python3
do
    command -v "$command_name" >/dev/null 2>&1 ||
        fail "$command_name is required."
done

info_plist="$app_path/Info.plist"
[ -f "$info_plist" ] ||
    fail "app Info.plist is missing."

actual_bundle_id=$(
    /usr/libexec/PlistBuddy \
        -c 'Print :CFBundleIdentifier' \
        "$info_plist"
)

[ "$actual_bundle_id" = "$expected_bundle_id" ] ||
    fail "built bundle ID does not match the expected identifier."

runtime_id=$(
    xcrun simctl list runtimes --json |
        python3 -c '
import json, sys
data=json.load(sys.stdin)
items=[
    runtime for runtime in data.get("runtimes", [])
    if runtime.get("isAvailable") and
       runtime.get("identifier", "").startswith(
           "com.apple.CoreSimulator.SimRuntime.iOS-26-0")
]
if not items:
    raise SystemExit(1)
print(items[0]["identifier"])
'
) || fail "no available iOS 26.0 Simulator runtime was found."

udid=$(
    xcrun simctl list devices --json |
        python3 -c '
import json, sys
runtime_id=sys.argv[1]
data=json.load(sys.stdin)
devices=[
    device for device in data.get("devices", {}).get(runtime_id, [])
    if device.get("isAvailable") and
       device.get("name", "").startswith("iPhone")
]
if not devices:
    raise SystemExit(1)
preferred=[
    device for device in devices
    if "Pro" in device.get("name", "")
]
choice=(preferred or devices)[0]
print(choice["udid"])
' "$runtime_id"
) || fail "no preinstalled iPhone Simulator is available for iOS 26.0."

cleanup()
{
    if [ -n "${udid:-}" ]; then
        xcrun simctl terminate \
            "$udid" \
            "$expected_bundle_id" >/dev/null 2>&1 ||
            true

        xcrun simctl shutdown "$udid" >/dev/null 2>&1 ||
            true
    fi
}
trap cleanup EXIT HUP INT TERM

xcrun simctl shutdown "$udid" >/dev/null 2>&1 ||
    true

run_with_timeout 60 \
    xcrun simctl erase "$udid" ||
    fail "could not reset the iOS 26.0 Simulator within 60 seconds."

xcrun simctl boot "$udid" ||
    fail "could not boot the iOS 26.0 Simulator."

run_with_timeout 180 \
    xcrun simctl bootstatus "$udid" -b ||
    fail "iOS 26.0 Simulator did not finish booting within 180 seconds."

run_with_timeout 90 \
    xcrun simctl install "$udid" "$app_path" ||
    fail "FullWorth could not be installed into the iOS Simulator within 90 seconds."

launch_output=$(
    run_with_timeout 60 \
        xcrun simctl launch "$udid" "$expected_bundle_id"
) || fail "FullWorth could not be launched in the iOS Simulator within 60 seconds."

printf '%s\n' "$launch_output" |
    grep -Fq "$expected_bundle_id:" ||
    fail "simctl launch did not return the expected FullWorth process identifier."

sleep 3

run_with_timeout 30 \
    xcrun simctl get_app_container \
        "$udid" \
        "$expected_bundle_id" \
        app >/dev/null ||
    fail "FullWorth app container was not available after launch."

printf '%s\n' \
    "FullWorth iOS simulator install/launch smoke passed for $expected_bundle_id."
