# Peer History Persistence and Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist each paired device's sent/received text and file timeline for seven days and synchronize it safely between updated Windows and Android peers without copying file bytes or deleting local files.

**Architecture:** Add independent native history services to the existing .NET and Kotlin apps, using the same versioned JSON event contract and per-platform append-only JSONL stores with compaction. Reuse the authenticated trusted-device text route for sync, negotiate support before sending history, and keep history persistence/synchronization failures separate from transfer outcomes. No cloud service or third-party history endpoint is introduced.

**Tech Stack:** .NET 10 / C# 12, Kotlin / Android SDK 36, existing TrustedChannel and text socket, System.Text.Json, org.json, existing console and JVM test harnesses; no new runtime package.

---

## Scope and ordering

This plan is the history/data subsystem and must finish before the UI plan at `docs/superpowers/plans/2026-11-06-cross-platform-ui-rebuild.md` consumes its store APIs. It does not redesign forms or change update/signing/release files. Start from the current branch and preserve all existing workspace changes; only stage the exact files listed for the current task.

## File map

- Create `ClipBridge/Windows/History/HistoryModels.cs`: C# event, file metadata, tombstone, and sync DTOs with bounded validation.
- Create `ClipBridge/Windows/History/HistoryPolicy.cs`: UTC seven-day visibility/expiry rules.
- Create `ClipBridge/Windows/History/HistoryStore.cs`: per-user JSONL append log, in-memory per-peer index, compaction, merge, and deletion tombstones.
- Create `ClipBridge/Windows/History/HistorySyncProtocol.cs` and `HistorySyncService.cs`: wire codec, size-limited pages, trusted peer sync orchestration.
- Create matching Kotlin files under `android/app/src/main/java/app/clipbridge/history/`.
- Modify `ClipBridge/Windows/Devices/DiscoveryProtocol.cs`, `DeviceHub.cs`, `SyncNode.cs`, `FileTransferNode.cs`, and `TrayAppContext.cs` only to advertise/route the sync capability and record completed sends/receives.
- Modify `android/app/src/main/java/app/clipbridge/protocol/DiscoveryProtocol.kt`, `network/DeviceHub.kt`, `network/BridgeService.kt`, `network/FileTransfers.kt`, and `storage/ReceivedFiles.kt` for the same behavior.
- Add `tools/TransferTests/HistoryTests.cs`; include history source files in `tools/TransferTests/TransferTests.csproj` and dispatch `--history` in `Program.cs`.
- Add `android/app/src/test/java/app/clipbridge/history/HistoryTests.kt` and extend `ExperienceInteropTest.kt` with the cross-language wire contract test.

## Task 1: Define the shared event contract and retention policy

**Files:**
- Create: `ClipBridge/Windows/History/HistoryModels.cs`
- Create: `ClipBridge/Windows/History/HistoryPolicy.cs`
- Create: `tools/TransferTests/HistoryTests.cs`
- Modify: `tools/TransferTests/TransferTests.csproj`
- Modify: `tools/TransferTests/Program.cs`
- Create: `android/app/src/main/java/app/clipbridge/history/HistoryModels.kt`
- Create: `android/app/src/main/java/app/clipbridge/history/HistoryPolicy.kt`
- Create: `android/app/src/test/java/app/clipbridge/history/HistoryTests.kt`

- [ ] **Step 1: Add failing UTC retention tests**

Test an event at 6 days 23:59 old as visible, exactly 7 days old as expired, a future timestamp as invalid, and an event from a different peer as excluded from a peer query. Use fixed UTC milliseconds; do not use sleeps or local time zones.

```csharp
var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000).ToUnixTimeMilliseconds();
Check(HistoryPolicy.IsVisible(now - TimeSpan.FromDays(6).Ticks / TimeSpan.TicksPerMillisecond, now), "six-day event visible");
Check(!HistoryPolicy.IsVisible(now - TimeSpan.FromDays(7).Ticks / TimeSpan.TicksPerMillisecond, now), "seven-day boundary expired");
```

- [ ] **Step 2: Run the focused C# test and confirm it fails because the policy types do not exist**

Run: `dotnet run --project tools/TransferTests -c Release -- --history`
Expected: compile failure naming `HistoryPolicy` and `HistoryTests`.

- [ ] **Step 3: Define matching C# and Kotlin DTOs**

Use the same JSON names and semantics in both languages: `schemaVersion`, `eventId`, `peerId`, `senderDeviceId`, `createdUtc`, `contentType`, `text`, `files`, and `state`. `HistoryFileMetadata` contains `name`, `size`, and `sha256`. Store-only file paths/URIs belong in a separate local field and must not appear in `HistorySyncEvent`. Define `HistoryTombstone(eventId, peerId, deletedUtc)` and version-1 sync page DTOs.

- [ ] **Step 4: Implement the policy and validation, then rerun tests**

Implement `HistoryPolicy.IsVisible(createdUtc, nowUtc)` as `createdUtc > nowUtc - SevenDays && createdUtc <= nowUtc`; reject empty IDs, unknown schema versions, negative sizes, malformed SHA-256, file names rejected by `TransferProtocol.ValidateFileName`, text larger than `SharedProtocol.MaxTextBytes`, and records whose peer does not match the authenticated route.

Run: `dotnet run --project tools/TransferTests -c Release -- --history`
Expected: `HISTORY TESTS PASSED`.

Run from `android`: `./gradlew.bat testDebugUnitTest --tests app.clipbridge.history.HistoryTests --no-daemon --max-workers 2 --console plain`
Expected: `BUILD SUCCESSFUL` and all history tests pass.

- [ ] **Step 5: Commit the contract and policy only**

~~~powershell
git add -- ClipBridge/Windows/History/HistoryModels.cs ClipBridge/Windows/History/HistoryPolicy.cs tools/TransferTests/HistoryTests.cs tools/TransferTests/TransferTests.csproj tools/TransferTests/Program.cs android/app/src/main/java/app/clipbridge/history/HistoryModels.kt android/app/src/main/java/app/clipbridge/history/HistoryPolicy.kt android/app/src/test/java/app/clipbridge/history/HistoryTests.kt
git commit -m "feat: define cross-platform history records"
~~~

## Task 2: Add durable stores, seven-day pruning, and safe deletion

**Files:**
- Create: `ClipBridge/Windows/History/HistoryStore.cs`
- Create: `android/app/src/main/java/app/clipbridge/history/HistoryStore.kt`
- Modify: `tools/TransferTests/HistoryTests.cs`
- Modify: `android/app/src/test/java/app/clipbridge/history/HistoryTests.kt`

- [ ] **Step 1: Add failing store tests using temporary directories**

Cover restart/reload, per-peer queries, idempotent upsert by `eventId`, seven-day prune, manual delete tombstone, merge of a remote tombstone, preservation of the referenced local file, and recovery from a truncated final JSONL line.

- [ ] **Step 2: Run focused tests and confirm the store APIs are missing**

Run `dotnet run --project tools/TransferTests -c Release -- --history` and `./gradlew.bat testDebugUnitTest --tests app.clipbridge.history.HistoryTests --no-daemon --max-workers 2 --console plain` from `android`. Expected: compile failures for the new `HistoryStore` API.

- [ ] **Step 3: Implement the same append-log behavior on both platforms**

Expose the same operations: `Upsert(entry)`, `GetRecent(peerId, nowUtc)`, `DeleteRecord(peerId, eventId, nowUtc)`, `Merge(peerId, entries, tombstones, nowUtc)`, `GetSyncSnapshot(peerId, nowUtc)`, and `Prune(nowUtc)`. Use `%LocalAppData%/ClipBridge/history/events-v1.jsonl` on Windows and `filesDir/history/events-v1.jsonl` on Android. Append one versioned JSON operation per line; load valid complete lines; write compaction output to a sibling temporary file and replace the old log atomically. Pruning and record deletion must never call `File.Delete`, `ContentResolver.delete`, or alter a local path/URI.

- [ ] **Step 4: Verify reload, prune, merge, and deletion tests pass**

Run focused C# and Kotlin history tests. Expected: `HISTORY TESTS PASSED` and `BUILD SUCCESSFUL`.

- [ ] **Step 5: Commit the platform stores**

~~~powershell
git add -- ClipBridge/Windows/History/HistoryStore.cs tools/TransferTests/HistoryTests.cs android/app/src/main/java/app/clipbridge/history/HistoryStore.kt android/app/src/test/java/app/clipbridge/history/HistoryTests.kt
git commit -m "feat: persist seven-day transfer history"
~~~

## Task 3: Record text sends and receives without background clipboard reads

**Files:**
- Modify: `ClipBridge/Windows/SyncNode.cs`
- Modify: `ClipBridge/Windows/TrayAppContext.cs`
- Modify: `android/app/src/main/java/app/clipbridge/network/BridgeService.kt`
- Modify: `tools/TransferTests/HistoryTests.cs` and the trusted-pair `PairingRoundtripAsync` in `tools/TransferTests/ExperienceTests.cs`

- [ ] **Step 1: Add failing loopback assertions for both local directions**

Use the two synthetic `DeviceHub` instances already created by `ExperienceTests.PairingRoundtripAsync`; after explicit pairing, send fixed text from A to B. Assert one sent entry on A and one received entry on B share the same event ID and original timestamp, use opposite direction labels, and contain the same text. Assert that constructing/starting the history service never reads the OS clipboard.

- [ ] **Step 2: Run `dotnet run --project tools/TransferTests -c Release -- --history` and confirm the assertions fail before recording is implemented**

Expected: a test failure stating that the text send/receive history event is absent.

- [ ] **Step 3: Emit typed text events from the existing send/receive paths**

Inject the Windows `HistoryStore` into `SyncNode`; create the history event from the existing `ClipboardMessage` after explicit send and after a trusted receive passes all current validation. On Android, use the existing `MessageId`, `OriginDeviceId`, and `TimestampUtc` in `BridgeService.sendText` and `receiveText`. Persist the event through `HistoryStore`; retain the current explicit user action and clipboard behavior. Never read clipboard contents from a timer, service startup, or history rendering.

- [ ] **Step 4: Run loopback text and history tests**

Run: `dotnet run --project tools/TransferTests -c Release -- --history`
Expected: `HISTORY TESTS PASSED`.

Run: `dotnet run --project tools/TransferTests -c Release`
Expected: `ALL TRANSFER TESTS PASSED`.

Run from `android`: `./gradlew.bat testDebugUnitTest --tests app.clipbridge.history.HistoryTests --no-daemon --max-workers 2 --console plain`. Run the full `ExperienceInteropTest` with the C# interoperability server procedure in Task 6. Expected: both Gradle commands finish with `BUILD SUCCESSFUL`.

- [ ] **Step 5: Commit text event recording**

~~~powershell
git add -- ClipBridge/Windows/SyncNode.cs ClipBridge/Windows/TrayAppContext.cs tools/TransferTests/Program.cs tools/TransferTests/HistoryTests.cs android/app/src/main/java/app/clipbridge/network/BridgeService.kt android/app/src/test/java/app/clipbridge/history/HistoryTests.kt
git commit -m "feat: record explicit text transfers"
~~~

## Task 4: Record file outcomes and local received-file references

**Files:**
- Modify: `ClipBridge/Windows/FileTransferNode.cs`
- Modify: `ClipBridge/Windows/TransferProtocol.cs`
- Modify: `android/app/src/main/java/app/clipbridge/network/FileTransfers.kt`
- Modify: `android/app/src/main/java/app/clipbridge/storage/ReceivedFiles.kt`
- Modify: `tools/TransferTests/ExperienceTests.cs`, `tools/TransferTests/Program.cs`, and history tests

- [ ] **Step 1: Add failing loopback tests for accepted, rejected, and failed file events**

Extend the trusted synthetic peer scenario in `ExperienceTests.PairingRoundtripAsync`; assert accepted file sends produce matching event IDs and metadata on both peers, the receiver record contains its local saved path while the sync DTO does not, rejection/failure is recorded only for the side that observed the outcome, and deleting a history record leaves the bytes intact.

- [ ] **Step 2: Run `dotnet run --project tools/TransferTests -c Release -- --history` and confirm the file history assertions fail**

Expected: test failure for missing file-history records.

- [ ] **Step 3: Record file metadata at the offer/terminal-result boundaries**

Use existing `TransferId`, offer timestamp, origin device, `TransferFile`/`Offer` metadata, completion result, and the paths returned by `ReceiveFileStore.Commit`. On Android, use the committed `MediaStore` URIs returned by `ReceivedFiles.commit`. Do not copy file bytes into history storage or sync messages. Keep the current SHA-256 verification and explicit accept/reject gate unchanged.

- [ ] **Step 4: Run transfer and history tests**

Run `dotnet run --project tools/TransferTests -c Release`. Expected: `ALL TRANSFER TESTS PASSED`. Run from `android`: `./gradlew.bat testDebugUnitTest --no-daemon --max-workers 2 --console plain`. Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 5: Commit file event recording**

~~~powershell
git add -- ClipBridge/Windows/FileTransferNode.cs ClipBridge/Windows/TransferProtocol.cs tools/TransferTests/Program.cs tools/TransferTests/HistoryTests.cs android/app/src/main/java/app/clipbridge/network/FileTransfers.kt android/app/src/main/java/app/clipbridge/storage/ReceivedFiles.kt android/app/src/test/java/app/clipbridge/history/HistoryTests.kt
git commit -m "feat: record file transfer outcomes"
~~~

## Task 5: Version and test the cross-platform sync envelope

**Files:**
- Create: `ClipBridge/Windows/History/HistorySyncProtocol.cs`
- Create: `android/app/src/main/java/app/clipbridge/history/HistorySyncProtocol.kt`
- Modify: `ClipBridge/Windows/Devices/DiscoveryProtocol.cs`
- Modify: `android/app/src/main/java/app/clipbridge/protocol/DiscoveryProtocol.kt`
- Modify: history protocol tests and `CompatibilityTest.kt`

- [ ] **Step 1: Add failing codec and compatibility tests**

Use the same JSON fixture in both test suites. Cover schema version 1, entries plus tombstones, unknown versions, oversized payload, invalid peer ID, absent capability field from an older discovery packet, and an older packet parser ignoring the optional capability field.

- [ ] **Step 2: Run the focused tests and verify the new codec/capability checks fail**

Expected: compile or assertion failures for missing codec and missing `historySyncVersion` support.

- [ ] **Step 3: Implement bounded sync pages and a conservative capability hint**

Use envelope types `historySyncHello`, `historySyncRequest`, `historySyncPage`, and `historySyncAck`, with `schemaVersion`, `requestId`, `peerId`, a fixed snapshot page number, entries, and tombstones. Freeze and sort one 7-day snapshot for each connection so page boundaries remain stable while new events append. Keep each encrypted JSON page below `SharedProtocol.MaxFrameBytes - 28`; split snapshots into pages and cap page count per connection. Add optional `historySyncVersion` to public discovery DTOs with a default of zero. Treat it only as a hint: send text or file metadata only after the existing trusted route authenticates the same `peerId` and the peer confirms protocol version 1.

- [ ] **Step 4: Run C# and Kotlin golden-fixture tests**

Run the focused C# history command and `./gradlew.bat testDebugUnitTest --tests app.clipbridge.protocol.CompatibilityTest --no-daemon --max-workers 2 --console plain`. Expected: `HISTORY TESTS PASSED` and `BUILD SUCCESSFUL`.

- [ ] **Step 5: Commit the sync contract**

~~~powershell
git add -- ClipBridge/Windows/History/HistorySyncProtocol.cs ClipBridge/Windows/Devices/DiscoveryProtocol.cs tools/TransferTests/HistoryTests.cs android/app/src/main/java/app/clipbridge/history/HistorySyncProtocol.kt android/app/src/main/java/app/clipbridge/protocol/DiscoveryProtocol.kt android/app/src/test/java/app/clipbridge/history/HistoryTests.kt android/app/src/test/java/app/clipbridge/protocol/CompatibilityTest.kt
git commit -m "feat: define versioned history sync messages"
~~~

## Task 6: Synchronize history over the existing trusted text route

**Files:**
- Create: `ClipBridge/Windows/History/HistorySyncService.cs`
- Create: `android/app/src/main/java/app/clipbridge/history/HistorySyncService.kt`
- Modify: `ClipBridge/Windows/SyncNode.cs`, `ClipBridge/Windows/Devices/DeviceHub.cs`, and `ClipBridge/Windows/TrayAppContext.cs`
- Modify: `android/app/src/main/java/app/clipbridge/network/BridgeService.kt` and `network/DeviceHub.kt`
- Modify: `tools/TransferTests/HistoryTests.cs`, `ExperienceInterop.cs`, and `android/app/src/test/java/app/clipbridge/protocol/ExperienceInteropTest.kt`

- [ ] **Step 1: Add failing two-peer loopback synchronization tests**

Seed peer A with a text event and a file event, peer B with a different event, then synchronize both ways. Assert each side sees the same shared events, local file references remain local, tombstones remove the record at both ends, reconnect/retry does not duplicate records, and sync timeout leaves ordinary `SendTextAsync`/file transfers usable.

- [ ] **Step 2: Run the history test and confirm both peers remain unsynchronized**

Run: `dotnet run --project tools/TransferTests -c Release -- --history`
Expected: failing assertion that peer B does not yet contain peer A's event.

- [ ] **Step 3: Add authenticated request/response handling to the existing text socket**

After `DeviceHub.IncomingAsync(..., kind: 1, ...)` returns a trusted `Source`, dispatch `historySync*` envelopes to `HistorySyncService`; never accept history sync on a legacy route with `Source == null`. Initiate sync only for an advertised version-1 peer and authenticate its device ID through `DeviceHub.OutgoingAsync`. Unknown non-clipboard message types must not be interpreted as clipboard text. Exchange bounded pages until both frozen snapshots are acknowledged; apply each page through idempotent `HistoryStore.Merge`.

- [ ] **Step 4: Trigger one sync on authenticated peer availability and retry after reconnect**

Add one peer-ready event to both `DeviceHub` implementations. Debounce by `(peerId, connection epoch)` in the host; do not run a one-second UI refresh sync loop. Reconnect triggers a new sync; timeout backs off and never changes transfer success state.

- [ ] **Step 5: Run C# loopback sync and Kotlin/.NET interop tests**

Create an ASCII-only loopback test directory, start the C# interoperability server in a hidden process, and run the Android JVM suite against that directory:

```powershell
$interop = 'C:\Temp\clipbridge-history-interop'
New-Item -ItemType Directory -Force -Path $interop | Out-Null
$server = Start-Process dotnet -ArgumentList @('run','--project','tools/TransferTests','-c','Release','--','--experience-interop',$interop) -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $interop 'server.log') -RedirectStandardError (Join-Path $interop 'server.err')
$env:CLIPBRIDGE_EXPERIENCE_INTEROP_DIR = $interop
for ($i = 0; $i -lt 200 -and !(Test-Path (Join-Path $interop 'experience.json')); $i++) { Start-Sleep -Milliseconds 50 }
if (!(Test-Path (Join-Path $interop 'experience.json'))) { throw 'C# interop server did not become ready.' }
Push-Location android
try { ./gradlew.bat testDebugUnitTest --no-daemon --max-workers 2 --console plain } finally { Pop-Location; Remove-Item Env:CLIPBRIDGE_EXPERIENCE_INTEROP_DIR -ErrorAction SilentlyContinue; Wait-Process -Id $server.Id -Timeout 20 -ErrorAction SilentlyContinue; if (Get-Process -Id $server.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $server.Id } }
Get-Content (Join-Path $interop 'server.log')
```

Run `dotnet run --project tools/TransferTests -c Release -- --history` before this integration run. Expected: both history assertions pass, Gradle prints `BUILD SUCCESSFUL`, and the server log ends with `PAIRING AND V2 BIDIRECTIONAL INTEROP PASSED`.

- [ ] **Step 6: Commit peer synchronization**

~~~powershell
git add -- ClipBridge/Windows/History/HistorySyncService.cs ClipBridge/Windows/SyncNode.cs ClipBridge/Windows/Devices/DeviceHub.cs ClipBridge/Windows/TrayAppContext.cs tools/TransferTests/HistoryTests.cs tools/TransferTests/ExperienceInterop.cs android/app/src/main/java/app/clipbridge/history/HistorySyncService.kt android/app/src/main/java/app/clipbridge/network/BridgeService.kt android/app/src/main/java/app/clipbridge/network/DeviceHub.kt android/app/src/test/java/app/clipbridge/protocol/ExperienceInteropTest.kt
git commit -m "feat: sync history over trusted peer channels"
~~~

## Task 7: Verify the subsystem and compatibility boundaries

**Files:**
- Modify only the history and protocol tests listed above if a failing assertion exposes a defect.

- [ ] **Step 1: Run complete Windows transfer/history tests**

Run: `dotnet run --project tools/TransferTests -c Release`
Expected: `ALL TRANSFER TESTS PASSED`.

- [ ] **Step 2: Run the Android verification suite**

Run from `android`: `./gradlew.bat testDebugUnitTest lintDebug assembleDebug --no-daemon --max-workers 2 --console plain`
Expected: `BUILD SUCCESSFUL`; the generated debug APK is for tests only and is not a release asset.

- [ ] **Step 3: Build the Windows app and inspect the history diff**

Run: `dotnet build -c Release`
Expected: build succeeds with no new warning about serialization or target frameworks. Confirm only the listed history, transport, and tests files changed; no manifest, signing key, installer, updater, release workflow, or package files changed.

- [ ] **Step 4: Commit any final test-only correction and report limits**

If a final test-only fix is required, stage `tools/TransferTests/HistoryTests.cs`, `android/app/src/test/java/app/clipbridge/history/HistoryTests.kt`, and `android/app/src/test/java/app/clipbridge/protocol/ExperienceInteropTest.kt` only when those files changed, then run `git commit -m "test: verify peer history compatibility"`. Report loopback coverage separately from real Wi-Fi/device verification; do not claim real network sync was exercised by unit tests.






