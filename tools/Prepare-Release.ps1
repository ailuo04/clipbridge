#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Notes,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$PrivateKeyPath = (Join-Path $env:USERPROFILE '.clipbridge-release-keys\clipbridge-update-private.pem'),
    [string]$InnoCompilerPath = (Join-Path $env:LOCALAPPDATA 'ClipBridgeBuildTools\InnoSetup\ISCC.exe')
)

$ErrorActionPreference = 'Stop'
if ($Notes.Length -gt 4000) { throw '更新说明不能超过 4000 个字符。' }
$sourceRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $sourceRoot 'ClipBridge.csproj'
$installerScript = Join-Path $PSScriptRoot 'ClipBridge-Setup.iss'
$publicKeyPath = Join-Path $sourceRoot 'release-signing-public-key.txt'
$releaseConfigPath = Join-Path $sourceRoot 'release-target.json'
foreach ($file in @($project, $installerScript, $publicKeyPath, $PrivateKeyPath, $InnoCompilerPath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "缺少打包所需文件：$file" }
}
$releaseConfig = Get-Content -LiteralPath $releaseConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$rawBaseUrl = $releaseConfig.rawBaseUrl.TrimEnd('/')
$releaseBaseUrl = $releaseConfig.releaseRepositoryUrl -replace '\.git$', ''
if (Test-Path -LiteralPath $OutputDirectory) { throw '输出目录已存在；为避免覆盖文件，请指定一个新目录。' }
$publicBytes = [Convert]::FromBase64String((Get-Content -LiteralPath $publicKeyPath -Raw).Trim())
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText($PrivateKeyPath))
    $actualPublic = $rsa.ExportSubjectPublicKeyInfo()
    if (-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($actualPublic, $publicBytes)) { throw '发布私钥与应用内公钥不匹配。' }

    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $output = (Resolve-Path -LiteralPath $OutputDirectory).Path
    $build = Join-Path $output 'app'
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained false "-p:Version=$Version" '-p:RuntimeFrameworkVersion=10.0.14' '-p:DebugType=None' '-p:DebugSymbols=false' --output $build --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败：$LASTEXITCODE" }
    $builtVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $build 'ClipBridge.dll')).Version.ToString(3)
    if ($builtVersion -ne $Version) { throw "应用版本不匹配：$builtVersion" }

    & $InnoCompilerPath '/Qp' "/DAppVersion=$Version" "/DBuildDir=$build" "/DOutputDir=$output" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "安装器编译失败：$LASTEXITCODE" }
    $zipName = "ClipBridge-$Version-win-x64.zip"
    $setupName = "ClipBridge-$Version-win-x64-Setup.exe"
    $zipPath = Join-Path $output $zipName
    $setupPath = Join-Path $output $setupName
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw '未生成安装程序。' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @('ClipBridge.exe', 'ClipBridge.dll', 'ClipBridge.deps.json', 'ClipBridge.runtimeconfig.json')) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $build $name), $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }

    $zip = Get-Item -LiteralPath $zipPath
    $setup = Get-Item -LiteralPath $setupPath
    $payload = [ordered]@{
        schema = 1
        generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
        windows = [ordered]@{
            version = $Version
            url = "$rawBaseUrl/$zipName"
            size = $zip.Length
            sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
            installerUrl = "$releaseBaseUrl/releases/download/v$Version/$setupName"
            installerSize = $setup.Length
            installerSha256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
            notes = $Notes
        }
        android = $null
    }
    $payloadBytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Depth 8 -Compress))
    $signature = $rsa.SignData($payloadBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    if (-not $rsa.VerifyData($payloadBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw '更新清单签名验证失败。' }
    $envelope = [ordered]@{ payload = [Convert]::ToBase64String($payloadBytes); signature = [Convert]::ToBase64String($signature) } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $output 'latest-v2.json'), $envelope, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output 'release-notes.txt'), $Notes, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output "manifest-$Version.json"), $envelope, [Text.UTF8Encoding]::new($false))
    Write-Output "本地打包完成：$output"
    Write-Output '尚未上传、推送或创建发行版。'
} finally { $rsa.Dispose() }
