# ClipBridge Liquid Glass Brand Implementation Plan

> **For agentic workers:** Execute inline in this task, one subsystem at a time. Keep each existing unrelated working-tree change intact; do not reset, stash, or commit unrelated files.

**Goal:** Complete the approved clear-liquid-glass visual language across the Windows updater/device surfaces and Android main flows, and make both launcher icons use the same refreshed folder-and-transfer mark.

**Architecture:** Preserve the existing WinForms and Android native UI implementations and their behavior; verify the in-progress glass surfaces against the approved design and make only focused visual gaps fixes. Generate the canonical brand mark once from a deterministic local renderer, package multiple sizes into the Windows ICO, and reuse its transparent PNG as the Android adaptive-icon foreground.

**Tech Stack:** .NET 10 WinForms, PowerShell/System.Drawing icon generation, Android Kotlin/Views, Android vector/adaptive icon XML, existing smoke tests and Gradle unit tests.

---

## Files and responsibilities

- `ClipBridge/Windows/UpdateDialog.cs`, `UpdateGlassControls.cs`, `UpdateGlassButton.cs`, `UpdateEmblem.cs`, and `UpdateReleaseNotes.cs`: complete Windows updater presentation and stage states. Existing behavior and progress source remain unchanged.
- `ClipBridge/Windows/ControlPanelForm.cs`, `Devices/DevicesForm.cs`, `BrandMark.cs`, `PairingQrForm.cs`, and `PairingQrPanel.cs`: Windows primary device and pairing surfaces. Existing in-progress glass work is preserved and verified.
- `android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt`, `GlassDrawable.kt`, `BridgeDialogs.kt`, `UiPresentation.kt`, and `MainActivity.kt`: Android native glass tokens and main/device/transfer/pairing/update surfaces.
- `tools/Generate-BrandIcon.ps1`: deterministic single-source drawing and output of a transparent 512px PNG plus a multi-size `ClipBridge.ico`.
- `ClipBridge.ico`: Windows executable, form and `BrandMark` resource.
- `android/app/src/main/res/drawable-nodpi/clipbridge_brand.png`, `android/app/src/main/res/mipmap-anydpi-v26/ic_clipbridge.xml`, `android/app/src/main/res/drawable/clipbridge_icon_background.xml`, and `android/app/src/main/AndroidManifest.xml`: Android adaptive icon using the exact generated shared mark.
- `tools/Test-BrandIcon.ps1`: verifies the Windows ICO frames, Android adaptive-icon references, generated foreground and parity with the canonical output.
- `tools/UiSmokeTests/UpdateDialogSmoke.cs`, `DevicesFormSmoke.cs`, `PairingQrPanelSmoke.cs`, and `tools/UpdateSmokeTests`: behavior and visual regression coverage.
- `android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt` and `tools/test_android_ui_style.py`: mobile layout/style regression coverage.

## Task 1: Establish a clean baseline for the requested visual work

- [x] Record the current status and do not alter or restore any existing user changes.
- [x] Build the current Windows sources and run updater/device smoke coverage before touching UI or icon code:

```powershell
dotnet build .\ClipBridge.csproj --configuration Release
dotnet build .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --updates-only --capture
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --devices-only --capture
```

Expected: both builds exit 0; smoke output ends in `PASS` for all updater/device checks and screenshots are written under `%TEMP%\clipbridge-ui-smoke`.

- [x] Build and test Android UI sources without release signing secrets:

```powershell
.\android\gradlew.bat -p .\android :app:testDebugUnitTest
.\android\gradlew.bat -p .\android :app:processDebugResources
python .\tools\test_android_ui_style.py
```

Expected: Gradle unit tests and debug resource processing succeed. If the `python` launcher is unavailable, record that limitation and use the repository's configured Python runtime or run the equivalent assertions through PowerShell; never skip the UI/resource validation silently.

- [x] Inspect the current smoke screenshots at actual size. The approved clear glass surfaces, concise hierarchy, progress/stage clarity, visible failure/retry, light/dark support, and custom dialog styling are present.

## Task 2: Add a failing regression for the shared icon contract

**Files:** Create `tools/Test-BrandIcon.ps1`; current targets `ClipBridge.ico`, Android launcher resource XML and foreground asset.

- [x] Add a PowerShell test that asserts: the ICO can be opened by `System.Drawing.Icon`; it contains renderable 16, 32, 48, 64, 128 and 256 px representations; Android manifest `icon` and `roundIcon` both point to `@mipmap/ic_clipbridge`; the API-26 adaptive icon points to the generated foreground PNG and a blue-white glass gradient background; and the generated foreground is a valid transparent 512×512 PNG.
- [x] Run `pwsh -File .\\tools\\Test-BrandIcon.ps1` and confirm it initially failed because the current Android manifest references the old stand-alone drawable/vector and the shared ICO/PNG outputs are not present.

## Task 3: Generate and wire one cross-platform glass brand mark

**Files:** Create `tools/Generate-BrandIcon.ps1`; modify `ClipBridge.ico`; create `android/app/src/main/res/drawable-nodpi/clipbridge_brand.png`, `android/app/src/main/res/mipmap-anydpi-v26/ic_clipbridge.xml`, and `android/app/src/main/res/drawable/clipbridge_icon_background.xml`; modify `android/app/src/main/AndroidManifest.xml`.

- [x] Implement one deterministic `System.Drawing` renderer in `tools/Generate-BrandIcon.ps1`. Draw a transparent rounded-square glass tile with a soft blue-violet gradient, two folder silhouettes, and two opposing transfer arrows; use high-contrast cool blue/teal fills, rounded outlines and a restrained white top highlight. Export the exact 512×512 transparent PNG and embed PNG frames at 16, 24, 32, 48, 64, 128 and 256 px into `ClipBridge.ico`.
- [x] Run the generator and the contract test; repeated generator runs are byte-identical. The test must pass and repeated generator runs must produce byte-identical outputs.
- [x] Add the adaptive icon resource using this contract:

```xml
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@drawable/clipbridge_icon_background" />
    <foreground android:drawable="@drawable/clipbridge_brand" />
</adaptive-icon>
```

- [x] Declare both launcher references in the manifest:

```xml
android:icon="@mipmap/ic_clipbridge"
android:roundIcon="@mipmap/ic_clipbridge"
```

- [x] Re-run `pwsh -File .\\tools\\Test-BrandIcon.ps1`, Android debug resource processing and Windows release build. Expected: test and builds exit 0; the Android adaptive icon uses the generated PNG rather than the previous unrelated green transfer vector, which is removed after migration.
- [x] Render/check the icon against light and dark backgrounds at 16, 32, 48 and 128 px. Confirm both arrows and the folder silhouette remain recognizable and no tile/glow clips the adaptive safe zone.

## Task 4: Close any verified Windows glass gaps without changing behavior

**Files:** Only when a baseline check identifies a concrete mismatch, modify the owning presentation file listed above and add the assertion to `tools/UiSmokeTests/UpdateDialogSmoke.cs`, `DevicesFormSmoke.cs`, or `PairingQrPanelSmoke.cs`.

- [x] Keep theme color selection in `Ui`/`UiPalette`, glass drawing in existing custom surfaces, and progress behavior in `UpdateProgress`; do not add business logic to paint controls.
- [x] The existing updater/device smoke suites verify the approved design and interaction states in light/dark modes; no further Windows UI source change was needed.
- [x] Required states remain: ready, download progress, cancel request, recoverable failure/retry, protected install, confirmed completion, compact notice, device list, empty state, pairing QR, and light/dark theme switch.


## Task 5: Close any verified Android glass gaps without changing behavior

**Files:** Only when a baseline check identifies a concrete mismatch, modify `BridgeUi.kt`, `GlassDrawable.kt`, `BridgeDialogs.kt`, `UiPresentation.kt`, or `MainActivity.kt`, plus focused tests in `android/app/src/test/java/app/clipbridge/ui`.

- [x] Preserve native Android sizing, touch feedback, dark mode, back handling, accessibility labels, font scaling, narrow-screen stacking, and all device/transfer/update behavior.
- [x] Existing Android style checks cover the current glass tokens; the icon contract test covers the new launcher-resource boundary. No visual-token change was needed.
- [x] Run Android unit tests, debug resource processing, and the style checks after the change. Expected: all existing tests and new assertions pass in both light/dark tokens and icon-resource compilation.
- [x] The Android primary-screen and dialog implementation already meets the approved style checks, so its UI source remained unchanged.

## Task 6: Cross-project verification and scope audit

- [x] Run final focused commands:

```powershell
dotnet build .\ClipBridge.csproj --configuration Release
dotnet build .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release
dotnet build .\tools\UpdateSmokeTests\UpdateSmokeTests.csproj --configuration Release
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --updates-only --capture
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --devices-only --capture
pwsh -File .\tools\Test-BrandIcon.ps1
.\android\gradlew.bat -p .\android :app:testDebugUnitTest :app:processDebugResources
```

- [x] Run `python .\\tools\\test_android_ui_style.py` and `python .\tools\test_publish_github.py` if Python is available. The release script test is read-only/hosted-independent; do not invoke any publish command, release workflow, credential, keystore, private key or signing operation.
- [x] Confirm via `git diff --check` that the only new edits beyond the user's pre-existing working tree are the approved icon assets/generator/test and any directly evidenced visual corrections. Do not stage or commit code or pre-existing files as part of this task.
- [x] Report exactly which UI code changed versus what already satisfied the design, list tests that passed, and disclose any test not runnable in this environment.

