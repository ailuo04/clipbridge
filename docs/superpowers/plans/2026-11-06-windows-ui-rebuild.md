# Windows UI Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the complete Windows presentation layer with the approved light-glass design and a compact per-device conversation timeline while preserving existing pairing, transfer, history, clipboard, update, and signing behavior.

**Architecture:** Keep WinForms and the existing networking/services. Add a compact `ConversationForm` backed by the history store, retain a redesigned device/setup/settings destination, and make the close button hide to the tray while explicit Exit shuts down the app. Reuse the existing theme controller and update device, transfer, pairing, and update surfaces to one visual system.

**Tech Stack:** .NET 10 WinForms / C# 12, existing `UiPalette`, `UiThemeController`, drawing helpers, `tools/TransferTests`, and WinForms `tools/UiSmokeTests`; no new UI or drawing dependency.

---

## Scope and ordering

Complete `docs/superpowers/plans/2026-11-06-peer-history-sync.md` before this plan so `HistoryStore` and typed transfer events exist. This plan changes Windows presentation only; do not change pairing cryptography, file SHA-256 checks, update verification, installer permissions, release scripts, or the Android project. Preserve all unrelated working-tree changes and stage only the exact paths for the current task.

## File map

- Create `ClipBridge/Windows/ConversationForm.cs`: compact connected workspace and top-level device selector.
- Create `ClipBridge/Windows/ConversationTimelineControl.cs`: virtualized timeline, empty/offline state, and update binding.
- Create `ClipBridge/Windows/HistoryEntryControl.cs`: owner-draw renderer and accessible action hit targets for text/file rows.
- Create `ClipBridge/Windows/HistoryPresentation.cs`: pure status/direction/action-label mapping.
- Modify `ClipBridge/Windows/TrayAppContext.cs`: create/activate conversation window, open device manager, wire store actions, hide/reopen and explicit exit behavior.
- Recompose `ClipBridge/Windows/ControlPanelForm.cs`, `ClipBridge/Windows/Devices/DevicesForm.cs`, `ClipBridge/Windows/TransfersForm.cs`, and `ClipBridge/Windows/UpdateDialog.cs` without changing their service callbacks.
- Reuse/update `ClipBridge/Windows/UiPalette.cs`, `Ui.cs`, `UiThemeController.cs`, `AppHeaderSurface.cs`, and `RoundedSurface.cs`.
- Extend `tools/UiSmokeTests/Program.cs`; create `tools/UiSmokeTests/ConversationSmoke.cs`; update `DevicesFormSmoke.cs` and `UpdateDialogSmoke.cs` only for changed accessible labels/layout.

## Task 1: Define timeline presentation mappings with tests first

**Files:**
- Create: `ClipBridge/Windows/HistoryPresentation.cs`
- Create: `tools/UiSmokeTests/ConversationSmoke.cs`
- Modify: `tools/UiSmokeTests/Program.cs`

- [ ] **Step 1: Add failing pure mapping assertions**

Add tests for sent/received labels, text/file row kinds, pending/active/complete/failed state labels, expiry labels, and which actions are allowed for each content type. A history row's Delete action must call only `HistoryStore.DeleteRecord`; file deletion is not an action on a history row.

```csharp
Check(HistoryPresentation.Direction(isOutgoing: false) == "收到", "received direction label");
Check(HistoryPresentation.Direction(isOutgoing: true) == "已发送", "sent direction label");
Check(HistoryPresentation.State("失败：连接中断") == "失败", "failed state label");
```

- [ ] **Step 2: Build the app and run the new focused smoke entry to see the expected missing-type failure**

Run `dotnet build -c Release`, then `dotnet run --project tools/UiSmokeTests -c Release -- --conversation-only`. Expected: the new smoke dispatcher runs and reports the missing `HistoryPresentation` type.

- [ ] **Step 3: Implement pure mappings and an isolated smoke dispatcher**

Implement `HistoryPresentation.Direction(bool)`, `HistoryPresentation.State(string)`, `HistoryPresentation.ContentKind(HistoryEntry)`, `HistoryPresentation.Actions(HistoryEntry)`, and `HistoryPresentation.ExpiryLabel(long createdUtc, long nowUtc)`. Add `--conversation-only` to `tools/UiSmokeTests/Program.cs` so the form test can run without launching the full app shell.

- [ ] **Step 4: Run focused mappings**

Run `dotnet build -c Release`, then `dotnet run --project tools/UiSmokeTests -c Release -- --conversation-only`. Expected: all pure mapping assertions pass.

- [ ] **Step 5: Commit presentation rules**

~~~powershell
git add -- ClipBridge/Windows/HistoryPresentation.cs tools/UiSmokeTests/ConversationSmoke.cs tools/UiSmokeTests/Program.cs
git commit -m "feat: define Windows timeline presentation states"
~~~

## Task 2: Apply the light-glass tokens to Windows surfaces

**Files:**
- Modify: `ClipBridge/Windows/UiPalette.cs`, `Ui.cs`, `UiThemeController.cs`, `AppHeaderSurface.cs`, and `RoundedSurface.cs`
- Modify: `tools/UiSmokeTests/Program.cs` and `ConversationSmoke.cs`

- [ ] **Step 1: Add failing light/dark palette assertions**

Assert every semantic state has a readable foreground/background pair and a text/icon label; assert focus and selected controls remain visible in both themes. Do not compare font pixels across machines.

- [ ] **Step 2: Add and run the palette-only smoke command**

Add `--palette-only` dispatch in `tools/UiSmokeTests/Program.cs`, run `dotnet build -c Release`, then run `dotnet run --project tools/UiSmokeTests -c Release -- --palette-only`. Expected: the new assertions fail for the missing tokens.

- [ ] **Step 3: Define the Windows theme tokens**

Add canvas, raised surface, glass border, lavender-blue primary, muted text, success, warning, error, and focus-ring colors to `UiPalette`. Ensure `UiThemeController` applies them without recreating input controls. Keep translucent gradients behind surfaces only; never use blur or low-contrast glass for body text, transfer status, or focus indicators.

- [ ] **Step 4: Verify both palettes and theme switching**

Run `dotnet run --project tools/UiSmokeTests -c Release -- --palette-only`. Expected: light, dark, and system theme mappings pass without losing keyboard focus indication.

- [ ] **Step 5: Commit the Windows visual primitives**

~~~powershell
git add -- ClipBridge/Windows/UiPalette.cs ClipBridge/Windows/Ui.cs ClipBridge/Windows/UiThemeController.cs ClipBridge/Windows/AppHeaderSurface.cs ClipBridge/Windows/RoundedSurface.cs tools/UiSmokeTests/Program.cs tools/UiSmokeTests/ConversationSmoke.cs
git commit -m "feat: refresh Windows light-glass surfaces"
~~~

## Task 3: Build the compact conversation window and device-specific timeline

**Files:**
- Create: `ClipBridge/Windows/ConversationForm.cs`
- Create: `ClipBridge/Windows/ConversationTimelineControl.cs`
- Create: `ClipBridge/Windows/HistoryEntryControl.cs`
- Modify: `ClipBridge/Windows/TrayAppContext.cs`
- Modify: `tools/UiSmokeTests/ConversationSmoke.cs` and `Program.cs`

- [ ] **Step 1: Add failing shell and timeline smoke checks**

Create a temporary-directory `HistoryStore`, seed one received text record, one sent file record, and one pending transfer row, then instantiate the form with fake peer snapshots and callbacks. Assert current peer/status, empty/offline states, event order, 7-day labels, device-switch callback, and history-delete callback.

- [ ] **Step 2: Run `dotnet build -c Release` and the conversation smoke command**

Run `dotnet build -c Release`, then `dotnet run --project tools/UiSmokeTests -c Release -- --conversation-only`. Expected: reflection reports the missing `ConversationForm` type.

- [ ] **Step 3: Add the compact shell and virtualized timeline**

Use a WinForms `ListView` in virtual mode. `ConversationTimelineControl` requests entries from `HistoryStore.GetRecent(peerId, nowUtc)` and renders each row through owner-draw methods on `HistoryEntryControl`. The window header contains current peer/status, a peer selector, device-management/settings access, a separate “本地接收文件 / 打开保存位置” action, and a clear close button. The local-files action opens the existing Downloads/ClipBridge directory in Explorer and is independent of history deletion. The bottom composer accepts pasted text and attachments but requires an explicit Send action. When the first trusted peer becomes online, the host opens this window for that peer; if no trusted peer is online, it opens the pairing/device destination.

- [ ] **Step 4: Verify layout with stable and long content**

Test 1280×800 and `MinimumSize`, empty/offline data, a long device name, a 256 KiB text record, and a long legal filename. With `--capture`, save light/dark screenshots under `%TEMP%/clipbridge-ui-smoke/`. Expected: no overlap, retained scroll position after refresh, and all smoke assertions pass.

- [ ] **Step 5: Commit the conversation window**

~~~powershell
git add -- ClipBridge/Windows/ConversationForm.cs ClipBridge/Windows/ConversationTimelineControl.cs ClipBridge/Windows/HistoryEntryControl.cs ClipBridge/Windows/TrayAppContext.cs tools/UiSmokeTests/ConversationSmoke.cs tools/UiSmokeTests/Program.cs
git commit -m "feat: add compact Windows conversation workspace"
~~~

## Task 4: Add drag/copy/paste and the tray lifecycle

**Files:**
- Modify: `ClipBridge/Windows/ConversationForm.cs`, `ConversationTimelineControl.cs`, `HistoryEntryControl.cs`, and `TrayAppContext.cs`
- Modify: `tools/UiSmokeTests/ConversationSmoke.cs`

- [ ] **Step 1: Add failing action and lifecycle assertions**

Assert text Copy copies only the selected record; Ctrl+C invokes the same operation; dragging a text row exports plain text; file drag exports the stored local file path; dropping files on the send area opens the existing send-preview callback and does not send immediately; X hides the same window and leaves services alive; tray Open restores it; tray Exit disposes the app context and network services.

- [ ] **Step 2: Implement explicit copy and drag actions**

Use `Clipboard.SetText` for text, `DataObject.SetFileDropList` for received file paths, and `DoDragDrop` from text/file rows. Add a separate header action that opens the receive directory with `Process.Start` and `UseShellExecute = true`. The send area handles `DragEnter` and `DragDrop`, validates the paths through existing file-offer validation, and calls the existing preview event. It must not call `FileTransferNode.SendAsync` directly.

- [ ] **Step 3: Implement close-to-tray and explicit exit**

On `FormClosing`, set `e.Cancel = true` and call `Hide()` unless the host has set `_exiting`. The tray Open action calls `Show()`, restores `WindowState`, and activates the existing instance. The tray Exit action sets `_exiting` before `ExitThread`; it is the only action that shuts down network services.

- [ ] **Step 4: Run action and lifecycle tests**

Run `dotnet run --project tools/UiSmokeTests -c Release -- --conversation-only`. Expected: clipboard, drag/drop preview, hide/reopen, and explicit-exit assertions all pass.

- [ ] **Step 5: Commit Windows actions**

~~~powershell
git add -- ClipBridge/Windows/ConversationForm.cs ClipBridge/Windows/ConversationTimelineControl.cs ClipBridge/Windows/HistoryEntryControl.cs ClipBridge/Windows/TrayAppContext.cs tools/UiSmokeTests/ConversationSmoke.cs
git commit -m "feat: add Windows timeline actions and tray behavior"
~~~

## Task 5: Redesign pairing, device management, transfer review, and update surfaces

**Files:**
- Modify: `ClipBridge/Windows/ControlPanelForm.cs`, `ClipBridge/Windows/Devices/DevicesForm.cs`, `ClipBridge/Windows/TransfersForm.cs`, and `ClipBridge/Windows/UpdateDialog.cs`
- Modify: `tools/UiSmokeTests/DevicesFormSmoke.cs`, `UpdateDialogSmoke.cs`, and `Program.cs`

- [ ] **Step 1: Add failing callback and layout assertions**

Cover QR and IP pairing controls, pending/trusted/nearby device states, explicit file accept/reject, transfer progress/results, update checking/downloading/ready/error states, keyboard navigation, focus restoration, and theme changes. Assert each existing decision/update callback fires once; no view bypasses signature verification or transfer acceptance.

- [ ] **Step 2: Run baseline app and existing device/update smoke tests**

Run `dotnet build -c Release`, `dotnet run --project tools/UiSmokeTests -c Release -- --devices-only`, and `dotnet run --project tools/UiSmokeTests -c Release -- --updates-only`. Expected: existing behavior passes before the layout changes.

- [ ] **Step 3: Recompose screens without changing service callbacks**

Use one primary action per view, clear headings, compact status rows, pending receive decisions before completed events, and concise update progress. Keep QR image, IP text, pairing confirmation identity, selected device, and update state stable during refresh and theme changes.

- [ ] **Step 4: Run device/update smoke tests in both themes**

Run both commands from Step 2 with `--capture`. Expected: all callback/state assertions pass and screenshots show no overlap at normal and minimum widths.

- [ ] **Step 5: Commit Windows secondary surfaces**

~~~powershell
git add -- ClipBridge/Windows/ControlPanelForm.cs ClipBridge/Windows/Devices/DevicesForm.cs ClipBridge/Windows/TransfersForm.cs ClipBridge/Windows/UpdateDialog.cs tools/UiSmokeTests/DevicesFormSmoke.cs tools/UiSmokeTests/UpdateDialogSmoke.cs tools/UiSmokeTests/Program.cs
git commit -m "feat: redesign Windows pairing and update views"
~~~

## Task 6: Run final Windows verification

- [ ] **Step 1: Run the production build and transfer/history suite**

Run `dotnet build -c Release` and `dotnet run --project tools/TransferTests -c Release`. Expected: `Build succeeded` and `ALL TRANSFER TESTS PASSED`.

- [ ] **Step 2: Run all WinForms smoke suites**

Run `dotnet run --project tools/UiSmokeTests -c Release -- --palette-only`, `--conversation-only`, `--devices-only`, and `--updates-only` as four separate invocations. Expected: each prints its final `PASS` marker.

- [ ] **Step 3: Inspect screenshots and exercise real Windows targets**

Inspect light/dark, standard/minimum-size screenshots. Manually drag a received file into Explorer and a file-accepting app; paste a copied file into Explorer; copy/drag received text into a text editor; hide/reopen from the tray; and explicitly exit. Record target-app results separately from smoke tests.

- [ ] **Step 4: Review the final diff and commit only the verified UI files**

Run `git status --short` and `git diff --check`; stage only the exact Windows UI and smoke-test paths listed above. Do not stage Android, release, installer, signing, or unrelated workspace changes.


