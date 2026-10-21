[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$IdentityName,
    [string]$OutputDirectory,
    [ValidateRange(10, 60)][int]$ExitTimeoutSeconds = 20
)

# Draft Store assets from this app's actual client UI. The app uses its existing
# diagnostic capture and normal close path; no desktop capture, image resizing,
# UI Automation, test-signal playback, policy change or forced termination.
# Diagnostic mode starts WASAPI/LampArray and can control a matching device.
# Only the resulting PNGs are listing assets; JSON reports are local evidence.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$appRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$actualPackageIdentity = $null
$launchMode = 'Executable'
if ($IdentityName) {
    if ($ExecutablePath) { throw 'Choose either IdentityName or ExecutablePath, not both.' }
    $package = @(Get-AppxPackage -Name $IdentityName)
    if ($package.Count -ne 1 -or $package[0].Name -ine $IdentityName) {
        throw "Expected exactly one installed package with the exact name $IdentityName."
    }
    $package = $package[0]
    $ExecutablePath = Join-Path $package.InstallLocation 'Esp32MusicSync.exe'
    $launchMode = 'PackageAumid'
    $actualPackageIdentity = [ordered]@{
        Name = $package.Name
        PackageFullName = $package.PackageFullName
        PackageFamilyName = $package.PackageFamilyName
        Publisher = $package.Publisher
        Version = [string]$package.Version
    }
} elseif (-not $ExecutablePath) {
    $ExecutablePath = Join-Path $appRoot 'bin\x64\Release\net10.0-windows10.0.26100.0\Esp32MusicSync.exe'
}
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($ExecutablePath) -ine 'Esp32MusicSync.exe') {
    throw 'ExecutablePath must identify Esp32MusicSync.exe.'
}
if (@(Get-Process -Name Esp32MusicSync -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'Close the existing app normally first. This script does not close or terminate it.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $appRoot ('artifacts\store-screenshots-draft-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and
    @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
    throw 'OutputDirectory must be empty to preserve previous captures.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing

function ConvertTo-AppArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value.Contains('"')) { throw 'An argument contains an unsupported quote.' }
    if ($Value -match '\s' -or $Value.Length -eq 0) { return '"' + $Value + '"' }
    return $Value
}

$views = @(
    [pscustomobject]@{ Name = '01-lighting-settings'; Arguments = @(); ExpectedPage = '燈光設置'; Caption = '音樂燈光設定：選擇音訊依據、配色、動態與速度，停止同步後可設定燈數。' },
    [pscustomobject]@{ Name = '02-about-and-hardware'; Arguments = @('--about'); ExpectedPage = '關於與版本'; Caption = '關於與版本：查看版本及相容硬體需求；需搭配支援 Windows 動態光效的本專案裝置。' },
    [pscustomobject]@{ Name = '03-privacy-policy'; Arguments = @('--privacy'); ExpectedPage = '隱私政策'; Caption = '內建隱私政策：查看音訊處理、本機設定及診斷資料說明，不需開啟外部網頁。' }
)
$results = [Collections.Generic.List[object]]::new()
$failure = $null
foreach ($view in $views) {
    $process = $null
    $capturedProcessId = $null
    $exitCode = $null
    $exitCodeReadError = $null
    $pngPath = Join-Path $OutputDirectory ($view.Name + '.png')
    $reportPath = Join-Path $OutputDirectory ($view.Name + '.json')
    try {
        if (@(Get-Process -Name Esp32MusicSync -ErrorAction SilentlyContinue).Count -ne 0) {
            throw 'Another app instance is running; refusing to open a second instance.'
        }
        $arguments = @('--diagnose', '3', '--store-screenshot', '--lamp-count', '8', '--close-via-button',
            '--screenshot', $pngPath, '--report', $reportPath) + @($view.Arguments)
        $commandLine = ($arguments | ForEach-Object { ConvertTo-AppArgument -Value $_ }) -join ' '
        # This is the interactive app being captured, so its window is visible.
        if ($IdentityName) {
            $activation = & (Join-Path $PSScriptRoot 'launch-packaged.ps1') -IdentityName $IdentityName -Arguments $commandLine -PassThru
            $capturedProcessId = [int]$activation.ProcessId
            $process = Get-Process -Id $capturedProcessId -ErrorAction SilentlyContinue
        } else {
            $process = Start-Process -FilePath $ExecutablePath -ArgumentList $commandLine -WorkingDirectory $appRoot -PassThru
            $capturedProcessId = $process.Id
        }
        if ($process -and -not $process.WaitForExit($ExitTimeoutSeconds * 1000)) {
            throw "App PID $capturedProcessId did not exit within $ExitTimeoutSeconds seconds. Close it normally; no process was terminated."
        }
        if ($process) {
            try { $exitCode = $process.ExitCode }
            catch {
                if (-not $IdentityName) { throw }
                $exitCodeReadError = 'Package activation process exit code was unavailable; PID disappearance and app cleanup are checked separately.'
            }
        }
        if ($null -ne $exitCode -and $exitCode -ne 0) { throw "App exited with code $exitCode." }
        if (-not $IdentityName -and $null -eq $exitCode) { throw 'Executable exit code was unavailable.' }
        if ($IdentityName -and $null -eq $exitCode -and -not $exitCodeReadError) {
            $exitCodeReadError = 'Package activation did not expose an exit code; PID disappearance and app cleanup are checked separately.'
        }
        if (Get-Process -Id $capturedProcessId -ErrorAction SilentlyContinue) { throw 'The captured app PID still exists.' }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($IdentityName -and $report.PackageIdentity -ne $true) {
            throw 'The app report does not confirm package identity after AUMID activation.'
        }
        if (-not $report.StoreScreenshotDraft -or $report.PreferenceSaveSucceeded -eq $true) {
            throw 'Capture was not identified as a draft or modified daily preferences.'
        }
        if (-not $report.FormDisposed -or -not $report.Shutdown.IsDisposed -or
            @($report.UiCleanupErrors).Count -ne 0 -or @($report.Shutdown.CleanupErrors).Count -ne 0) {
            throw 'The app did not report clean disposal.'
        }
        foreach ($worker in @('AudioWorkerRunning', 'RenderWorkerRunning', 'LightingDiscoveryRunning', 'CleanupWorkerRunning')) {
            if ($report.Shutdown.$worker -ne $false) { throw "A worker remains active after capture: $worker." }
        }
        if ($report.SelectedPage -ne $view.ExpectedPage) { throw 'The app did not select the requested capture page.' }
        if ($null -ne $report.StartError) { throw 'The app reported a startup error.' }
        $image = [Drawing.Image]::FromFile($pngPath)
        try {
            $width = $image.Width
            $height = $image.Height
        } finally { $image.Dispose() }
        if ($width -lt 1366 -or $height -lt 768) {
            throw "Screenshot is ${width}x${height}; Desktop Store screenshots require at least 1366x768."
        }
        $pngBytes = (Get-Item -LiteralPath $pngPath).Length
        if ($pngBytes -gt 50MB) { throw 'Screenshot exceeds the Store 50 MB file limit.' }
        $results.Add([pscustomobject]@{
            File = [IO.Path]::GetFileName($pngPath)
            Width = $width
            Height = $height
            Bytes = $pngBytes
            Sha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash
            Caption = $view.Caption
            ReportFile = [IO.Path]::GetFileName($reportPath)
            ProcessId = $capturedProcessId
            ExitCode = $exitCode
            ExitCodeVerified = $null -ne $exitCode
            ExitCodeReadError = $exitCodeReadError
            ProcessExitVerifiedExternally = $true
            PackageIdentityReported = $report.PackageIdentity
            DailyPreferencesSaved = $false
            PhysicalLedVerified = $false
        })
    } catch {
        $failure = $_.Exception.Message
    } finally {
        if ($process) { $process.Dispose() }
    }
    if ($failure) { break }
}
$index = [ordered]@{
    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Status = $(if ($failure) { 'FailedDraft' } else { 'DraftPendingVisualReview' })
    ExecutableVersion = (Get-Item -LiteralPath $ExecutablePath).VersionInfo.FileVersion
    LaunchMode = $launchMode
    ActualPackageIdentity = $actualPackageIdentity
    BackendVerified = $false
    StoreIdentityVerified = $false
    UploadedToStore = $false
    VisualReviewRequired = $true
    ExpectedScreenshots = $views.Count
    Screenshots = @($results.ToArray())
    Failure = $failure
    Note = 'Real app client UI captured at Store dimensions. Audio/device status reflects the run; no physical LED result is implied. Reports may contain local endpoint descriptions and are not Store assets.'
}
$index | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'index.json') -Encoding utf8
$index | ConvertTo-Json -Depth 7
if ($failure) { throw "Draft Store capture failed: $failure" }
