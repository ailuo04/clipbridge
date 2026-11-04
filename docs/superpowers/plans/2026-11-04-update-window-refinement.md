# Update window visual refinement

- [x] Capture v1 and add failing checks for borderless chrome, custom close and release-note rendering.
- [x] Draw the glass emblem, translucent surfaces, pill actions and compact stage indicators.
- [x] Rebuild header/content/footer hierarchy; retain cancellation, install protection, keyboard access and restart behavior.
- [x] Validate large title/100% label fit, long-note scrolling, themes and enlarged layout; review actual captures.
- [x] Run main Release build, complete UI smoke suite, updater progress/staging and standalone helper restart checks.

The visual scope is the Windows update window; no release/version/signing changes. The enlarged layout check is simulated control scaling, not a physical multi-monitor test.

## Final state and compact-notice checks

- Failure status color survives a theme refresh.
- Confirmed completion uses a finished title and permits closing.
- Independent installer starts at the protected waiting stage; failure does not offer an unavailable retry.
- Latest-version and check-failure notices use a compact layout without an empty progress area.
- Main Release build and UpdateSmokeTests build: passed, zero warnings/errors.
- Complete UI suite, --verify-experience, --verify-helper and --verify-rollback: passed.
- Final screenshots: D:\互传\artifacts\update-window-refined\preview.png and notices-preview.png.
- This remains a local implementation. No real installed-client/GitHub network upgrade or release was performed; enlarged-layout tests simulate scaling rather than physically switching monitors.
