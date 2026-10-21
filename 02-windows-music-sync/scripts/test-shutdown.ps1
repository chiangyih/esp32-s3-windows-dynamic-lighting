[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$OutputDirectory,
    [ValidateRange(10, 60)]
    [int]$ExitTimeoutSeconds = 15
)

# Integration verification for this app only. These tests can start WASAPI and
# the existing LampArray controller. They do not play audio, change volume,
# change Windows lighting policy, or force-terminate any process.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$appRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ExecutablePath) {
    $ExecutablePath = Join-Path $appRoot 'bin\x64\Release\net10.0-windows10.0.26100.0\Esp32MusicSync.exe'
}
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetFileName($ExecutablePath) -ine 'Esp32MusicSync.exe') {
    throw 'ExecutablePath must identify this app, Esp32MusicSync.exe.'
}
if (@(Get-Process -Name Esp32MusicSync -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'ESP32 Music Sync is already running. Close it normally before running shutdown verification; this script does not close an existing instance.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $appRoot ('artifacts\ui-shutdown-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
        throw 'OutputDirectory must be empty so earlier verification evidence is preserved.'
    }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function ConvertTo-AppArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    # All variable arguments are Windows file paths ending in .json/.png.
    # Quotes cannot occur in a Windows filename; refusing them also keeps
    # command-line construction explicit rather than attempting shell escaping.
    if ($Value.Contains('"')) { throw 'An app argument contains an unsupported quote.' }
    if ($Value -match '\s' -or $Value.Length -eq 0) { return '"' + $Value + '"' }
    return $Value
}

function Assert-Report {
    param([Parameter(Mandatory = $true)]$Report, [Parameter(Mandatory = $true)][string]$ExpectedRoute)
    foreach ($name in @('FormDisposed', 'UiComponentsDisposed', 'OwnedFontsDisposed')) {
        if ($Report.$name -ne $true) { throw "Report does not confirm $name." }
    }
    if ($Report.Shutdown.IsDisposed -ne $true) { throw 'Engine was not disposed.' }
    foreach ($name in @('AudioWorkerRunning', 'RenderWorkerRunning', 'LightingDiscoveryRunning', 'CleanupWorkerRunning')) {
        if ($Report.Shutdown.$name -ne $false) { throw "A worker remains active: $name." }
    }
    if (@($Report.Shutdown.CleanupErrors).Count -ne 0) { throw 'Engine cleanup reported errors; inspect the case report.' }
    if (@($Report.UiCleanupErrors).Count -ne 0) { throw 'UI cleanup reported errors; inspect the case report.' }
    if ($null -ne $Report.StartError) { throw 'The app reported a startup failure.' }
    if ($Report.CloseRequestedBy -ne $ExpectedRoute) { throw 'The requested close route did not run.' }
}

$cases = @(
    [pscustomobject]@{ Name = 'button'; Arguments = @('--diagnose', '4', '--close-via-button'); Route = 'button'; Screenshot = $true; CloseWindow = $false },
    [pscustomobject]@{ Name = 'startup'; Arguments = @('--close-after-ms', '50', '--close-via-button'); Route = 'button'; Screenshot = $false; CloseWindow = $false },
    [pscustomobject]@{ Name = 'tray'; Arguments = @('--diagnose', '3', '--close-via-tray'); Route = 'tray'; Screenshot = $false; CloseWindow = $false },
    [pscustomobject]@{ Name = 'window'; Arguments = @('--autostart'); Route = 'window'; Screenshot = $false; CloseWindow = $true }
)
$results = [Collections.Generic.List[object]]::new()
$failure = $null
foreach ($case in $cases) {
    $caseClock = [Diagnostics.Stopwatch]::StartNew()
    $process = $null
    $reportPath = Join-Path $OutputDirectory ($case.Name + '.json')
    $caseArguments = @($case.Arguments) + @('--report', $reportPath)
    if ($case.Screenshot) {
        $caseArguments += @('--screenshot', (Join-Path $OutputDirectory 'new-ui.png'))
    }
    $commandLine = ($caseArguments | ForEach-Object { ConvertTo-AppArgument -Value $_ }) -join ' '
    $result = [ordered]@{
        Case = $case.Name
        ExpectedCloseRoute = $case.Route
        ProcessId = $null
        ExitCode = $null
        ProcessExitVerifiedExternally = $false
        CloseRequestedBy = $null
        ShutdownElapsedMilliseconds = $null
        ElapsedSeconds = $null
        ReportFile = [IO.Path]::GetFileName($reportPath)
        Status = 'NotStarted'
        Failure = $null
    }
    try {
        if (@(Get-Process -Name Esp32MusicSync -ErrorAction SilentlyContinue).Count -ne 0) {
            throw 'An app instance is running between cases; refusing to open another instance.'
        }
        # This is the interactive app under test, so its window remains visible.
        $process = Start-Process -FilePath $ExecutablePath -ArgumentList $commandLine -WorkingDirectory $appRoot -PassThru
        $result.ProcessId = $process.Id
        if ($case.CloseWindow) {
            if (-not $process.WaitForInputIdle(5000)) { throw 'The app did not become input-idle within five seconds.' }
            Start-Sleep -Milliseconds 1000
            $process.Refresh()
            if ($process.HasExited) { throw 'The app exited before WM_CLOSE was requested.' }
            if ($process.MainWindowTitle -ne 'ESP32 音樂同步') {
                throw 'The current main window is not the app form (a dropdown or dialog may be active); refusing to close a different window.'
            }
            if (-not $process.CloseMainWindow()) { throw 'WM_CLOSE could not be requested for the app window.' }
        }
        $remainingMilliseconds = [int][Math]::Max(1, ($ExitTimeoutSeconds * 1000) - $caseClock.ElapsedMilliseconds)
        if (-not $process.WaitForExit($remainingMilliseconds)) {
            throw "The test app did not exit within $ExitTimeoutSeconds seconds. Its PID is $($process.Id); close it normally before retrying. No process was force-terminated."
        }
        $result.ExitCode = $process.ExitCode
        if ($process.ExitCode -ne 0) { throw "The app exited with code $($process.ExitCode)." }
        if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) { throw 'The original PID still exists after exit.' }
        $result.ProcessExitVerifiedExternally = $true
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'The app did not write its exit report.' }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        Assert-Report -Report $report -ExpectedRoute $case.Route
        $result.CloseRequestedBy = $report.CloseRequestedBy
        $result.ShutdownElapsedMilliseconds = $report.ShutdownElapsedMilliseconds
        $result.Status = 'Passed'
    } catch {
        $result.Status = 'Failed'
        $result.Failure = $_.Exception.Message
        $failure = $_.Exception.Message
    } finally {
        $result.ElapsedSeconds = $caseClock.Elapsed.TotalSeconds
        $results.Add([pscustomobject]$result)
        if ($process) { $process.Dispose() }
    }
    if ($failure) { break }
}

$summary = [ordered]@{
    ObservedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    ExecutableFileVersion = (Get-Item -LiteralPath $ExecutablePath).VersionInfo.FileVersion
    ExitTimeoutSeconds = $ExitTimeoutSeconds
    Status = $(if ($failure) { 'Failed' } else { 'Passed' })
    ExpectedCases = $cases.Count
    CompletedCases = $results.Count
    Cases = @($results.ToArray())
    TestSignalPlayed = $false
    WindowsLightingPolicyChanged = $false
    ProcessForceTerminated = $false
    PhysicalLedVerification = 'Not performed; these are UI, lifecycle, and process-exit checks.'
}
$summaryPath = Join-Path $OutputDirectory 'verification-summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
$summary | ConvertTo-Json -Depth 8
Write-Host "Shutdown verification: $summaryPath"
if ($failure) { throw "Shutdown verification failed: $failure" }
