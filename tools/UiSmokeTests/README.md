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
