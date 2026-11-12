# Single-window conversation workspace implementation plan

Goal: Ship a runnable local Windows preview matching the approved chat mock, with interoperable original-file/folder transfers and independent device conversations.
Architecture: A dedicated WorkspaceForm is the application host. ConversationView and DevicesView are reusable controls; existing standalone forms remain thin test/compatibility wrappers where needed. Transfer tree metadata is explicit and version-gated; file bytes and SHA-256 verification remain unchanged.
Tech stack: .NET 10 WinForms, Kotlin Android, encrypted existing TCP protocol.
Spec: docs/superpowers/specs/2026-11-10-conversation-workspace-design.md; subsequent user approvals add one file picker, folder preservation, deletion and click-to-open.

Constraints: Preserve existing working-tree edits. Work locally on current codex branch. No release, installation, remote mutations, or key changes. No subagents requested. Keep progress updates and execute inline without further design approval.

## 1. Directory-aware transfers
- [ ] Add tools/TransferTests/FolderTests.cs and --folders entry. Verify a fileTreeOffer is initially rejected and mark the test failed for unsupported feature.
- [ ] Extend TransferFile with optional RelativePath and IsDirectory. FileOffer.Type=fileTreeOffer gates tree transfers; normal fileOffer JSON remains backward compatible.
- [ ] Validate each component with ValidateFileName; reject duplicate paths, missing parents, path/file conflicts and directory payloads. Limit 256 entries and 256KiB offer frame consistently.
- [ ] Add TransferSource.Expand(paths) for deterministic folder enumeration, including empty directories; reject reparse points. Retain actual source paths separately from wire paths.
- [ ] Receive into unpredictable staging, verify every file, then commit top-level roots using no-overwrite names. Return per-entry local references. Test nested bytes, empty folders, collisions, traversal and corrupted hashes.
- [ ] Implement matching Kotlin protocol and storage, plus tree URI enumeration on Android. Use app-owned external received storage with a scoped content provider for folder files and an in-app folder browser.
- [ ] Extend optional history metadata without changing ordinary-file golden JSON; tree entries share canonical relative paths across both platforms.

## 2. Conversation control/state
- [ ] Extract ConversationView from current form into a reusable control. Keep thin ConversationForm wrapper only for existing standalone smoke flows.
- [ ] Per-peer draft object holds text, attachments, busy status and cancellation source. Send captures target and removes only successful content from that target's draft. Switching never disables peer navigation.
- [ ] File picker exposes files and folder choices. Drag/paste queues originals. Enter sends, Shift+Enter adds newline, IME composition remains safe.
- [ ] Timeline can open image/video/file/directory via shell, shows top-level folder cards, and preserves scroll by peer.
- [ ] Add UI smoke for A busy → switch B → send B → A completes without clearing B, folder attachments, offline drafts and single file action.

## 3. Workspace shell and app routing
- [ ] Add WorkspaceForm with left device list, add-device page, settings/update navigation; embedded conversation on right. No key/log panels.
- [ ] Compose persistent trusted records with live discovery by PeerId so offline devices stay visible. Track unread boundary and last selection locally.
- [ ] Extract reusable DevicesView for in-place discovery and pairing. Revoke from current-device menu after concrete confirmation; retain local files.
- [ ] Route tray, startup, activation and pairing completion to the same main window. Incoming messages do not steal selection/focus.
- [ ] Existing update check/install services and signature checks remain; installation is blocked while transfers are active.
- [ ] Validate identity for typed-text send as already done for file sends.

## 4. Verification and delivery
- [ ] dotnet build ClipBridge.csproj -c Release -o artifacts/chat-preview
- [ ] Run transfer/history/clipboard suites, directory roundtrip and new workspace smoke. Retain prior update/dialog tests.
- [ ] Android testDebugUnitTest assembleDebug from ASCII junction.
- [ ] Capture real Windows workspace UI, inspect width/layout/themes; inspect diff and report only verified outcomes.
- [ ] Document no real phone/network/installer validation when unavailable. Keep builds isolated from installed running app.

## Verification status (2026-11-10)

Implemented Windows single-window sidebar/conversation workspace; embedded device pairing; per-peer draft/send state, read boundaries and scrolling; one file/folder picker and original attachment opening. Android now has conversation list/detail navigation, matching file/folder selection, device deletion, per-peer draft/send state and IME inset handling.

Verified: Windows Release build (0 warnings/errors); conversation/workspace smoke including A-send/B-draft isolation; existing complete UI smoke; transfer and history suites; directory integrity/empty-directory/collision/corruption tests; Android unit tests and debug APK build. Kotlin/C# encrypted directory interoperability passed in both directions; this uses JVM protocol code and production Windows transport, not a real phone.

Limitations: directory offers are limited to 256 total file/directory entries and the existing 256 KiB control frame. Both endpoints must run the directory-aware version. Android tree receives use app-owned external Received storage and a read-only content provider; ordinary receives remain in Download/ClipBridge. No physical Android device connected; the available emulator stalled during graphics initialization and was stopped, so Android UI, keyboard and external viewer behavior are not visually verified. No installer, release, production-network speed test or replacement of the already running app.
