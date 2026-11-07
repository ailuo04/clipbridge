#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'release-target.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$rawBaseUrl = $config.rawBaseUrl.TrimEnd('/')
$releaseBaseUrl = $config.releaseRepositoryUrl -replace '\.git$', ''
$envelope = Get-Content -LiteralPath (Join-Path $Directory 'latest-v2.json') -Raw | ConvertFrom-Json
$payload = [Convert]::FromBase64String($envelope.payload)
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $publicKey = [Convert]::FromBase64String(([IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'release-signing-public-key.txt'))).Trim())
    $read = 0
    $rsa.ImportSubjectPublicKeyInfo($publicKey, [ref]$read)
    if (-not $rsa.VerifyData($payload, [Convert]::FromBase64String($envelope.signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw '更新清单签名校验失败。' }
} finally { $rsa.Dispose() }
$data = [Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json
$win = $data.windows
if ($data.schema -ne 1 -or $win.version -ne $Version) { throw '清单版本不匹配。' }
$zipName = "ClipBridge-$Version-win-x64.zip"
$setupName = "ClipBridge-$Version-win-x64-Setup.exe"
foreach ($item in @(@($zipName, $win.size, $win.sha256, $win.url, "$rawBaseUrl/$zipName"), @($setupName, $win.installerSize, $win.installerSha256, $win.installerUrl, "$releaseBaseUrl/releases/download/v$Version/$setupName"))) {
    $file = Get-Item -LiteralPath (Join-Path $Directory $item[0])
    if ($file.Length -ne $item[1] -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $item[2] -or $item[3] -cne $item[4]) { throw "文件/哈希/URL 不匹配：$($item[0])" }
    Write-Output "PASS $($item[0]) hash, size and URL"
}
if ((Get-FileHash -LiteralPath (Join-Path $Directory "manifest-$Version.json")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Directory 'latest-v2.json')).Hash) { throw '不可变版本清单不匹配。' }
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory $zipName))
try {
    $names = @($zip.Entries.FullName | Sort-Object)
    $expected = @('ClipBridge.exe', 'ClipBridge.dll', 'ClipBridge.deps.json', 'ClipBridge.runtimeconfig.json', 'QRCoder.dll', 'QRCoder-LICENSE.txt' | Sort-Object)
    if (Compare-Object $names $expected) { throw 'ZIP 文件列表不匹配。' }
    $entry = $zip.GetEntry('ClipBridge.runtimeconfig.json').Open()
    try { $reader = [IO.StreamReader]::new($entry); $config = $reader.ReadToEnd() | ConvertFrom-Json } finally { $entry.Dispose() }
    if ($config.runtimeOptions.tfm -ne 'net10.0' -or @($config.runtimeOptions.frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -eq '10.0.14' }).Count -ne 1) { throw 'ZIP 运行时版本不匹配。' }
} finally { $zip.Dispose() }
Write-Output 'PASS signed manifest and update archive'

$android = $data.android
$apkName = "ClipBridge-$Version-android.apk"
$apkPath = Join-Path $Directory $apkName
$certificate = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'android/release-signing-certificate.sha256')).Trim()
if (-not $android -or $android.version -ne $Version -or $android.packageName -ne 'app.clipbridge' -or $android.versionCode -le 0 -or $android.certificateSha256 -cne $certificate -or $android.url -cne "$releaseBaseUrl/releases/download/v$Version/$apkName") { throw 'Android manifest version, certificate or URL mismatch.' }
if ((Get-Item -LiteralPath $apkPath).Length -ne $android.size -or (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash -ne $android.sha256) { throw 'Android APK size or hash mismatch.' }
Write-Output 'PASS Android APK, pinned signing certificate and signed manifest metadata'
