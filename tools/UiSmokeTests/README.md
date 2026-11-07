# UI smoke test

Build the recovered client first, then run this Windows-only interactive test:

```powershell
dotnet build .\ClipBridge.csproj --configuration Release
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release
```

The test creates a standalone control-panel form with a fake key; it does not
start the tray app, clipboard listener, synchronization service, update check,
or write the real theme preference. It opens, minimizes and restores a window,
so run it in an interactive desktop session. Pass `-- --capture` to save light
and dark screenshots under the system temporary directory for visual review.

The suite also opens the device and pairing window using an in-memory trust store
and a discovery-disabled loopback hub. It checks refresh-safe address input,
scroll position, light/dark colors, resizable layouts, request expiry, and local
address validation without reading real device records. To run just these checks:

```powershell
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --devices-only --capture
```

The inline QR tests use synthetic network adapters to cover offline startup,
local-address filtering, default address selection, address changes, bitmap
reuse, dark-theme contrast, and recovery after a network failure. Capture also
saves inline-qr-rendered.png, which can be decoded by tools/QrInterop/DecodeQr.java
using the cached Android ZXing dependency.

The updater checks cover version/notes visibility, progress, cancel/retry, installation close protection, theme changes and enlarged layout. To run just them:

```powershell
dotnet run --project tools/UiSmokeTests/UiSmokeTests.csproj --configuration Release -- --updates-only --capture
```

Screenshots are saved as update-light.png, update-dark.png, update-*-error.png and update-light-scaled.png in the same temporary capture directory. They use synthetic update metadata and never install an update.

The refined update window also verifies custom rounded chrome, accessible close behavior, keyboard scrolling for long release notes, and unclipped titles/100% labels. Ready-state captures are update-light-ready.png and update-dark-ready.png. Captures preserve the actual rounded window region.

Final-state checks also cover persistent failure colors, confirmed completion, protected independent installer state, and compact latest-version/check-error notices. Additional captures: update-*-installer.png, update-*-latest.png and update-*-check-error.png.
The Windows dialogs verify the shared frosted status capsule in success, warning, error, progress, and info states, including light/dark rendering and accessible status text. Latest-version notices omit duplicate result copy; startup failures use the themed notice. Device smoke tests cover pending, locally accepted, and expired pairing states while preserving verification codes and action behavior. Device capture also saves `devices-request-light.png`.
