# UI smoke test

Covers the shipping UI only. The legacy control panel and its standalone
windows were removed; the app now runs entirely on `WorkspaceForm`.

Build the client first, then run this Windows-only interactive test:

```powershell
dotnet build .\ClipBridge.csproj --configuration Release
dotnet run --project .\tools\UiSmokeTests\UiSmokeTests.csproj --configuration Release -- --capture
```

The suite never starts the tray app, clipboard listener, synchronization
service or update check, and it never writes the real theme preference or
device records. It opens, minimizes and restores windows, so run it in an
interactive desktop session. `--capture` saves light and dark screenshots to
`%TEMP%\clipbridge-ui-smoke` for visual review.

## Suites

| Flag | Scope |
| --- | --- |
| *(none)* | Retirement guard, palette, theme persistence, then all suites below |
| `--conversation-only` | Workspace shell, conversation view, timeline, composer |
| `--palette-only` | Palette opacity and canvas/ink separation in both themes |
| `--devices-only` | Device page and inline pairing QR |
| `--updates-only` | Update dialog |

Set `CLIPBRIDGE_TEST_DLL` to test a build outside the default
`bin/Release/net10.0-windows/ClipBridge.dll`.

## What is asserted

The retirement guard fails if any of the twenty removed legacy UI types
reappears in the assembly, so a stale file cannot reintroduce a second,
divergent interface.

The workspace suite drives the real shell: sidebar device rows and unread
badges, day markers between message groups, incoming/outgoing bubble
alignment, context-menu actions, scroll preservation, paste and drop
attachments, send success/failure handling, draft isolation per device,
settings and rollback navigation, the offline connection chip, and the
disabled send state. Layout is checked at the minimum window size in both
themes.

The device suite hosts `DevicesView` in a plain form the way the workspace
does, using an in-memory trust store and a discovery-disabled loopback hub.
It checks refresh-safe address input, scroll position, light/dark colors,
resizable layouts, request expiry and local address validation. The inline
QR tests use synthetic network adapters to cover offline startup,
local-address filtering, default selection, address changes, bitmap reuse,
dark-theme contrast and recovery after a network failure.

The updater suite covers version/notes visibility, progress, cancel/retry,
installation close protection, theme changes and enlarged layout, using
synthetic update metadata. It never installs anything.

## Captures

Captures wait for any in-flight page cross-fade to hand over before shooting,
so they show the live page rather than the transition overlay. Key files:
`workspace-chat.png`, `workspace-chat-offline.png`,
`workspace-chat-{light,dark}-compact.png`, `conversation-day-groups.png`,
`devices-{light,dark}.png`, `update-{light,dark}*.png`.