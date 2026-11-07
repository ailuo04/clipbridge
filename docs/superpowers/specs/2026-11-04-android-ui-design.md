# Android UI refinement

Approved by the user: retain the three device/transfer/settings destinations, redesign the hierarchy and shared visual system, and unify in-app confirmation sheets. No framework migration or protocol/release change.

## Pages
- Devices: pending pairing decisions first when present; selected device/status and explicit send actions before discovery. Scan and refresh remain clear connection tools. Saved devices and offline states are readable.
- Transfers: pending accepts/rejects, in-progress transfers, finished transfers and unsuccessful transfers are distinct. Byte progress uses the real transfer counters, not simulated progress.
- Settings: device profile, appearance/connection, update state/options, trusted-device management. IP and legacy shared-key controls are folded into advanced settings.

## Visual and interaction system
Use the existing native Android Views with a soft background, locally painted glass gradients, bordered cards, violet primary actions, capsule navigation and simple vector glyphs. Preserve light/dark/system preferences. Reflow side-by-side actions into a vertical layout for narrow screens or large text. Minimum interactive height is 48dp; body text and device names wrap. No new rendering/dependency framework.

In-app lists, text/file previews, name/IP/key inputs and revoke confirmations use one scrollable keyboard-aware bottom sheet. System permission, document picker, camera scanning and package installation screens remain system-controlled. No background clipboard reads or automatic launches. All existing user confirmation callbacks remain in place.

## Validation
Pure presentation rules have JVM regression tests. Run all debug unit tests, lintDebug and assembleDebug without signing secrets. As required by the Android README, do not run UI smoke tests, an emulator, installers or application windows. Report that physical-device rendering and behavior remain unverified. No debug APK is a formal release. Preserve existing workspace changes and do not touch github-public.
