#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
workflow="$root_dir/.github/workflows/ios-internal-simulator.yml"
smoke_script="$root_dir/deploy/tests/ios-simulator-smoke.sh"
docs="$root_dir/IOS_INTERNAL_TESTING.md"

fail()
{
    printf '%s\n' "iOS internal simulator workflow regression failed: $1" >&2
    exit 1
}

[ -f "$workflow" ] ||
    fail "workflow is missing."
[ -f "$smoke_script" ] ||
    fail "iOS simulator smoke script is missing."
[ -f "$docs" ] ||
    fail "iOS internal testing documentation is missing."

grep -Fq 'name: FullWorth iOS Internal Simulator App' "$workflow" ||
    fail "workflow name changed unexpectedly."
grep -Fq 'workflow_dispatch:' "$workflow" ||
    fail "manual iOS build entry point is missing."
grep -Fq 'pull_request:' "$workflow" ||
    fail "pull-request iOS build trigger is missing."
grep -Fq -- '- master' "$workflow" ||
    fail "master push trigger is missing."
grep -Fq 'contents: read' "$workflow" ||
    fail "workflow repository permissions are not read-only."
grep -Fq 'runs-on: macos-15' "$workflow" ||
    fail "iOS build is not pinned to the macos-15 runner that carries the matching iOS 26.0 simulator runtime."
grep -Fq 'dotnet workload install maui-ios --skip-manifest-update' "$workflow" ||
    fail "MAUI iOS workload installation is missing."

grep -Fq 'Select Xcode 26.0 required by .NET iOS' "$workflow" ||
    fail "workflow does not explicitly select the Xcode version required by the .NET iOS workload."
grep -Fq 'xcode_app="/Applications/Xcode_26.0.1.app"' "$workflow" ||
    fail "workflow does not use the real Xcode 26.0.1 bundle path."
grep -Fq '26.0|26.0.*)' "$workflow" ||
    fail "workflow does not accept the Xcode 26.0 patch line required by the .NET iOS workload."
grep -Fq 'com.apple.CoreSimulator.SimRuntime.iOS-26-0' "$workflow" ||
    fail "workflow does not preflight the matching iOS 26.0 simulator runtime."
grep -Fq 'DEVELOPER_DIR=%s' "$workflow" ||
    fail "selected Xcode developer directory is not exported for later steps."
grep -Fq 'xcodebuild -version' "$workflow" ||
    fail "selected Xcode version is not verified."
grep -Fq 'RuntimeIdentifier=iossimulator-arm64' "$workflow" ||
    fail "workflow is not explicitly pinned to the arm64 simulator runtime."
grep -Fq 'FullWorthApiBaseUrl=https://api.fullworth.org/' "$workflow" ||
    fail "iOS simulator build is not pinned to the canonical HTTPS API."

grep -Fq 'dotnet restore FullWorth.Core/FullWorth.Core.csproj' "$workflow" ||
    fail "shared Core restore is missing after the iOS-targeted restore."

ios_restore_line=$(grep -Fn 'dotnet restore FullWorth.csproj' "$workflow" | head -n 1 | cut -d: -f1)
core_restore_line=$(grep -Fn 'dotnet restore FullWorth.Core/FullWorth.Core.csproj' "$workflow" | head -n 1 | cut -d: -f1)

[ -n "$ios_restore_line" ] ||
    fail "could not locate the iOS-targeted restore."
[ -n "$core_restore_line" ] ||
    fail "could not locate the shared Core restore."
[ "$ios_restore_line" -lt "$core_restore_line" ] ||
    fail "shared Core must be restored after the iOS-targeted restore so its net10.0 assets are not overwritten."

if grep -Fq 'iossimulator-x64' "$workflow"; then
    fail "workflow must not produce an x64 simulator app on the arm64 macOS runner."
fi

if grep -Fq 'mapfile ' "$workflow"; then
    fail "workflow must remain compatible with the macOS system Bash version."
fi
grep -Fq 'ios-simulator-smoke.sh' "$workflow" ||
    fail "workflow does not install and launch the built app in Simulator."
grep -Fq 'actions/upload-artifact@v7' "$workflow" ||
    fail "workflow does not publish a simulator artifact."
grep -Fq 'retention-days: 14' "$workflow" ||
    fail "iOS simulator artifact retention is not bounded."
grep -Fq 'shasum -a 256' "$workflow" ||
    fail "workflow does not publish an artifact integrity hash."
grep -Fq 'not signed for a physical iPhone' "$workflow" ||
    fail "workflow summary does not clearly distinguish simulator output from a device build."

if grep -Fq '${{ secrets.' "$workflow"; then
    fail "simulator-only workflow must not consume persistent GitHub secrets."
fi

if grep -Eiq '(codesign|provisioning|certificate|p12|mobileprovision).*(password|secret|base64)' "$workflow"; then
    fail "simulator workflow appears to contain signing-secret handling."
fi

grep -Fq 'com.apple.CoreSimulator.SimRuntime.iOS-26-0' "$smoke_script" ||
    fail "smoke is not pinned to the iOS 26.0 simulator runtime compatible with the selected Xcode."
grep -Fq 'xcrun simctl list devices --json' "$smoke_script" ||
    fail "smoke does not select a preinstalled iOS 26.0 iPhone Simulator."
grep -Fq 'xcrun simctl erase' "$smoke_script" ||
    fail "smoke does not reset the selected Simulator before launch."
grep -Fq 'xcrun simctl bootstatus "$udid" -b' "$smoke_script" ||
    fail "smoke does not wait for Simulator boot completion."
grep -Fq 'run_with_timeout 180' "$smoke_script" ||
    fail "Simulator boot wait is not bounded."
grep -Fq 'run_with_timeout 90' "$smoke_script" ||
    fail "Simulator installation is not bounded."
grep -Fq 'run_with_timeout 60' "$smoke_script" ||
    fail "Simulator reset or app launch is not bounded."
grep -Fq 'xcrun simctl install' "$smoke_script" ||
    fail "smoke does not install FullWorth."
grep -Fq 'xcrun simctl launch' "$smoke_script" ||
    fail "smoke does not launch FullWorth."
grep -Fq 'xcrun simctl get_app_container' "$smoke_script" ||
    fail "smoke does not verify the launched app container."
grep -Fq 'xcrun simctl shutdown' "$smoke_script" ||
    fail "Simulator cleanup is missing."
grep -Fq 'subprocess.run(' "$smoke_script" ||
    fail "smoke does not use the bounded command wrapper."

sh -n "$smoke_script" ||
    fail "iOS simulator smoke script has invalid shell syntax."

grep -Fq 'cannot be installed on a physical iPhone' "$docs" ||
    fail "documentation does not state the simulator artifact boundary."
grep -Fq 'Issue #258' "$docs" ||
    fail "documentation does not keep the iOS PWA issue separate."

printf '%s\n' "iOS internal simulator workflow regression passed."
