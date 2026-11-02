# Android and Windows Manual Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. User has already approved implementation; execute inline in the current session without another approval gate.

**Goal:** Deliver installable Android and Windows clients for manual, encrypted clipboard and file transfers over a local network.

**Architecture:** Reuse the existing v1 encrypted discovery and text framing. Remove Windows clipboard monitoring, expose explicit per-device sends, and add a separate authenticated chunked file channel. A native Kotlin Android activity and connected-device foreground service own pairing, networking, manual clipboard actions, URI file selection, and receiving.

**Tech Stack:** .NET SDK 10.0.403, Windows Forms, Kotlin/JVM, Android SDK 36 with minSdk 29, AGP 8.10.1, Gradle 8.11.1, JDK 17, AES-GCM/SHA-256. Verify tool compatibility against primary documentation before building; record any necessary version correction.

## Workspace and files

Use the existing isolated Git worktree at D:/互传/github-public on codex/manual-android-transfer. Preserve all release files and update signing configuration.

Windows responsibilities:
- Modify ClipBridge/Windows/SyncNode.cs: discovery, explicit text send, validation, receive, no watcher or implicit queue.
- Modify ClipBridge/Windows/TrayAppContext.cs: wire manual actions, clipboard retry, preview, file confirmation and lifecycle.
- Modify ClipBridge/Windows/ControlPanelForm.cs: replace automatic toggle with manual actions and preserve theme/window behavior.
- Create ClipBridge/Windows/ManualSendForm.cs: target selection, text preview or selected files, cancellation and result UI.
- Create ClipBridge/Windows/FileTransferNode.cs: file listener, sender, confirmation, cancellation and bounded streaming.
- Create ClipBridge/Windows/TransferProtocol.cs: file metadata/control, size limits, safe names, encrypted frames, key separation.
- Create ClipBridge/Windows/ReceiveFileStore.cs: temporary writes, hash checks, non-overwrite commit and cleanup.
- Modify ClipBridge/Windows/Announcement.cs and PeerSnapshot.cs only with optional capability metadata if required; preserve existing constructor callers.
- Create tools/TransferTests: executable tests using production protocol files; avoid test framework/network package dependencies.

Android responsibilities (under android/):
- Gradle wrapper/build settings and app/build.gradle.kts: reproducible APK and JVM tests.
- app/src/main/AndroidManifest.xml: Internet/network, notifications and connectedDevice foreground service; no privileged clipboard or storage permissions.
- protocol/BridgeProtocol.kt: interoperable discovery, AES-GCM, framed text/control, bounds and freshness.
- protocol/FileProtocol.kt: versioned file messages and bounded binary data chunks.
- storage/PairingStore.kt: Keystore encrypted local pairing key, device ID and manual peer address.
- storage/ReceivedFiles.kt: temporary files, MediaStore Downloads and cleanup.
- network/BridgeService.kt: lifecycle, notifications, discovery, explicit sends and receive.
- network/FileTransfers.kt: offer/accept/reject/complete state machine and stream processing.
- MainActivity.kt: device selection, clipboard preview/send, pairing, file picker/share, receive confirmation and progress.
- tests: same crypto vector and negative cases on JVM; instrumented/manual test checklist for OS clipboard and storage.

## Task 1: Protocol test baseline

- [ ] Create tools/TransferTests/TransferTests.csproj linking production protocol sources, plus Program.cs.
- [ ] Add a deterministic cross-platform fixture with pairing bytes 00..1f, nonce 00..0b and text 中文🙂. Assert derived key, ciphertext decoding, UTF-8 boundaries, malformed frame lengths, tampering and timestamp limits.
- [ ] Run dotnet run --project tools/TransferTests -c Release; record existing assertions versus missing manual-send assertions.
- [ ] Use public contract: Task<bool> SendTextAsync(IPAddress address, string text, CancellationToken token). Sending to one address cannot broadcast to discovered peers; repeated explicit sends of identical text remain valid.

## Task 2: Windows manual text transport

- [ ] Add a test that reflection finds SendTextAsync and no OnLocalClipboardTextChanged method; run and observe failure before editing SyncNode.
- [ ] Replace the bounded clipboard change channel and content hash suppression with explicit per-target SendTextAsync. Keep receive deduplication by message ID.
- [ ] Remove the _enabled gate associated with old auto sync; incoming authenticated clipboard messages are received while app is running. Preserve freshness and max-size validation.
- [ ] Run protocol tests and dotnet build ClipBridge.csproj -c Release.

## Task 3: Windows manual UI

- [ ] Add UI assertions for SendClipboardButton, SendFilesButton, absence of SyncAnimatedToggle and callback invocation; run old UI to observe failure.
- [ ] Replace auto toggle header with manual actions. Remove watcher construction and disposal from TrayAppContext; remove obsolete WindowsClipboardListener.cs.
- [ ] Add ManualSendForm with target selector and read-only preview. Clipboard reading happens once per user click, on UI thread with bounded busy retries. Repeating a user-requested send must work; pairing-key text is rejected.
- [ ] Expose same clipboard/file actions from tray; preserve theme selection, update checking, minimize/restore and hide-to-tray.
- [ ] Run TransferTests, main build and UiSmokeTests.

## Task 4: File protocol and safe receive storage

- [ ] Add failing tests for ../evil, absolute paths, Windows reserved names, >256 files, negative sizes, duplicate names and wrong SHA-256.
- [ ] Define port 45823, file key SHA256(UTF8(ClipBridge/files/v1 plus NUL) || pairing bytes), max control 256 KiB, chunk size 64 KiB. Every frame is length-prefixed AES-GCM.
- [ ] Define offer JSON: Type=fileOffer, TransferId, OriginDeviceId, TimestampUtc, Files=[Name, Size, Sha256]. Replies use Type=accept/reject/complete/error and the same TransferId. Binary data plaintext is byte 1, 16-byte transfer ID, int32 BE file index, int64 BE offset, content. Completion is explicit JSON and carries per-file hashes.
- [ ] Write ReceiveFileStore with CreateNew temporary files inside a task-specific directory; verify expected length/hash; commit with no overwrite and collision suffixes. Cleanup is scoped to this transfer; cancellation does not remove existing files.
- [ ] Run negative tests and non-overwrite/hash tests.

## Task 5: Windows file service and UI

- [ ] Add loopback integration tests: multi-file/empty-file roundtrip, receiver rejection, sender cancel and bad hash cleanup. Start listeners on configurable ephemeral ports in tests.
- [ ] Implement FileTransferNode: authenticate offer, async user confirmation, 60s decision timeout, per-read/write idle timeout, explicit completion result and bounded concurrency.
- [ ] Wire file selection/drop and receiver confirmation to Windows UI; show bytes and progress; allow cancel and retry. Text listener remains independent during transfers.
- [ ] Test on loopback and run build/UI regression checks.

## Task 6: Android build and protocol core

- [ ] Install JDK and SDK into local tool cache outside tracked source, and record checksums; create wrapper pinned to Gradle 8.11.1 with SHA-256 verification.
- [ ] Create minimal Kotlin application/build configuration with compile/target SDK 36 and min SDK 29; no Shizuku, ADB library, IME or clipboard background permission.
- [ ] Write JVM tests against the fixed cross-language vector and malformed file/text frames, run to observe missing implementation, then implement BridgeProtocol and FileProtocol.
- [ ] Run gradlew testDebugUnitTest assembleDebug. Keep machine local SDK path ignored.

## Task 7: Android pairing, lifecycle and manual clipboard UI

- [ ] Implement Keystore-backed pairing store and stable non-sensitive device ID; support 64-hex key import, manual IPv4 and revoke.
- [ ] Implement foreground connectedDevice service started only from visible user interaction, ongoing state notification with Stop action, and no clipboard reading in the service.
- [ ] Implement MainActivity native views: pairing, target device selector, send clipboard preview/confirmation, send text result and received text copy status. Only activity handles clipboard reads while focused; received text writing failure is visible.
- [ ] Add device discovery using same announcement field casing, port and timestamps. Close sockets, multicast lock and workers on Stop/revoke; reconnect discovery with bounded backoff.
- [ ] Build APK and run JVM tests.

## Task 8: Android file selection, sending and receiving

- [ ] Implement ACTION_OPEN_DOCUMENT multiple selection plus ACTION_SEND/SEND_MULTIPLE; use ContentResolver streams and persist URI permissions when offered.
- [ ] Copy selected files into bounded on-disk preparation cache while computing size/hash so metadata is stable, progress is visible, cancellations clean cache and unknown-size URIs are supported. Never load entire files into memory.
- [ ] Implement FileTransfers protocol with immutable per-operation pairing key, input validation, per-file offsets, exact byte limits, authenticated final completion, cancel and rejection.
- [ ] Receive into app temporary files; verify all files before publishing to MediaStore Downloads/ClipBridge with IS_PENDING, clean pending rows after failure and preserve existing names.
- [ ] Surface offers only through an in-app confirmation; while app is backgrounded notify user to open it, never auto-accept. Provide Stop service and cancel controls.
- [ ] Run JVM tests, Android lint and assembleDebug.

## Task 9: Cross-platform validation and local artifacts

- [ ] Execute C# to JVM and JVM to C# loopback text/file interoperability against production protocol sources. Include Chinese names, empty file, multiple files, file rejection and tampering.
- [ ] Run Windows main build, TransferTests, UiSmokeTests and existing update test project build. Do not run production update installer.
- [ ] Run Android testDebugUnitTest lintDebug assembleDebug, inspect manifest and APK signature using build tools, and retain relevant diagnostic output.
- [ ] Produce local Windows runnable package and Android debug APK under D:/互传/artifacts/manual-transfer. Label APK as debug-signed local test build; do not create a public release or change latest manifests.
- [ ] Update README and android/README with pairing, install, same-LAN, foreground clipboard operation and file receive behavior. Include exact known test limitations: no connected OnePlus device unless one becomes available; no claim of actual phone verification.
- [ ] Update plan checkboxes only for verified work, review git diff/check and record test results. Commit coherent source changes locally and leave branch for user review.

## User steering: preserve game focus

Do not run interactive tests, launch either client, installers, emulator or previews during this session. Compile UiSmokeTests without executing. Remove automatic startup panel activation and automatic update prompts. Queue file requests and expose a pending-request button for user-initiated confirmation; incoming requests never call Show/Activate/MessageBox/balloon. Background errors update in-app status only. Add static behavior checks for these rules and record interactive UI/real phone verification as deferred.

## Verification status (2026-11-02)

Verified in the current worktree:

- Windows Release build, UiSmokeTests build, and UpdateSmokeTests build compile successfully. The interactive UI smoke test was intentionally not run because the user is gaming and requested no window activation.
- tools/TransferTests passes cross-platform key/AES-GCM vectors, explicit targeted text sends, duplicate explicit sends, malformed file names, empty/multi-file roundtrips, receiver rejection, out-of-order chunks, hash failure, cleanup, and no-overwrite behavior.
- Android testDebugUnitTest, assembleDebug and lintDebug pass when invoked through the temporary ASCII junction required by Gradle's Windows classpath handling for the Chinese workspace path.
- Local artifacts are copied to artifacts/manual-transfer/ as a debug APK and Windows Release DLL.

Deferred until the user is not gaming and has a device available:

- Interactive Windows UI behavior, actual one-plus 15 pairing and ColorOS Wi-Fi/hotspot testing, real system clipboard/file picker behavior, firewall behavior and large video transfer.
- A live C# to Kotlin phone test; the fixed crypto and framing vector is shared and the JVM unit tests pass, but no Android device was connected in this run.
