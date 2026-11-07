# Device and pairing window refresh

The device window should share the existing control panel's visual language: rounded surfaces, restrained blue accents, modern buttons, clear type hierarchy, and light/dark palettes. Keep the native resizable Windows frame.

Use a fixed page heading with a discovery action, followed by a compact QR invitation card with an expandable IP form. Put pending confirmations first in the scrolling content, then trusted devices and nearby devices. Each device row has a platform icon, a bounded name, readable connection state, address/platform metadata, and one right-aligned action. Empty states explain the next step in a compact card. Show device counts and a quiet persistent status footer.

Build the QR and IP controls once. Discovery changes only rebuild device and confirmation sections, retaining IP text, selection, focus, and viewport position. Include names and platforms in change detection. Expired confirmations cannot be approved. Route hub status messages to the UI thread and unsubscribe on disposal. Theme changes recolor the open window without recreating its inputs.

Validate with an isolated in-memory device store and a loopback-only hub. Cover populated/empty lists, narrow/wide layouts, long names, refresh while editing, rejected IP input, metadata changes, connection states, expired/pending/completed confirmations, cancellation, and light/dark rendering. Capture actual WinForms screenshots. Existing protocol, signing, installation, and release behavior are outside this change.

## Inline QR revision

Display a scannable QR directly in the connection card on opening. Put concise phone instructions and a local network address selector beside it; preserve the selected address and bitmap while discovery or theme refreshes. The QR stays black on white in both themes and follows local network changes. No extra display button or QR dialog is needed. Keep manual IPv4 entry hidden behind a clearly labelled backup option and retain its validation and input state.
