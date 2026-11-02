#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ProjectDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'android'),
    [string]$GradlePath,
    [string]$AndroidSdkRoot = $env:ANDROID_HOME
)
$ErrorActionPreference = 'Stop'
if (-not $AndroidSdkRoot) { $AndroidSdkRoot = $env:ANDROID_SDK_ROOT }
if (-not $AndroidSdkRoot) { throw 'Android SDK path is required.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Android output directory already exists; use a new directory.' }
$temporaryKey = $null
$originalKeyPath = $env:CLIPBRIDGE_ANDROID_KEYSTORE_PATH
try {
    if ($env:CLIPBRIDGE_ANDROID_KEYSTORE_BASE64) {
        $temporaryKey = Join-Path ([IO.Path]::GetTempPath()) ('clipbridge-android-' + [Guid]::NewGuid().ToString('N') + '.p12')
        [IO.File]::WriteAllBytes($temporaryKey, [Convert]::FromBase64String($env:CLIPBRIDGE_ANDROID_KEYSTORE_BASE64))
        $env:CLIPBRIDGE_ANDROID_KEYSTORE_PATH = $temporaryKey
        $env:CLIPBRIDGE_ANDROID_KEYSTORE_BASE64 = $null
    }
    if (-not $env:CLIPBRIDGE_ANDROID_KEYSTORE_PATH -or -not $env:CLIPBRIDGE_ANDROID_KEYSTORE_PASSWORD) { throw 'Persistent Android release signing key and password are required.' }
    if (-not (Test-Path -LiteralPath $env:CLIPBRIDGE_ANDROID_KEYSTORE_PATH -PathType Leaf)) { throw 'Android release keystore is unavailable.' }
    if (-not $GradlePath) { $GradlePath = Join-Path $ProjectDirectory 'gradlew.bat' }
    Push-Location $ProjectDirectory
    try {
        & $GradlePath testDebugUnitTest lintRelease assembleRelease --no-daemon --max-workers 2 --console plain
        if ($LASTEXITCODE -ne 0) { throw 'Android tests, lint or release build failed.' }
    } finally { Pop-Location }
    $apk = Join-Path $ProjectDirectory 'app/build/outputs/apk/release/app-release.apk'
    if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) { throw 'Signed Android release APK was not generated.' }
    $tools = Join-Path $AndroidSdkRoot 'build-tools/36.0.0'
    $certLines = & (Join-Path $tools 'apksigner.bat') verify --verbose --print-certs $apk
    if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
    $certificateLine = $certLines | Where-Object { $_ -match '^Signer #1 certificate SHA-256 digest: ([0-9a-fA-F]{64})$' } | Select-Object -First 1
    if (-not $certificateLine) { throw 'Missing APK signer certificate.' }
    $certificate = ($certificateLine -split ': ', 2)[1].ToLowerInvariant()
    $expectedCertificate = [IO.File]::ReadAllText((Join-Path $ProjectDirectory 'release-signing-certificate.sha256')).Trim()
    if ($certificate -cne $expectedCertificate) { throw 'APK signer differs from the pinned release certificate.' }
    $badging = (& (Join-Path $tools 'aapt.exe') dump badging $apk) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Android package metadata.' }
    if ($badging -notmatch "package: name='app.clipbridge' versionCode='(\d+)' versionName='([^']+)'") { throw 'Android package ID or version metadata is invalid.' }
    $versionCode = [int]$Matches[1]
    if ($Matches[2] -ne $Version) { throw 'Android APK version does not match the release version.' }
    if ($badging.Contains('application-debuggable')) { throw 'Debuggable APK cannot be published.' }
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $name = "ClipBridge-$Version-android.apk"
    Copy-Item -LiteralPath $apk -Destination (Join-Path $OutputDirectory $name)
    $metadata = [ordered]@{
        version = $Version
        versionCode = $versionCode
        packageName = 'app.clipbridge'
        url = "https://github.com/ailuo04/clipbridge/releases/download/v$Version/$name"
        size = (Get-Item -LiteralPath $apk).Length
        sha256 = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
        certificateSha256 = $certificate
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'android-release.json'), ($metadata | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    Write-Output "PASS Android $Version release signature, certificate, version and non-debuggable APK"
} finally {
    if ($temporaryKey -and (Test-Path -LiteralPath $temporaryKey -PathType Leaf)) { Remove-Item -LiteralPath $temporaryKey -Force }
    $env:CLIPBRIDGE_ANDROID_KEYSTORE_PATH = $originalKeyPath
    $env:CLIPBRIDGE_ANDROID_KEYSTORE_BASE64 = $null
    $env:CLIPBRIDGE_ANDROID_KEYSTORE_PASSWORD = $null
}
