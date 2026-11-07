# Android UI Implementation Plan

> Execute inline, retaining the current shared workspace and all pre-existing pairing/release edits.

**Goal:** Implement the approved Android visual hierarchy and glass-style interface without changing communication or update trust.
**Architecture:** UiPresentation contains pure layout/status/progress rules. BridgeUi owns reusable native View builders, GlassDrawable owns painted surfaces, and BridgeDialogs owns app-only sheets. MainActivity keeps all existing service and confirmation callbacks.
**Tech Stack:** Existing Kotlin/Android Views, minSdk 29, existing Gradle wrapper; no new dependencies.

### 1. Presentation rules
- [x] Write and run UiPresentationTest first; expect missing presentation-model assertions to fail.
- [x] Implement overflow-safe percentage clamping, transfer grouping, update-kind classification, byte formatting and narrow/large-text action stacking.
- [x] Run the targeted JVM test and confirm success.

### 2. Shared visuals and sheets
- [x] Implement GlassDrawable and replace BridgeUi card/button/icon/navigation builders. Keep interactive minimums >=48dp and use wrapping labels.
- [x] Implement BridgeDialogs with scrollable content, bounded height, keyboard-resize support and existing confirmation callback signatures.

### 3. Page hierarchy
- [x] Put current target and sending actions above discovery; preserve pending pairing and QR flows.
- [x] Use tested transfer groups and honest counter-based progress.
- [x] Group preferences/update/device management and fold advanced IP/legacy controls.
- [x] Retain navigation controls across background progress refresh; retain page scroll, filter and advanced expansion across recreation.
- [x] Replace only app AlertDialogs with the shared sheet builder.

### 4. Verification
- [x] Run gradlew.bat testDebugUnitTest lintDebug assembleDebug --offline --no-daemon --max-workers 2 --console plain using JDK 17 and the existing ASCII workspace alias.
- [x] Review lint findings, source differences and APK metadata. Record test counts and actual output path.
- [x] Confirm signing/update/network sources and version metadata are untouched. Do not run UI/emulator/install or release commands.

## Verified result

- Final offline Gradle testDebugUnitTest/lintDebug/assembleDebug run succeeded using JDK 17 through the pre-existing workspace-current ASCII junction. No dependency or signing configuration changes.
- 28 JUnit cases: 26 passed, 2 cross-platform interop cases skipped because the counterpart test servers were not configured; 0 failures/errors. The 9 UI presentation cases passed.
- Static mobile color tests: 2 passed, including both primary-gradient stops and secondary text surfaces.
- Lint: 0 errors; 1 pre-existing scanner-orientation warning. Removed the newly introduced draw-allocation warnings.
- Debug APK metadata, signature and copied file hash verified. Debug certificate differs from the pinned release certificate; not a publishable package or direct replacement for the formal app.
- No emulator, UI smoke test, Android app, camera, installer, clipboard action, real network transfer or release was run. Actual physical-device layout, IME and accessibility behavior remain unverified.
