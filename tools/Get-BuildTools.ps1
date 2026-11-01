#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $root -Force | Out-Null
function Get-VerifiedFile($Url, $Name, $Hash, $Algorithm, $Publisher) {
    $path = Join-Path $root $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Invoke-WebRequest -Uri $Url -OutFile $path }
    if ((Get-FileHash -LiteralPath $path -Algorithm $Algorithm).Hash -ne $Hash) { throw "校验和不匹配：$Name" }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike "*$Publisher*") { throw "签名不匹配：$Name" }
    return $path
}
$installer = Get-VerifiedFile 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' 'innosetup-6.7.3.exe' '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732' SHA256 'Pyrsys B.V.'
$inno = Join-Path $root 'InnoSetup'
& $installer /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER "/DIR=$inno" | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $inno 'ISCC.exe'))) { throw 'Inno Setup 安装失败。' }
Write-Output "InnoCompilerPath=$(Join-Path $inno 'ISCC.exe')"
