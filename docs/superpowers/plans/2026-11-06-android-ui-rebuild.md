# Android UI Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all Android screens with the approved light-glass visual system and a per-device conversation timeline while keeping Android-native share/copy flows and existing pairing, transfer, update, and storage behavior.

**Architecture:** Keep the current Kotlin Activity and Android Views; make the timeline the app's main destination and add focused adapters/row renderers backed by the history APIs from the peer-history plan. Use platform copy/share/open actions and a separate local-files entry; do not request an overlay permission or migrate to Compose/another UI framework.

**Tech Stack:** Kotlin, Android SDK 36 / minSdk 29, existing `BridgeUi`, `GlassDrawable`, `UiPresentation`, MediaStore Downloads, existing JUnit tests and Gradle lint/build tasks; no new UI framework dependency.

---

## Scope and ordering

Complete `docs/superpowers/plans/2026-11-06-peer-history-sync.md` before this plan. Use `docs/superpowers/specs/2026-11-06-cross-platform-lightweight-history-ui-design.md` as the acceptance source. Do not alter pairing cryptography, transfer integrity, update signature validation, release signing, or Android permissions. Follow `android/README.md`: do not launch the Android app, an emulator, an installer, or Android UI smoke tests during development. Use JVM presentation tests, lint, and debug assembly; report physical-device rendering and OS share/file-manager behavior as unverified.

## File map

- Create `android/app/src/main/java/app/clipbridge/ui/HistoryTimelineAdapter.kt`: recycled ListView adapter for per-device history rows.
- Create `android/app/src/main/java/app/clipbridge/ui/TimelineRowView.kt`: accessible text/file/progress row rendering.
- Create `android/app/src/main/java/app/clipbridge/ui/LocalFilesLauncher.kt`: Downloads view launch and local MediaStore fallback.
- Rework `android/app/src/main/java/app/clipbridge/MainActivity.kt`: timeline home, peer selection, navigation, copy/share actions, and secondary destinations.
- Rework `android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt`, `GlassDrawable.kt`, `BridgeDialogs.kt`, and pure `UiPresentation.kt` mappings.
- Extend `android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt`; create `LocalFilesPresentationTest.kt` for pure fallback/action rules.
- Do not add permissions to `android/app/src/main/AndroidManifest.xml`.

## Task 1: Define Android timeline presentation rules with tests first

**Files:**
- Modify: `android/app/src/main/java/app/clipbridge/ui/UiPresentation.kt`
- Modify: `android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt`
- Create: `android/app/src/test/java/app/clipbridge/ui/LocalFilesPresentationTest.kt`

- [ ] **Step 1: Add failing pure JVM assertions**

Cover selected-peer isolation, sent/received direction, text/file row kind, pending/active/complete/failed state, seven-day expiry label, copy/share availability, narrow-screen action stacking, and local-files fallback selection. Keep the tests free of Activity, Intent, emulator, and real MediaStore dependencies.

```kotlin
@Test fun historyRowsKeepDirectionAndTerminalStateDistinct() {
    assertEquals("收到", UiPresentation.historyDirection(isOutgoing = false))
    assertEquals("已发送", UiPresentation.historyDirection(isOutgoing = true))
    assertEquals("失败", UiPresentation.historyState("失败：连接中断"))
    assertEquals("已完成", UiPresentation.historyState("已保存到 Download/ClipBridge"))
}
```

- [ ] **Step 2: Run the focused JVM test to observe missing mappings**

Run from `android`: `./gradlew.bat testDebugUnitTest --tests app.clipbridge.ui.UiPresentationTest --no-daemon --max-workers 2 --console plain`. Expected: compile/test failure naming the new presentation helpers.

- [ ] **Step 3: Implement pure mapping functions**

Add `historyDirection(isOutgoing)`, `historyState(state)`, `historyContentKind(entry)`, `historyActions(entry)`, `expiryLabel(createdUtc, nowUtc)`, and `localFilesFallback(hasSystemDownloadsHandler)`. History actions expose Copy/Open/Share/Remove-record only; they never delete local files.

- [ ] **Step 4: Run focused mapping tests**

Run `./gradlew.bat testDebugUnitTest --tests app.clipbridge.ui.UiPresentationTest --no-daemon --max-workers 2 --console plain`. Expected: `BUILD SUCCESSFUL` and all presentation assertions pass.

- [ ] **Step 5: Commit Android presentation rules**

~~~powershell
git add -- android/app/src/main/java/app/clipbridge/ui/UiPresentation.kt android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt android/app/src/test/java/app/clipbridge/ui/LocalFilesPresentationTest.kt
git commit -m "feat: define Android timeline presentation rules"
~~~

## Task 2: Implement the Android light-glass visual system

**Files:**
- Modify: `android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt`
- Modify: `android/app/src/main/java/app/clipbridge/ui/GlassDrawable.kt`
- Modify: `android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt`

- [ ] **Step 1: Add failing light/dark semantic-token assertions**

Assert light and dark colors exist for canvas, raised surface, border, primary, muted text, focus, success, warning, and error; assert each transfer state has text as well as color. Do not test Android font rasterization in JVM tests.

- [ ] **Step 2: Run the focused JVM test and observe missing token assertions**

Run `./gradlew.bat testDebugUnitTest --tests app.clipbridge.ui.UiPresentationTest --no-daemon --max-workers 2 --console plain`. Expected: failures for the new theme token checks.

- [ ] **Step 3: Add focused visual tokens and surface helpers**

Use the approved pale blue/lavender gradients, restrained translucent card fills, quiet borders, rounded controls, and visible focus/pressed states. Keep text and status labels on opaque/high-contrast surfaces. Continue honoring light, dark, and system preferences; do not add a rendering library.

- [ ] **Step 4: Run unit and repository style checks**

Run the focused JUnit suite and, from repository root, `python tools/test_android_ui_style.py`. Expected: `BUILD SUCCESSFUL` and the style checker exits 0.

- [ ] **Step 5: Commit the Android visual tokens**

~~~powershell
git add -- android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt android/app/src/main/java/app/clipbridge/ui/GlassDrawable.kt android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt
git commit -m "feat: refresh Android light-glass visual system"
~~~

## Task 3: Make the peer-specific conversation timeline the Android home

**Files:**
- Create: `android/app/src/main/java/app/clipbridge/ui/HistoryTimelineAdapter.kt`
- Create: `android/app/src/main/java/app/clipbridge/ui/TimelineRowView.kt`
- Modify: `android/app/src/main/java/app/clipbridge/MainActivity.kt`
- Modify: `android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt`
- Modify: `android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt`

- [ ] **Step 1: Add failing peer-selection and timeline state tests**

Test that switching from peer A to peer B changes the queried history ID, preserves per-peer records, and maps empty, offline, active, and completed states distinctly. Test pure adapter row-type mapping for text, file, and transfer-progress entries.

- [ ] **Step 2: Run `UiPresentationTest` and confirm the new timeline rules are absent**

Run `./gradlew.bat testDebugUnitTest --tests app.clipbridge.ui.UiPresentationTest --no-daemon --max-workers 2 --console plain`. Expected: failures for peer/timeline helpers.

- [ ] **Step 3: Add the ListView timeline home**

Use Android `ListView` with a recycled `BaseAdapter`; bind `historyStore.getRecent(selectedPeerId, nowUtc)` and keep pending/current transfer rows distinct from persistent history. Put peer name and connection state in the header; provide concise Devices, Settings, and Local received files routes. Restore the selected peer and scroll position after Activity recreation.

- [ ] **Step 4: Verify pure layout and state mapping**

Run focused `UiPresentationTest` and `LocalFilesPresentationTest`. Expected: `BUILD SUCCESSFUL`; no Activity or UI window is launched.

- [ ] **Step 5: Commit Android timeline home**

~~~powershell
git add -- android/app/src/main/java/app/clipbridge/ui/HistoryTimelineAdapter.kt android/app/src/main/java/app/clipbridge/ui/TimelineRowView.kt android/app/src/main/java/app/clipbridge/MainActivity.kt android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt
git commit -m "feat: make Android conversation timeline the home"
~~~

## Task 4: Add native copy/share actions and the separate local-files entry

**Files:**
- Create: `android/app/src/main/java/app/clipbridge/ui/LocalFilesLauncher.kt`
- Modify: `android/app/src/main/java/app/clipbridge/MainActivity.kt`, `ui/HistoryTimelineAdapter.kt`, and `ui/TimelineRowView.kt`
- Modify: `android/app/src/test/java/app/clipbridge/ui/LocalFilesPresentationTest.kt`

- [ ] **Step 1: Add failing action-dispatch tests**

Use injected callbacks to assert Copy never triggers Send, Share uses the selected text or file URI, history Delete invokes only `HistoryStore.deleteRecord`, and the separate local-files destination invokes no history-delete callback.

- [ ] **Step 2: Add text/file actions to timeline rows**

Copy text only from an explicit user tap through `ClipboardManager`; use the existing MediaStore URI for file Open/Share with a read grant. Long-press may show the same actions. Where Android and the target app support cross-app drag, start a drag with a content URI and read grant; retain Copy/Share when drag is unavailable. Do not read the clipboard in the background, and do not auto-send copied or shared content.

- [ ] **Step 3: Implement the separate local-files entry and fallback**

First try the public Downloads view action `DownloadManager.ACTION_VIEW_DOWNLOADS`. If `PackageManager.resolveActivity` finds no handler, query only ClipBridge's own rows from `MediaStore.Downloads` where `RELATIVE_PATH` is `Download/ClipBridge`, then display those URIs in the dedicated local-files destination. Delete a local file only after an explicit confirmation and only by `ContentResolver.delete` on the selected URI. Do not derive this list from the seven-day history store, because local files remain after history expires. Do not add broad storage or overlay permissions.

- [ ] **Step 4: Run JVM dispatch and file-safe behavior tests**

Run `./gradlew.bat testDebugUnitTest --tests app.clipbridge.ui.LocalFilesPresentationTest --no-daemon --max-workers 2 --console plain`. Expected: `BUILD SUCCESSFUL`; tests prove history deletion and local file deletion use different callbacks.

- [ ] **Step 5: Commit Android content operations**

~~~powershell
git add -- android/app/src/main/java/app/clipbridge/ui/LocalFilesLauncher.kt android/app/src/main/java/app/clipbridge/MainActivity.kt android/app/src/main/java/app/clipbridge/ui/HistoryTimelineAdapter.kt android/app/src/main/java/app/clipbridge/ui/TimelineRowView.kt android/app/src/test/java/app/clipbridge/ui/LocalFilesPresentationTest.kt
git commit -m "feat: add Android timeline copy and file actions"
~~~

## Task 5: Redesign Android pairing, devices, settings, and update surfaces

**Files:**
- Modify: `android/app/src/main/java/app/clipbridge/MainActivity.kt`
- Modify: `android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt`, `BridgeDialogs.kt`, and `GlassDrawable.kt`
- Modify: `android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt`

- [ ] **Step 1: Add failing destination/state mapping tests**

Cover device discovery, QR pairing, trusted/offline devices, pending receive accept/reject, transfer progress/results, update checking/downloading/ready/error, sheet dismissal, keyboard-aware scrolling, and large-font action stacking.

- [ ] **Step 2: Run the focused JVM tests to capture missing destination rules**

Run `./gradlew.bat testDebugUnitTest --tests app.clipbridge.ui.UiPresentationTest --no-daemon --max-workers 2 --console plain`. Expected: failures for missing page/sheet mappings.

- [ ] **Step 3: Recompose secondary screens without changing service callbacks**

Use the same type scale, surface hierarchy, semantic status colors, and accessible descriptions for Devices, Settings, pairing/QR, incoming-file confirmation, and update sheets. Keep the system camera, document picker, permission screens, and package installation flows system-controlled. Preserve explicit confirmation callbacks.

- [ ] **Step 4: Run Android unit, lint, and assembly checks**

Run from `android`: `./gradlew.bat testDebugUnitTest lintDebug assembleDebug --no-daemon --max-workers 2 --console plain`. Expected: `BUILD SUCCESSFUL`; do not install or launch the debug APK.

- [ ] **Step 5: Commit the secondary Android surfaces**

~~~powershell
git add -- android/app/src/main/java/app/clipbridge/MainActivity.kt android/app/src/main/java/app/clipbridge/ui/BridgeUi.kt android/app/src/main/java/app/clipbridge/ui/BridgeDialogs.kt android/app/src/main/java/app/clipbridge/ui/GlassDrawable.kt android/app/src/test/java/app/clipbridge/ui/UiPresentationTest.kt
git commit -m "feat: redesign Android pairing and settings"
~~~

## Task 6: Run final Android verification without launching UI

- [ ] **Step 1: Run the full Android quality suite**

Run from `android`: `./gradlew.bat testDebugUnitTest lintDebug assembleDebug --no-daemon --max-workers 2 --console plain`. Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 2: Run the repository UI style guard**

Run from repository root: `python tools/test_android_ui_style.py`. Expected: process exit code 0.

- [ ] **Step 3: Review the source diff and record the mandated validation boundary**

Run `git diff --check`; confirm `AndroidManifest.xml`, pairing/trust cryptography, transfer hashing, update verifier, signing files, and release workflows are unchanged. Do not launch the Android app, emulator, installer, or UI smoke tests. Report physical-device rendering, cross-app drag/share behavior, and system Downloads/file-manager behavior as unverified.


