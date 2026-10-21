[CmdletBinding()]
param(
    [ValidateSet('Development', 'Store')]
    [string]$PackageKind = 'Development',
    [string]$IdentityName = 'LocalDev.Esp32MusicSync',
    [string]$Publisher = 'CN=ESP32 Music Sync Development',
    [string]$DisplayName = '音樂動態燈光設置',
    [string]$PublisherDisplayName = 'ESP32 Music Sync Development',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '0.2.2.0',
    [string]$OutputRoot,
    [string]$WindowsSdkBin
)

$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputRoot) { $OutputRoot = Join-Path $appRoot 'artifacts' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ($PackageKind -eq 'Store') {
    foreach ($required in @('IdentityName', 'Publisher', 'DisplayName', 'PublisherDisplayName')) {
        if (-not $PSBoundParameters.ContainsKey($required)) {
            throw "Store mode requires -$required from the actual Partner Center product."
        }
    }
    if ($IdentityName -eq 'LocalDev.Esp32MusicSync' -or $Publisher -eq 'CN=ESP32 Music Sync Development') {
        throw 'Development identity cannot be used for a Store candidate.'
    }
}
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Invalid package Identity Name.' }
if (-not $Publisher.StartsWith('CN=', [StringComparison]::OrdinalIgnoreCase)) { throw 'Publisher must be the complete certificate subject from Partner Center (CN=...).' }
$packageVersion = [version]$Version
if ($packageVersion.Major -gt 65535 -or $packageVersion.Minor -gt 65535 -or $packageVersion.Build -gt 65535 -or $packageVersion.Revision -gt 65535) {
    throw 'Each package version field must be between 0 and 65535.'
}
if ($PackageKind -eq 'Store' -and $packageVersion.Revision -ne 0) { throw 'Store candidates reserve the fourth version field for Store use; set it to 0.' }

if (-not $WindowsSdkBin) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdkVersion = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $sdkVersion) { throw 'Windows SDK x64 MakeAppx.exe is required.' }
    $WindowsSdkBin = Join-Path $sdkVersion.FullName 'x64'
}
$makeAppx = Join-Path $WindowsSdkBin 'makeappx.exe'
if (-not (Test-Path -LiteralPath $makeAppx)) { throw "Missing MakeAppx: $makeAppx" }
$makePri = Join-Path $WindowsSdkBin 'makepri.exe'
if (-not (Test-Path -LiteralPath $makePri)) { throw "Missing MakePri: $makePri" }

$runId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$runRoot = Join-Path $OutputRoot "package-$($PackageKind.ToLowerInvariant())-$runId"
$layout = Join-Path $runRoot 'layout'
$assets = Join-Path $layout 'Assets'
$publicFolder = Join-Path $layout 'Public'
New-Item -ItemType Directory -Path $assets, $publicFolder -Force | Out-Null

$project = Join-Path $appRoot 'MusicSync.csproj'
& dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $layout `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false `
    -p:Version=$Version -p:AssemblyVersion=$Version -p:FileVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath (Join-Path $layout 'Esp32MusicSync.exe'))) { throw 'Publish did not produce Esp32MusicSync.exe.' }

& (Join-Path $PSScriptRoot 'generate-assets.ps1') -OutputDirectory $assets
$template = [IO.File]::ReadAllText((Join-Path $appRoot 'Packaging\AppxManifest.template.xml'))
$values = @{
    '__IDENTITY_NAME__' = $IdentityName
    '__PUBLISHER__' = $Publisher
    '__VERSION__' = $Version
    '__DISPLAY_NAME__' = $DisplayName
    '__PUBLISHER_DISPLAY_NAME__' = $PublisherDisplayName
}
foreach ($key in $values.Keys) { $template = $template.Replace($key, [Security.SecurityElement]::Escape($values[$key])) }
if ($template -match '__[A-Z_]+__') { throw 'An unresolved manifest placeholder remains.' }
$manifestPath = Join-Path $layout 'AppxManifest.xml'
[IO.File]::WriteAllText($manifestPath, $template, [Text.UTF8Encoding]::new($false))
[xml]$manifestCheck = [IO.File]::ReadAllText($manifestPath)
[IO.File]::WriteAllText((Join-Path $publicFolder 'README.txt'), 'Reserved folder for the Windows Dynamic Lighting app extension. No audio is stored here.', [Text.UTF8Encoding]::new($false))

# Qualified images need a fresh index so Windows can select scale/targetsize variants.
# Keep the configuration and tool logs outside the package payload.
$priConfig = Join-Path $runRoot 'priconfig.xml'
$makePriLog = Join-Path $runRoot 'makepri.log'
$resourceIndex = Join-Path $layout 'resources.pri'
function Convert-MakePriLogToUtf8 {
    # MakePri writes UTF-16 to redirected output. PowerShell 7 can preserve those
    # bytes without a BOM, so normalize after both native calls, never mid-append.
    $bytes = [IO.File]::ReadAllBytes($makePriLog)
    if ($bytes.Length -ge 2 -and (($bytes[0] -eq 255 -and $bytes[1] -eq 254) -or $bytes[1] -eq 0)) {
        $text = [Text.Encoding]::Unicode.GetString($bytes)
    } else {
        $text = [Text.Encoding]::UTF8.GetString($bytes)
    }
    $text = $text.Replace([string][char]0xFEFF, '').Replace([string][char]0, '')
    [IO.File]::WriteAllText($makePriLog, $text, [Text.UTF8Encoding]::new($false))
}
& $makePri createconfig /cf $priConfig /dq zh-TW /o *> $makePriLog
if ($LASTEXITCODE -ne 0) {
    $toolExitCode = $LASTEXITCODE
    Convert-MakePriLogToUtf8
    Get-Content -LiteralPath $makePriLog -Tail 30 | Write-Host
    throw "MakePri createconfig failed with exit code $toolExitCode. Full log: $makePriLog"
}

# createconfig enables automatic resource-package splitting by default. This
# script builds one standalone MSIX, not a bundle with language/scale packages.
# Remove the entire packaging section so all qualified images stay in one PRI.
$priConfigDocument = [xml][IO.File]::ReadAllText($priConfig)
foreach ($packagingNode in @($priConfigDocument.SelectNodes('/*[local-name()="resources"]/*[local-name()="packaging"]'))) {
    [void]$packagingNode.ParentNode.RemoveChild($packagingNode)
}
$priWriterSettings = [Xml.XmlWriterSettings]::new()
$priWriterSettings.Encoding = [Text.UTF8Encoding]::new($false)
$priWriterSettings.Indent = $true
$priWriter = [Xml.XmlWriter]::Create($priConfig, $priWriterSettings)
try { $priConfigDocument.Save($priWriter) } finally { $priWriter.Dispose() }

& $makePri new /pr $layout /cf $priConfig /mn $manifestPath /of $resourceIndex /o *>> $makePriLog
if ($LASTEXITCODE -ne 0) {
    $toolExitCode = $LASTEXITCODE
    Convert-MakePriLogToUtf8
    Get-Content -LiteralPath $makePriLog -Tail 30 | Write-Host
    throw "MakePri new failed with exit code $toolExitCode. Full log: $makePriLog"
}
Convert-MakePriLogToUtf8
if (-not (Test-Path -LiteralPath $resourceIndex -PathType Leaf)) { throw 'MakePri did not produce resources.pri.' }
$resourceIndexFiles = @(Get-ChildItem -LiteralPath $layout -Filter '*.pri' -File -Recurse)
if ($resourceIndexFiles.Count -ne 1 -or
    -not $resourceIndexFiles[0].FullName.Equals($resourceIndex, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Expected one root resources.pri containing every scale/targetsize variant; resource-package splitting is not supported by this standalone MSIX workflow.'
}

$packagePath = Join-Path $runRoot "Esp32MusicSync_$($Version)_x64_$($PackageKind.ToLowerInvariant()).msix"
$makeAppxLog = Join-Path $runRoot 'makeappx.log'
& $makeAppx pack /d $layout /p $packagePath /o *> $makeAppxLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $makeAppxLog -Tail 30 | Write-Host
    throw "MakeAppx failed with exit code $LASTEXITCODE. Full log: $makeAppxLog"
}
$packageFile = Get-Item -LiteralPath $packagePath
$layoutFiles = @(Get-ChildItem -LiteralPath $layout -Recurse -File)
$report = [ordered]@{
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    PackageKind = $PackageKind
    IdentityName = $IdentityName
    Publisher = $Publisher
    DisplayName = $DisplayName
    Version = $Version
    Architecture = 'x64'
    SelfContained = $true
    LanguageVersion = '10.0'
    MinimumWindowsVersion = '10.0.22631.0'
    PackagePath = $packagePath
    PackageBytes = $packageFile.Length
    PackageSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    LayoutBytes = ($layoutFiles | Measure-Object Length -Sum).Sum
    LayoutFileCount = $layoutFiles.Count
    MakeAppxValidation = 'Passed during pack (no /nv)'
    PackageResourceIndex = 'Generated by MakePri from manifest and qualified visual assets'
    ResourcePackageSplitting = 'Disabled for standalone MSIX; all qualifiers in one PRI'
    ResourceIndexFileCount = $resourceIndexFiles.Count
    ResourceIndexSha256 = (Get-FileHash -LiteralPath $resourceIndex -Algorithm SHA256).Hash
    Signed = $false
    Installed = $false
    WindowsAppCertificationKit = 'NotRun'
    StoreSubmitted = $false
    StoreCertified = $false
    StorePublished = $false
    DotnetSdk = (& dotnet --version)
    MakeAppxPath = $makeAppx
    MakeAppxLog = $makeAppxLog
    MakePriPath = $makePri
    MakePriLog = $makePriLog
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'package-report.json') -Encoding utf8
Write-Host "Unsigned $PackageKind package: $packagePath"
Write-Host 'MakeAppx validation passed. Installed-package tests, WACK, and Store certification remain separate.'
$report | ConvertTo-Json -Depth 4
