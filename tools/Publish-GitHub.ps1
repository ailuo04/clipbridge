#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Notes,
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$AndroidDirectory,
    [string]$PrivateKeyPath,
    [string]$InnoCompilerPath,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $sourceRoot 'artifacts\release'
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $InnoCompilerPath = Join-Path $env:LOCALAPPDATA 'ClipBridgeBuildTools\InnoSetup\ISCC.exe'
}

$temporaryKeyPath = $null
function New-TemporarySigningKey {
    $secret = $env:CLIPBRIDGE_UPDATE_PRIVATE_KEY
    if ([string]::IsNullOrWhiteSpace($secret)) {
        throw '云端发布需要 CLIPBRIDGE_UPDATE_PRIVATE_KEY；密钥只能通过 GitHub Actions Secret 注入。'
    }
    $base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
    $path = Join-Path $base ('clipbridge-update-signing-' + [Guid]::NewGuid().ToString('N') + '.pem')
    [IO.File]::WriteAllText($path, $secret, [Text.UTF8Encoding]::new($false))
    $env:CLIPBRIDGE_UPDATE_PRIVATE_KEY = $null
    return $path
}

try {
    if (Test-Path -LiteralPath $OutputDirectory) {
        throw "输出目录已存在；请使用新的目录：$OutputDirectory"
    }

    if ($Publish) {
        if ([string]::IsNullOrWhiteSpace($env:CLIPBRIDGE_UPDATE_PRIVATE_KEY)) {
            throw '正式发布需要 GitHub Actions Secret CLIPBRIDGE_UPDATE_PRIVATE_KEY。'
        }
        if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
            throw '正式发布需要 GITHUB_TOKEN；GitHub Actions 会自动提供该令牌。'
        }
        $temporaryKeyPath = New-TemporarySigningKey
        $PrivateKeyPath = $temporaryKeyPath
    } elseif ([string]::IsNullOrWhiteSpace($PrivateKeyPath)) {
        $defaultKey = Join-Path $env:USERPROFILE '.clipbridge-release-keys\clipbridge-update-private-0.3.9.pem'
        if (Test-Path -LiteralPath $defaultKey -PathType Leaf) {
            $PrivateKeyPath = $defaultKey
        } elseif (-not [string]::IsNullOrWhiteSpace($env:CLIPBRIDGE_UPDATE_PRIVATE_KEY)) {
            $temporaryKeyPath = New-TemporarySigningKey
            $PrivateKeyPath = $temporaryKeyPath
        } else {
            throw '缺少签名私钥。请指定 -PrivateKeyPath，或设置 CLIPBRIDGE_UPDATE_PRIVATE_KEY。'
        }
    }

    $prepareArgs = @{
        AndroidDirectory = $AndroidDirectory
        Version = $Version
        Notes = $Notes
        OutputDirectory = $OutputDirectory
        PrivateKeyPath = $PrivateKeyPath
        InnoCompilerPath = $InnoCompilerPath
    }
    & (Join-Path $PSScriptRoot 'Prepare-Release.ps1') @prepareArgs
    if ($LASTEXITCODE -ne 0) { throw "Prepare-Release 失败：$LASTEXITCODE" }

    & (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Directory $OutputDirectory -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "Verify-Release 失败：$LASTEXITCODE" }

    if (-not $Publish) {
        Write-Output "本地构建和签名验证完成：$OutputDirectory"
        Write-Output '未上传 GitHub；正式发布必须由 GitHub Actions 显式执行。'
        return
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) { throw '正式发布需要 Python 3；GitHub Actions Windows runner 应已提供。' }
    $sourceCommit = $env:GITHUB_SHA
    if ([string]::IsNullOrWhiteSpace($sourceCommit)) {
        $sourceCommit = (& git -C $sourceRoot rev-parse HEAD 2>$null).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) { $sourceCommit = 'unknown' }
    }
    $publishArgs = @(
        (Join-Path $PSScriptRoot 'publish_github.py')
        '--directory', $OutputDirectory
        '--version', $Version
        '--source-commit', $sourceCommit
        '--publish'
    )
    & $python.Source @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "GitHub 发布失败：$LASTEXITCODE" }
} finally {
    if ($temporaryKeyPath -and (Test-Path -LiteralPath $temporaryKeyPath -PathType Leaf)) {
        Remove-Item -LiteralPath $temporaryKeyPath -Force -ErrorAction SilentlyContinue
    }
    $env:CLIPBRIDGE_UPDATE_PRIVATE_KEY = $null
}
