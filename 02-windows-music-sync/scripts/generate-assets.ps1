[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputDirectory)

# Packaging uses checked-in exports. SVG rendering is a separate design-time step.
$ErrorActionPreference = 'Stop'
$sourceDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Packaging\Assets')).TrimEnd('\', '/')
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
if ($sourceDirectory.Equals($OutputDirectory, [StringComparison]::OrdinalIgnoreCase) -or
    $OutputDirectory.StartsWith($sourceDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be separate from Packaging\Assets; source exports will not be overwritten.'
}
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) { throw 'Packaging\Assets is missing.' }

function Get-PngDimensions {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 33 -or [BitConverter]::ToString($bytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A' -or
        [Text.Encoding]::ASCII.GetString($bytes, 12, 4) -ne 'IHDR') {
        throw "Invalid PNG export: $Path"
    }
    $width = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 16))
    $height = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 20))
    if ($width -le 0 -or $height -le 0) { throw "Invalid PNG dimensions: $Path" }
    return [pscustomobject]@{ Width = $width; Height = $height }
}

$required = @{
    'StoreLogo.png' = @(50, 50)
    'Square44x44Logo.png' = @(44, 44)
    'Square150x150Logo.png' = @(150, 150)
    'Wide310x150Logo.png' = @(310, 150)
}
foreach ($name in $required.Keys) {
    $path = Join-Path $sourceDirectory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package image: $name" }
    $size = Get-PngDimensions -Path $path
    if ($size.Width -ne $required[$name][0] -or $size.Height -ne $required[$name][1]) {
        throw "Unexpected size for $name; expected $($required[$name][0])x$($required[$name][1])."
    }
}
$iconPath = Join-Path $sourceDirectory 'AppIcon.ico'
if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) { throw 'Missing AppIcon.ico.' }
$iconBytes = [IO.File]::ReadAllBytes($iconPath)
if ($iconBytes.Length -lt 22 -or [BitConverter]::ToUInt16($iconBytes, 0) -ne 0 -or
    [BitConverter]::ToUInt16($iconBytes, 2) -ne 1) { throw 'Invalid AppIcon.ico header.' }
$iconImageCount = [BitConverter]::ToUInt16($iconBytes, 4)
if ($iconImageCount -eq 0 -or 6 + $iconImageCount * 16 -gt $iconBytes.Length) { throw 'Invalid AppIcon.ico image directory.' }

$assetFiles = @(Get-ChildItem -LiteralPath $sourceDirectory -File | Where-Object { $_.Extension -in @('.png', '.ico') })
foreach ($file in $assetFiles) {
    if ($file.Extension -ne '.png') { continue }
    $size = Get-PngDimensions -Path $file.FullName
    $expectedWidth = $null
    $expectedHeight = $null
    if ($file.BaseName -match '^Square44x44Logo\.targetsize-(\d+)(?:_altform-(?:unplated|lightunplated))?$') {
        $expectedWidth = $expectedHeight = [int]$Matches[1]
    } elseif ($file.BaseName -match '^(StoreLogo|Square44x44Logo|Square150x150Logo|Wide310x150Logo)\.scale-(\d+)$') {
        $baseSize = $required[$Matches[1] + '.png']
        $scale = [int]$Matches[2] / 100.0
        $expectedWidth = [int][Math]::Ceiling($baseSize[0] * $scale)
        $expectedHeight = [int][Math]::Ceiling($baseSize[1] * $scale)
    }
    if ($null -ne $expectedWidth -and ($size.Width -ne $expectedWidth -or $size.Height -ne $expectedHeight)) {
        throw "Export dimensions do not match resource qualifiers: $($file.Name)."
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($file in $assetFiles) {
    $destination = Join-Path $OutputDirectory $file.Name
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) { throw "Asset copy mismatch: $($file.Name)" }
}
Write-Host "Validated and copied $($assetFiles.Count) checked-in Windows icon exports."
