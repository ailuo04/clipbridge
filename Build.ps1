param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $env:LOCALAPPDATA 'ClipBridgeBuildTools\dotnet-10\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { $dotnet = $command.Source }
}
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw '请先安装 .NET SDK 10.0.403，或使用 GitHub Actions Windows runner 构建。' }
Push-Location -LiteralPath $PSScriptRoot
try {
    & $dotnet build (Join-Path $PSScriptRoot 'ClipBridge.csproj') -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败：$LASTEXITCODE" }
} finally { Pop-Location }
