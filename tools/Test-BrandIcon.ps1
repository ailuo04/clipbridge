[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$icoPath = Join-Path $Root 'ClipBridge.ico'
$foregroundPath = Join-Path $Root 'android/app/src/main/res/drawable-nodpi/clipbridge_brand.png'
$manifestPath = Join-Path $Root 'android/app/src/main/AndroidManifest.xml'
$adaptivePath = Join-Path $Root 'android/app/src/main/res/mipmap-anydpi-v26/ic_clipbridge.xml'
$backgroundPath = Join-Path $Root 'android/app/src/main/res/drawable/clipbridge_icon_background.xml'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL $Message" }
    Write-Host "PASS $Message"
}

foreach ($path in @($icoPath, $foregroundPath, $manifestPath, $adaptivePath, $backgroundPath)) {
    Assert-True (Test-Path -LiteralPath $path) "Required icon file exists: $([IO.Path]::GetFileName($path))"
}

$legacy = Join-Path $Root 'android/app/src/main/res/drawable/ic_clipbridge.xml'
Assert-True (-not (Test-Path -LiteralPath $legacy)) 'Legacy Android icon resource is removed'

$manifestText = Get-Content -Raw -LiteralPath $manifestPath
Assert-True ($manifestText -match 'android:icon="@mipmap/ic_clipbridge"') 'Android manifest uses shared adaptive icon'
Assert-True ($manifestText -match 'android:roundIcon="@mipmap/ic_clipbridge"') 'Android manifest declares round adaptive icon'

$backgroundText = Get-Content -Raw -LiteralPath $backgroundPath
Assert-True ($backgroundText -match 'android:type="linear"' -and $backgroundText -match '#FAFCFF' -and $backgroundText -match '#DAE7FC') 'Android tile uses the matching clear glass gradient'

$adaptiveText = Get-Content -Raw -LiteralPath $adaptivePath
Assert-True ($adaptiveText -match '@drawable/clipbridge_brand') 'Adaptive icon references shared foreground'
Assert-True ($adaptiveText -match '@drawable/clipbridge_icon_background') 'Adaptive icon references glass background'

$iconBytes = [IO.File]::ReadAllBytes($icoPath)
Assert-True ($iconBytes.Length -ge 6) 'Windows ICO has a complete header'
$reserved = [BitConverter]::ToUInt16($iconBytes, 0)
$type = [BitConverter]::ToUInt16($iconBytes, 2)
$count = [BitConverter]::ToUInt16($iconBytes, 4)
Assert-True ($reserved -eq 0 -and $type -eq 1) 'Windows asset is a valid icon container'
Assert-True ($count -eq 7) 'Windows ICO includes all seven planned sizes'
$expectedSizes = @(16, 24, 32, 48, 64, 128, 256)
for ($index = 0; $index -lt $count; $index++) {
    $entry = 6 + (16 * $index)
    $width = [int]$iconBytes[$entry]
    $height = [int]$iconBytes[$entry + 1]
    if ($width -eq 0) { $width = 256 }
    if ($height -eq 0) { $height = 256 }
    $length = [BitConverter]::ToUInt32($iconBytes, $entry + 8)
    $offset = [BitConverter]::ToUInt32($iconBytes, $entry + 12)
    Assert-True ($width -eq $expectedSizes[$index] -and $height -eq $expectedSizes[$index]) "Windows ICO directory includes ${width}x${height}"
    Assert-True ($offset -ge (6 + 16 * $count) -and $offset + $length -le $iconBytes.Length) "Windows ${width}px frame stays within the ICO file"
    $frameBytes = [byte[]]::new($length)
    [Array]::Copy($iconBytes, [int]$offset, $frameBytes, 0, [int]$length)
    $frameStream = [IO.MemoryStream]::new($frameBytes)
    try {
        $frame = [Drawing.Image]::FromStream($frameStream, $true, $true)
        try { Assert-True ($frame.Width -eq $width -and $frame.Height -eq $height) "Windows ${width}px frame decodes correctly" }
        finally { $frame.Dispose() }
    }
    finally { $frameStream.Dispose() }
}

$icon = [Drawing.Icon]::new($icoPath)
try { Assert-True ($icon.Width -ge 16 -and $icon.Height -ge 16) 'Windows ICO loads as a system icon' }
finally { $icon.Dispose() }

$bitmap = [Drawing.Bitmap]::new($foregroundPath)
try {
    Assert-True ($bitmap.Width -eq 512 -and $bitmap.Height -eq 512) 'Android foreground is 512x512'
    Assert-True ($bitmap.PixelFormat.ToString() -match 'Argb|PArgb') 'Android foreground preserves alpha'
    Assert-True ($bitmap.GetPixel(0, 0).A -eq 0) 'Android foreground keeps transparent safe area'
    $minX = $bitmap.Width; $minY = $bitmap.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if ($bitmap.GetPixel($x, $y).A -le 8) { continue }
            $minX = [Math]::Min($minX, $x); $minY = [Math]::Min($minY, $y)
            $maxX = [Math]::Max($maxX, $x); $maxY = [Math]::Max($maxY, $y)
        }
    }
    Assert-True ($minX -ge 87 -and $minY -ge 87 -and $maxX -le 424 -and $maxY -le 424) 'Android mark fits inside the adaptive-icon safe zone'
}
finally { $bitmap.Dispose() }

Write-Host 'BRAND ICON TESTS PASSED'
