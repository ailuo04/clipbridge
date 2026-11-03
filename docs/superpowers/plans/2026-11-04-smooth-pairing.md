# Smooth Pairing and QR Invitation Implementation Plan

**Goal:** Reduce friction for first pairing and authenticated reconnection while adding an optional Windows-to-Android QR invitation.

**Architecture:** Preserve the existing pairing handshake and trust store. Add strict invitation parsing and QR presentation, permission-aware Android UX, coalesced discovery refresh, and explicit incoming-request feedback. Include the pinned Windows QR dependency in both installer and signed update ZIP.

**Tech stack:** .NET 10 WinForms, Kotlin/Android API 29+, QRCoder, ZXing Android Embedded, existing C#/JVM tests and PowerShell release tools.

## Work sequence

1. **Permission regression first:** extend `android/app/src/test/java/app/clipbridge/protocol/CompatibilityTest.kt` so API 37 / target 36 with missing access is rejected and earlier devices remain unaffected. Run the focused test and observe the old implementation fail. Update `PlatformCompat.kt` and the Android entry points to present permission recovery without bypassing user approval; rerun the focused test.
2. **Invitation contract:** add tests for fixed valid C#/Kotlin vectors and rejection of malformed IDs, oversized text, public/loopback addresses and unsupported versions. Implement a minimal `PairingInvitation` parser/formatter on both platforms; pass the decoded expected ID into the existing session handshake. Re-run both suites.
3. **QR display and scanning:** add pinned QRCoder and ZXing dependencies, Windows local-address selection and QR rendering, Android user-initiated scanner and validated pairing entry. Keep camera permission optional and requested only by scanning. Verify encoded QR decoding independently and cancellation/invalid input behavior.
4. **Discovery and UI:** add coalesced immediate discovery refresh for network changes and manual search on both endpoints, clean up callbacks, bring devices and confirmation to the foreground of the app UI, add an actionable Windows tray reminder, and classify connection failures. Add regression tests before each behavior change.
5. **Archive integrity:** add `QRCoder.dll` to the Windows installer, update archive builder and strict verifier, then verify the published build contains exactly the required runtime files and test publisher/installer policy invariants. Do not change signing verification.
6. **End-to-end verification:** run Windows build, Android unit tests/lint/debug build, C#/JVM pairing interop, transfer/security regressions, both smoke project builds, installer policy and publisher tests. Review the diff and report explicitly what was not tested on the live device or real NTU network.

All tests use synthetic data and loopback unless the user explicitly consents to a separate real-device test. Preserve existing unrelated worktree changes and never overwrite release assets. Publish only with explicit user authorization.