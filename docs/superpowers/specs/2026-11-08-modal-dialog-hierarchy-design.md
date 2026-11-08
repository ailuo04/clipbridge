# ClipBridge Modal Dialog Hierarchy Design

## Goal

Improve visual hierarchy in the Windows update dialog and make device discovery and pairing a secondary modal dialog over the main ClipBridge window. Both dialogs use the selected B direction: a frosted-glass foreground with a blurred, dimmed owner backdrop. Foreground text, QR codes, and actions remain crisp and readable.

## Scope

- Keep the update dialog owner-sized and centered over its owner, preserving its current update stages, cancellation rules, accessibility behavior, and signed-update security requirements.
- Rework its glass treatment so the title, content, progress or result, and actions read as distinct levels. Increase glass opacity and text contrast enough that owner content does not compete with the dialog.
- Change the device window from a modeless standalone window into a modal secondary dialog owned by the main window.
- Keep QR pairing, nearby-device discovery, trusted devices, incoming pairing requests, and manual IP fallback available in the device dialog. Preserve polling, refresh, pairing confirmation, revoke, and scroll behavior.
- Give the device dialog an owner-captured, softly blurred and dimmed backdrop and a centered glass content surface. Keep its foreground content crisp; do not blur the QR image or live controls.
- Keep both light and dark themes and DPI scaling supported.

## Interaction and Visual Hierarchy

The owner remains visible as context but is visually subdued by a cached low-resolution blur and a theme-aware tint. The glass foreground uses a stronger opaque base than the current update card, a restrained rim/highlight, and a clear shadow. Avoid nested translucent cards: use one main surface, thin dividers, and grouped content regions.

The device dialog is opened modally from the main window. Closing it returns focus to the owner. Its QR and device lists remain usable while discovery and incoming pairing state update. Long lists scroll inside the modal surface. Existing pairing actions continue to update DeviceHub exactly as they do now.

## Implementation Boundaries

Reuse the update backdrop capture/blur behavior where practical, but do not couple device pairing state to update-dialog classes. Prefer a small shared backdrop surface only if it can be reused without changing update lifecycle behavior; otherwise keep the device backdrop implementation local. Do not change the pairing protocol, update manifest, signing, download, or installer logic.

## Validation

- Extend UI smoke coverage for owner-sized/modal positioning, backdrop presence and cache reuse, front-surface readability/layout containment, light/dark theme, and device modal open/close behavior.
- Preserve existing device discovery, QR address selection, pairing confirmation/expiry, revoke, and manual-IP tests.
- Build the main Windows project and UI smoke project; run the relevant UI smoke tests with screenshot capture and inspect the screenshots.
- Do not claim real device/network pairing or real update delivery was verified by UI smoke tests.
