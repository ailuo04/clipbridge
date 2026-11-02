# Android experience implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan in the current isolated checkout. The user has approved the design and authorized implementation; continue without an additional workflow-selection question.

**Goal:** Deliver signed-update support, the approved three-page Android interface, and secure LAN discovery/pairing on Android and Windows.

**Architecture:** Separate public discovery, commitment-based pairing, protected per-device trust, and authenticated business sessions. Preserve legacy frames and keys. Keep updates independently verified and every background event quiet.

**Tech Stack:** Kotlin / Android platform Views and JobScheduler, C# / .NET 10 / Windows Forms, platform P-256 ECDH and AES-GCM, existing Gradle and GitHub release tools.

---

- [x] 1. Add failing headless tests for public discovery parsing, paired routing isolation, commitment mismatch, short-code agreement, rejected pairing, and signed Android update metadata. Tests use synthetic identities and keys only. Run `dotnet run --project tools/TransferTests -c Release` and Gradle unit tests; observe missing-feature failures.
- [x] 2. Implement matching `PairingProtocol`, `DeviceDiscovery`, and `TrustedChannel` modules in C# and Kotlin. Fix canonical wire representation: big-endian length-prefixed UTF-8 fields; 32-hex device IDs; P-256 SPKI keys; SHA-256 commitments; HKDF-SHA256 keys; six-digit SAS. New business connections authenticate source/target/type header as AES-GCM AAD and derive a fresh session key. Run protocol tests.
- [x] 3. Add protected trust stores and bounded pairing coordinators. Persist pending commits separately, require both explicit decisions, and recover interrupted final confirmation only with proof of the new key. Route public advertisements separately from authenticated presence; announce on directed broadcasts and age devices out. Bind 45824 for pairing. Cover cancellation, replay and refusal in tests.
- [x] 4. Integrate Windows text/file nodes with negotiated per-device sessions, stable device identity and legacy fallback. Add an explicitly opened device/pairing manager to the control panel and tray, including code confirmation, paired-device revoke and initiate-pairing actions. Never show the manager in a network callback.
- [x] 5. Integrate Android discovery before pairing, per-device business sessions and pairing decisions into BridgeService. Preserve manual clipboard and incoming-file confirmation. Add network interface refresh, connection stop and per-device revoke cleanup.
- [x] 6. Add `updates/UpdateProtocol.kt`, `UpdateRepository.kt`, `UpdateJobService.kt` and a private APK content provider. Embed the existing public key and certificate pin. Verify RSA, package/version, HTTPS, length/hash and installed/APK signer identity. Raw failure may fall back to the same GitHub Contents API. Schedule quiet daily checks and unmetered downloads; install only from an explicit foreground button.
- [x] 7. Replace MainActivity's vertical form with themed device/transfer/settings pages matching the approved mockup. Add selected-target cards, discovery empty states, pairing and pending requests, preview/send controls, readable sizes/progress, settings and update state. Support dark mode, large text, insets and system shares.
- [x] 8. Run headless protocol and C#/JVM interoperability tests; build Windows and both smoke-test projects (do not execute UI tests); run Android unit tests, lint and assemble; run publisher synthetic tests and installer-policy checks. Fix actual failures, review the final diff, update docs, and prepare signed local Android/Windows artifacts with existing release keys. Report exact verification limits; no device/emulator/UI launches.

Commands run from D:/互传/github-public unless specified. Android commands run through C:/Users/ailuo/.codex/cache/clipbridge-android/ascii-workspace/android with JDK 17 and SDK 36. Windows outputs use local artifacts only; never push origin or alter published 0.4.0 assets.
