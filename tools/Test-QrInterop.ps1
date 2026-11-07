#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GradleUserHome = $(if ($env:GRADLE_USER_HOME) { $env:GRADLE_USER_HOME } else { Join-Path $env:USERPROFILE '.gradle' }),
    [string]$TemporaryRoot = $env:TEMP
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $GradleUserHome 'caches/modules-2/files-2.1/com.google.zxing/core/3.5.3'
$jar = Get-ChildItem -LiteralPath $cache -Filter 'core-3.5.3.jar' -File -Recurse -ErrorAction Stop | Select-Object -First 1 -ExpandProperty FullName
if (-not $jar) { throw 'ZXing core 3.5.3 must first be resolved by the Android build.' }
$javac = (Get-Command javac -ErrorAction Stop).Source
$java = (Get-Command java -ErrorAction Stop).Source
$work = Join-Path $TemporaryRoot ('clipbridge-qr-interop-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$fixture = Join-Path $work 'invitation.png'
$project = Join-Path $sourceRoot 'tools/TransferTests/TransferTests.csproj'
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'QR fixture generator build failed.' }
& dotnet run --project $project -c Release --no-build -- --write-qr $fixture
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw 'QR fixture generation failed.' }
& $javac -encoding UTF-8 -classpath $jar -d $work (Join-Path $PSScriptRoot 'QrInterop/DecodeQr.java')
if ($LASTEXITCODE -ne 0) { throw 'QR decoder compilation failed.' }
& $java '-Djava.awt.headless=true' -classpath "$work;$jar" DecodeQr $fixture
if ($LASTEXITCODE -ne 0) { throw 'Windows-to-Android QR decoding failed.' }