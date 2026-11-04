# Isolated Windows updater tests

Run from the repository root with .NET SDK 10.0.403:

```powershell
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-experience
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-helper
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-rollback
dotnet run --project tools/UpdateSmokeTests/UpdateSmokeTests.csproj -c Release -- --verify-relaunch
```

These checks use loopback HTTP and synthetic application copies in random temporary directories. They do not use a release private key, access GitHub, launch the real application, or update a real installation. They cover byte progress, cancellation, hash/length rejection, safe extraction, helper dependencies, file replacement, automatic-open arguments, startup confirmation, backup retention and failed-startup rollback. Failure cases never report 100%. Helper/rollback directories are retained for diagnosis.

The older `--run` mode is a separate signed-manifest integration harness that accepts explicit synthetic builds and signing material; it is not required by the checks above.

The relaunch check models a normal secondary-instance exit using isolated synthetic executables. It verifies that unconfirmed concurrent startup keeps the new files, original backup and staged files without rollback or a completed progress report. A different early-exit result still restores the old files. It never acquires the real application singleton mutex or starts an application window.
