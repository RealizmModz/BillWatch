# FullWorth iOS internal testing

FullWorth still contains a transitional .NET MAUI iOS target while the authenticated Web/PWA becomes the primary client. The iOS internal workflow exists to keep that target buildable and testable without turning it back into the main product architecture.

## What CI produces

`.github/workflows/ios-internal-simulator.yml` builds the current `net10.0-ios` target on a GitHub-hosted macOS runner using:

- the canonical production API endpoint, `https://api.fullworth.org/`;
- an `iossimulator-arm64` runtime identifier;
- no Apple signing certificate, provisioning profile, or persistent signing secret.

The workflow selects a preinstalled iOS 26.0 iPhone Simulator on the ephemeral GitHub runner, erases it, boots it with a bounded wait, installs the exact app it built, launches FullWorth with a bounded wait, verifies the app container exists, then uploads a SHA-256-addressed ZIP artifact for 14 days.

The resulting ZIP is an iOS Simulator artifact. It **cannot be installed on a physical iPhone** and is not an IPA, TestFlight build, or App Store package.

## Why start here

The workflow is pinned to GitHub's `macos-15` Apple Silicon runner because that image carries the compatible Xcode 26.0.1 toolchain and an installed iOS 26.0 Simulator runtime. It uses the real `/Applications/Xcode_26.0.1.app` bundle path and preflights the iOS 26.0 runtime before restoring/building. The simulator build is pinned to `iossimulator-arm64`. The workflow also restores `FullWorth.Core` after the iOS-targeted restore so the shared project keeps its required `net10.0` asset target. A simulator build gives FullWorth an iOS compile/install/launch gate now, without asking for Apple signing credentials and without committing signing material to source control. It also catches iOS-specific MAUI regressions independently from Android.

Physical-device/TestFlight distribution is a later signing milestone. When that work starts, certificates and provisioning material must be supplied through protected GitHub/Apple mechanisms rather than committed to this repository or pasted into chat.

## PWA issue remains separate

Issue #258 tracks the production Safari **Add to Home Screen** failure. This simulator workflow does not claim to fix or close that issue. The Web/PWA path and the transitional MAUI iOS build are separate acceptance surfaces and must be tested independently.

## Manual workflow

From GitHub Actions, run **FullWorth iOS Internal Simulator App**. A successful run means:

1. the iOS target restored and compiled;
2. the generated FullWorth app bundle had a concrete bundle identifier;
3. a reset iOS 26.0 iPhone Simulator booted;
4. the app installed and launched;
5. the app container was present after launch;
6. the simulator ZIP and SHA-256 file were uploaded.

A green simulator run is build/runtime evidence only. It is not physical-iPhone, TestFlight, App Store, Plaid-provider, or production-PWA acceptance evidence.
