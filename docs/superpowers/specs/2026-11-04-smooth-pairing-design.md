# Smooth pairing and QR invitation design

## Goal

Make Android-to-Windows first pairing and subsequent reconnection easier without weakening authentication, changing existing trust records, or publishing a release. Existing 0.5.x devices must continue to discover, pair and exchange authenticated presence with the revised clients.

## User experience

- Android 17 and later must check the effective local-network permission even when the app targets API 36. If missing, present a clear user-initiated recovery path and preserve the user's choice; do not claim access is allowed when it is denied. Earlier Android versions must not request a nonexistent permission.
- Show discovery and pairing entry points before send controls on the Android device page. A found device remains untrusted until a successful pairing; the user explicitly selects it. After pairing, display it as connected only after authenticated presence.
- Windows displays a pairing invitation QR code. A selector makes the advertised IPv4 address explicit when multiple local addresses exist. Android scans on demand and requests camera access only for scanning. A camera-less or denied-permission device retains normal search and manual-IP pairing.
- The QR payload has the exact ASCII form `clipbridge:pair:1:<32-lowercase-hex-device-id>:<local-ipv4>`. It contains neither a secret nor a verification code. Parsing rejects malformed, oversized, non-local, loopback, multicast and unsupported inputs, and the fixed existing pairing port is never supplied by the QR code. The scanning client pins the advertised ID as the expected remote ID; existing short-code verification and independent approvals remain mandatory.
- An incoming pairing request is prominent without silently approving or automatically launching a background Android activity. On Windows, an actionable tray reminder opens the existing pairing window; in Android, a visible device-page prompt directs the user to the code. Both endpoints retain explicit approve and reject controls, expiry and existing cryptography.
- Search and already-trusted reconnection wake promptly on user rescan and relevant network changes. Coalesce wakeups, retain periodic advertisements and authenticated presence, and do not disrupt active file transfers by restarting the service. Never treat public discovery or QR data as authenticated.
- Distinguish inability to reach a peer, a denied local-network permission, and failure/cancellation during confirmation; provide actionable retry instructions without claiming a cause that is not known. QR does not bypass network isolation or a firewall.

## Packaging and privacy

- Add only maintained, pinned QR libraries: QRCoder on Windows and ZXing Android Embedded plus ZXing core on Android. Camera images stay on the device and are processed only while scanning.
- Because Windows updates are file archives, include the QR runtime dependency in the signed ZIP, installer, and strict release verifier; older updaters copy the additional signed archive entry without changing the manifest protocol. Never alter signature, SHA-256, size, URL, immutability or installer-permission checks.
- Do not overwrite existing releases, increment release versions, access private signing keys, or remove user files and pairing records. A debug APK must never replace the production-signed installation.

## Validation

- Unit tests: API 37 permission denied/granted including target 36; QR payload exact round trip and malformed/untrusted addresses; identity pinning and unchanged two-sided verification; deterministic refresh wakeup, coalescing and cleanup; connection error classification.
- Integration: C#/JVM loopback interop, existing transfer/pairing security regressions, Android tests/lint/debug build, Windows build, both smoke-test project builds, release-publisher tests, installer policy and archive dependency layout. Synthetic tests never access real clipboard, real pairing keys or live device trust stores.
- Connected-device validation requires a separately installed test variant or a properly release-signed update; never uninstall or overwrite the installed release with an incompatible debug signature. Report real-device, camera, actual NTU isolation and release-signing limitations honestly.