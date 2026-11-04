# Device Window Implementation Plan

**Goal:** Make the Windows device and pairing window consistent with ClipBridge's main window and reliable while discovery refreshes.

**Architecture:** Reuse ModernButton, RoundedSurface, IconTile, CountPill, TextBoxShell, and Ui. Keep stable page/input controls and rebuild only dynamic device sections from DeviceHub snapshots. Use the current theme palette and dispose timers, event subscriptions, and owned fonts.

**Tech stack:** .NET 10 WinForms and the existing standalone UI smoke harness.

1. Add isolated device-window smoke coverage in tools/UiSmokeTests/DevicesFormSmoke.cs, with an in-memory DispatchProxy store and DeviceHub(discovery: false, pairingPort: 0). Add --devices-only routing to the existing test runner. Run against the current application and confirm refresh-safe manual input is missing.
2. Replace DevicesForm layout with a heading, scrollable compact connection card, expandable IP form, confirmation section, counted trusted/nearby sections, and a status footer. Use bounded labels and docking so long names and small widths cannot expand the content horizontally.
3. Preserve the stable IP controls during device refresh, include visible metadata in snapshot comparison, disable expired confirmations, marshal status notifications, and recolor the existing controls on theme changes. Connect the open device form to the tray theme callback.
4. Build ClipBridge and both existing smoke-test projects. Run device-only and full UI smoke tests with screenshot capture. Inspect actual light/dark/minimum-width renders and fix clipping or overlap. Run transfer regressions if device/pairing behavior changes. Review the final diff and leave unrelated work in place.

Commands: dotnet build ClipBridge.csproj -c Release; dotnet build tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release; dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --devices-only --capture; dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --capture; dotnet run --project tools/TransferTests/TransferTests.csproj -c Release.

## Verification completed on November 4, 2026

- ClipBridge Release build: passed, zero warnings and errors.
- UpdateSmokeTests Release build: passed, zero warnings and errors.
- Full UiSmokeTests run with capture: ALL TESTS PASSED, including the device window checks.
- TransferTests run: ALL TRANSFER TESTS PASSED, including explicit two-party pairing, identity pinning, refusal, revocation, discovery, authenticated routing, and file integrity cases.
- git diff --check on edited tracked files: passed.
- Actual WinForms captures reviewed at 125% display scale for light, dark, IP entry, pending confirmation, empty lists, and minimum window width. Resizing and long names are checked without horizontal scrolling or overlapping sections; refresh retains the IP editor, its selection and focus, and the scroll position. Hub status delivery and event unsubscription are verified.
- Preview images saved in artifacts/ui/ (ignored build outputs). Screenshots use synthetic device records; no real pairing store, clipboard, installed application, or GitHub Release was changed.

## Inline QR follow-up

1. Extend the device smoke checks to require an immediately visible QR and an explicitly labelled, initially hidden IP backup option; confirm the current display-button implementation fails.
2. Add a focused PairingQrPanel with local-address enumeration, address selection, cached QR rendering, readable offline state, and bitmap disposal. Embed it directly into the connection card and refresh network addresses on opening, rescan, and periodically while visible.
3. Verify address switching and empty-network recovery with synthetic network choices, confirm black-on-white contrast across themes, and retain the existing layout/input/confirmation checks. Build the app, run the full UI suite with capture, and inspect the actual inline QR layouts.

Inline QR revision verification: ClipBridge Release and UpdateSmokeTests builds passed with zero warnings/errors. The full UI suite passed, including 11 synthetic network/QR checks and existing device/input/layout/confirmation checks. Android ZXing core 3.5.3 independently decoded the PictureBox-rendered synthetic invitation. Light, dark, narrow and expanded-IP screenshots reviewed and saved to artifacts/ui/. Real phone-camera scanning was not performed.
