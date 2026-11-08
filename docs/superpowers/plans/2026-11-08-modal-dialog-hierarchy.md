# Modal Dialog Hierarchy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make update content easier to read and present device discovery and pairing as a blurred-backdrop modal glass dialog owned by the main window.

**Architecture:** Extract the current cached owner capture/downsample blur from UpdateBackdropSurface into a neutral ModalBackdropSurface. Reuse it behind the update card and the device modal. Keep DeviceHub, pairing state, and protocol unchanged; put the existing DevicesForm workspace inside a centered glass surface on an owner-sized modal overlay.

**Tech Stack:** .NET 10, Windows Forms, GDI+, existing UiTheme/UiPalette drawing helpers, reflection-based tools/UiSmokeTests.

---

## File Map

- Create ClipBridge/Windows/ModalBackdropSurface.cs for cached owner capture, low-resolution blur, tint, fallback, and safe bitmap retirement.
- Modify ClipBridge/Windows/UpdateDialog.cs to use the shared backdrop without changing update lifecycle or security behavior.
- Modify ClipBridge/Windows/UpdateCardSurface.cs to tune glass opacity, highlight, borders, and section contrast for light and dark themes while preserving text and progress geometry.
- Modify ClipBridge/Windows/Devices/DevicesForm.cs to provide an owner-sized borderless modal overlay, centered glass content shell, existing workspace hosting, and owner resize/reposition/backdrop lifecycle.
- Modify ClipBridge/Windows/TrayAppContext.cs to open DevicesForm with ShowDialog(_panel).
- Modify tools/UiSmokeTests/DevicesFormSmoke.cs for modal ownership, backdrop, centering, resizing, dismissal, and existing pairing behaviors.
- Modify tools/UiSmokeTests/UpdateDialogSmoke.cs for shared backdrop capture reuse and readable themed glass surface without update-state regressions.
- Remove ClipBridge/Windows/UpdateBackdropSurface.cs after migrating callers and smoke lookups, unless a compatibility wrapper is required by a remaining caller.

## Task 1: Add Modal Backdrop and Modal Behavior Tests

**Files:** Test tools/UiSmokeTests/DevicesFormSmoke.cs and tools/UiSmokeTests/UpdateDialogSmoke.cs.

- [x] Add device overlay checks using a visible owner form and a scheduled modal close. Assert that DevicesForm is modal and owned by the owner, bounds match the owner's client bounds, the backdrop is visible, and the content shell is centered and contained. After close, assert that owner input is restored.
- [x] Add light/dark backdrop checks: one capture builds the cached image, an unchanged resize does not rebuild repeatedly, and the image matches overlay dimensions.
- [x] Run dotnet build tools/UiSmokeTests/UiSmokeTests.csproj -c Release and dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --devices-only. Confirm new assertions fail because DevicesForm has no owner-sized backdrop or glass surface.

## Task 2: Extract the Shared Backdrop Surface

**Files:** Create ClipBridge/Windows/ModalBackdropSurface.cs; modify ClipBridge/Windows/UpdateDialog.cs; remove or adapt ClipBridge/Windows/UpdateBackdropSurface.cs; update tools/UiSmokeTests/UpdateDialogSmoke.cs lookup.

- [x] Move capture, two-stage downsampling, theme tint, paint fallback, bitmap retirement, Release, and Dispose behavior into ModalBackdropSurface. Retain CaptureOwner(Control?), Image, and Builds semantics.
- [x] Change UpdateDialog's backdrop field and smoke accessor to ModalBackdropSurface; keep owner move/resize tracking and update lifecycle unchanged.
- [x] Crop the update card's cached backdrop in owner-client coordinates; verify a known corner pixel comes from the matching owner image region even when the owner is offset on screen.
- [x] Run dotnet build ClipBridge.csproj -c Release and the update-only UI smoke command; verify backdrop capture caching, theme switching, and update lifecycle tests pass.

## Task 3: Improve Update Glass Readability and Hierarchy

**Files:** Modify ClipBridge/Windows/UpdateCardSurface.cs; test tools/UiSmokeTests/UpdateDialogSmoke.cs.

- [x] Update DrawGlass so the outer glass has a more opaque theme-aware base, restrained highlight and rim, and stronger separation for the release-notes reading area and security/progress region. Keep existing card bounds, typography, notes scrolling, and progress coordinates.
- [x] Add light/dark smoke assertions that card chrome is rebuilt on theme change, foreground content and actions remain within bounds, and the update background remains visible but subordinate in captured screenshots.
- [x] Run dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --updates-only --capture. Inspect generated light/dark screenshots and verify update states, cancel/install protection, notices, and keyboard reading remain intact.

## Task 4: Convert DevicesForm to an Owner-Sized Modal Glass Overlay

**Files:** Modify ClipBridge/Windows/Devices/DevicesForm.cs and ClipBridge/Windows/TrayAppContext.cs.

- [x] Host the existing header, scroll area, and footer in one centered glass content panel. Keep QR, discovery, request, and trusted-device controls as live WinForms controls so QR remains scannable and text stays sharp.
- [x] Make the borderless outer form cover the owner's client rectangle, paint a cached ModalBackdropSurface behind the content panel, recenter on owner resize, and recapture after resize settles. Cap content dimensions to the owner client area and keep lists scrollable on small windows.
- [x] Open the overlay through ShowDialog(_panel); verify close/cancel restores focus and owner input. Preserve refresh timer start/stop, DeviceHub event unsubscribe, pairing status dispatch, network refresh, and pairing callbacks.
- [x] Dispose each modal form after ShowDialog returns, stop polling on close, and remove DeviceHub/owner event subscriptions; test that closing leaves no polling timer or status subscription.
- [x] Run device smoke tests and verify modal overlay, QR, nearby/trusted/pending rows, manual-IP validation, expiry, revoke, scroll retention, both themes, and small-owner layout.

## Task 5: Full Verification and Visual Review

**Files:** No new production files; adjust implementation and smoke tests only when a reproduced issue requires it.

- [x] Run dotnet build ClipBridge.csproj -c Release.
- [x] Run dotnet build tools/UiSmokeTests/UiSmokeTests.csproj -c Release, then run dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --updates-only --capture and dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj -c Release -- --devices-only --capture separately.
- [x] Inspect update and device modal screenshots in light/dark themes at standard and 1.5x scaling. Confirm the background is blurred, foreground remains crisp, content hierarchy is distinct, and no controls overlap or clip.
- [x] Report unverified boundaries explicitly: UI smoke tests do not establish real LAN pairing, live device discovery, signed network update delivery, or installer behavior.
