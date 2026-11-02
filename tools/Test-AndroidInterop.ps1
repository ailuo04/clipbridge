#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ProjectDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'android'),
    [string]$GradlePath,
    [string]$TemporaryRoot=$env:TEMP
)
$ErrorActionPreference='Stop'
if(-not $GradlePath) { $GradlePath=Join-Path $ProjectDirectory 'gradlew.bat' }
$sourceRoot=Split-Path -Parent $ProjectDirectory
& dotnet build (Join-Path $sourceRoot 'tools/TransferTests/TransferTests.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0) { throw 'Transfer test build failed.' }
$testRoot=Join-Path $TemporaryRoot ('clipbridge-interop-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$legacyDir=Join-Path $testRoot 'legacy'; $newDir=Join-Path $testRoot 'experience'
$testDll=Join-Path $sourceRoot 'tools/TransferTests/bin/Release/net10.0/TransferTests.dll'
$dotnetPath=(Get-Command dotnet).Source
$previousLegacy=$env:CLIPBRIDGE_INTEROP_DIR; $previousExperience=$env:CLIPBRIDGE_EXPERIENCE_INTEROP_DIR
$servers=@()
try {
    # The harness stays hidden and only uses loopback sockets and synthetic data.
    $servers+=Start-Process -FilePath $dotnetPath -ArgumentList @('"'+$testDll+'"','--interop','"'+$legacyDir+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'legacy.log') -RedirectStandardError (Join-Path $testRoot 'legacy.err')
    $servers+=Start-Process -FilePath $dotnetPath -ArgumentList @('"'+$testDll+'"','--experience-interop','"'+$newDir+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'experience.log') -RedirectStandardError (Join-Path $testRoot 'experience.err')
    $readyLimit=[DateTime]::UtcNow.AddSeconds(30)
    while((-not(Test-Path -LiteralPath (Join-Path $legacyDir 'server.json')) -or -not(Test-Path -LiteralPath (Join-Path $newDir 'experience.json'))) -and [DateTime]::UtcNow -lt $readyLimit) { Start-Sleep -Milliseconds 100 }
    if(-not(Test-Path -LiteralPath (Join-Path $legacyDir 'server.json')) -or -not(Test-Path -LiteralPath (Join-Path $newDir 'experience.json'))) { throw 'Interop servers did not start.' }
    $env:CLIPBRIDGE_INTEROP_DIR=$legacyDir; $env:CLIPBRIDGE_EXPERIENCE_INTEROP_DIR=$newDir
    Push-Location $ProjectDirectory
    try { & $GradlePath testDebugUnitTest --no-daemon --max-workers 2 --console plain; $gradleResult=$LASTEXITCODE } finally { Pop-Location }
    if($gradleResult -ne 0) { throw 'JVM tests failed.' }
    foreach($server in $servers) { if(-not $server.WaitForExit(10000) -or $server.ExitCode -ne 0) { throw 'C# interoperability server failed.' } }
    Get-Content -LiteralPath (Join-Path $testRoot 'legacy.log'),(Join-Path $testRoot 'experience.log')
    Write-Output 'PASS real C#/JVM pairing, legacy files, and per-device text/files in both directions.'
} finally {
    foreach($server in $servers) { if(-not $server.HasExited) { Stop-Process -Id $server.Id -Force } }
    Get-Content -LiteralPath (Join-Path $testRoot 'legacy.err'),(Join-Path $testRoot 'experience.err') -ErrorAction SilentlyContinue
    $env:CLIPBRIDGE_INTEROP_DIR=$previousLegacy; $env:CLIPBRIDGE_EXPERIENCE_INTEROP_DIR=$previousExperience
}
