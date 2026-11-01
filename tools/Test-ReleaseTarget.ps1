param(
    [string]$ReleaseDirectory
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $projectDirectory 'release-target.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) { $ReleaseDirectory = $config.releaseDirectory }
if (-not (Test-Path -LiteralPath $ReleaseDirectory -PathType Container)) {
    throw "发布仓库目录不存在：$ReleaseDirectory"
}

$remote = git -C $ReleaseDirectory remote get-url origin
if ($LASTEXITCODE -ne 0) { throw '无法读取发布仓库 origin。' }
if ($remote.TrimEnd('/') -ne $config.releaseRepositoryUrl.TrimEnd('/')) {
    throw "发布仓库地址与配置不一致：$remote"
}
$branch = git -C $ReleaseDirectory branch --show-current
if ($LASTEXITCODE -ne 0 -or $branch -ne $config.branch) {
    throw "发布仓库当前分支不是 $($config.branch)：$branch"
}
$head = git -C $ReleaseDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw '无法读取本地发布仓库提交。' }
$remoteResult = git -C $ReleaseDirectory ls-remote --heads origin "refs/heads/$($config.branch)"
if ($LASTEXITCODE -ne 0 -or -not $remoteResult) { throw '无法读取远程发布分支。' }
$remoteHead = ($remoteResult -split '\s+')[0]
if ($head -ne $remoteHead) {
    throw "本地发布分支与远程不一致：本地 $head；远程 $remoteHead。请先检查，脚本不会自动拉取或覆盖文件。"
}

$manifestPath = Join-Path $ReleaseDirectory $config.manifestFile
$envelope = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$payloadJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.payload))
$payload = $payloadJson | ConvertFrom-Json
if ($payload.schema -ne 1 -or -not $payload.windows) { throw '当前更新清单没有有效的 Windows 信息。' }
$prefix = $config.manifestUrl.Substring(0, $config.manifestUrl.LastIndexOf('/') + 1)
function Test-PublishedFile([string]$Url, [long]$ExpectedSize, [string]$ExpectedHash) {
    $name = [IO.Path]::GetFileName(([Uri]$Url).LocalPath)
    $downloaded = $false
    if ($Url.StartsWith($prefix, [StringComparison]::Ordinal)) {
        $path = Join-Path $ReleaseDirectory $name
    } else {
        $base = $config.releaseRepositoryUrl -replace '\.git$', ''
        $expectedUrl = "$base/releases/download/v$($payload.windows.version)/ClipBridge-$($payload.windows.version)-win-x64-Setup.exe"
        if ($Url -cne $expectedUrl) { throw "安装器地址不属于该版本发行版：$Url" }
        $path = Join-Path ([IO.Path]::GetTempPath()) ('clipbridge-release-check-' + [Guid]::NewGuid().ToString('N') + '.exe')
        $downloaded = $true
        Invoke-WebRequest -Uri $Url -OutFile $path
    }
    try {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "发布文件不存在：$path" }
        if ((Get-Item -LiteralPath $path).Length -ne $ExpectedSize) { throw "发布文件大小不匹配：$name" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if (-not [string]::Equals($hash, $ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) { throw "发布文件 SHA-256 不匹配：$name" }
        Write-Output "文件校验通过：$name"
    } finally {
        if ($downloaded -and (Test-Path -LiteralPath $path -PathType Leaf)) { Remove-Item -LiteralPath $path -Force }
    }
}

Test-PublishedFile $payload.windows.url $payload.windows.size $payload.windows.sha256
if ($payload.windows.installerUrl) {
    Test-PublishedFile $payload.windows.installerUrl $payload.windows.installerSize $payload.windows.installerSha256
}
$dirty = git -C $ReleaseDirectory status --porcelain
if ($LASTEXITCODE -ne 0) { throw '无法检查发布仓库状态。' }
Write-Output "发布地址：$remote"
Write-Output "分支：$branch；本地与远程提交一致：$head"
Write-Output "当前 Windows 版本：$($payload.windows.version)"
if ($dirty) { Write-Warning '发布仓库有未提交的修改或文件，请发布前检查。' } else { Write-Output '发布仓库工作区干净。' }
Write-Output '此检查不会验证清单签名、生成发布文件、上传或推送。'
