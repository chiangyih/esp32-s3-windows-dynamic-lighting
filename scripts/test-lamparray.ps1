#requires -Version 5.1
<#
Windows PowerShell 5.1 / Windows 11 LampArray test for this project's 1..64 LEDs.
Default: enumerate the exact target and inspect properties; no colour writes.
  powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\test-lamparray.ps1
Explicit colour sequence (a visible foreground window is required):
  powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\test-lamparray.ps1 -RunSequence
Read-only inspection of a firmware-configured 64-lamp device:
  powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64
Explicit 64-lamp end-index or held-black tests (only after physical setup checks):
  powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64 -RunSequence -TestPattern FirstLast
  powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64 -RunSequence -TestPattern Black -ColorHoldMilliseconds 10000

Only Windows.Devices.Lights is used. No COM ports, raw HID, registry writes,
global lighting settings, IsEnabled setters, or BrightnessLevel setters.
Keep the helper window in the foreground. Closing it aborts host writes, but
does not disconnect 5V. A final black frame is sent only while control is held.
The default RGB/sequence timing remains compatible for 8 lamps; above 8 lamps,
the default per-lamp hold is 100ms. Explicit holds are bounded and reported.
Host colour values are subject to the installed firmware's limits. This tool
does not measure current, USB voltage, physical lamp count or output brightness.
Original USB instance IDs, MAC values, and serials are never printed.

Official references:
https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/lighting-dynamic-lamparray
https://learn.microsoft.com/en-us/uwp/api/windows.devices.lights.lamparray
https://learn.microsoft.com/en-us/uwp/api/windows.devices.lights.lamparray.setcolorsforindices
https://learn.microsoft.com/en-us/uwp/api/windows.devices.lights.lamparray.isavailable
#>
[CmdletBinding()]
param(
    [switch]$RunSequence,
    [ValidateRange(1, 64)]
    [int]$ExpectedLampCount = 8,
    [ValidateSet('RgbAndSequential', 'FirstLast', 'Sequential', 'Black', 'White')]
    [string]$TestPattern = 'RgbAndSequential',
    [ValidateRange(100, 30000)]
    [int]$ColorHoldMilliseconds = 3000,
    [ValidateRange(100, 1000)]
    [int]$LampHoldMilliseconds = 700,
    [ValidateRange(1, 5)]
    [int]$AsyncTimeoutSeconds = 4,
    [ValidateRange(1, 12)]
    [int]$WaitForForegroundSeconds = 10
)

$ErrorActionPreference = 'Stop'
$taskClock = [System.Diagnostics.Stopwatch]::StartNew()
$taskExpectedName = 'ESP32-S3 USB Light Strip'
$taskExpectedVendor = 0x303A
$taskExpectedProduct = 0x1001
$taskExpectedCount = $ExpectedLampCount
$taskLampHoldMs = if ($PSBoundParameters.ContainsKey('LampHoldMilliseconds') -or $ExpectedLampCount -le 8) {
    $LampHoldMilliseconds
} else { 100 }
$taskPatternHoldMs = switch ($TestPattern) {
    'RgbAndSequential' { 3 * $ColorHoldMilliseconds + $taskExpectedCount * $taskLampHoldMs }
    'FirstLast' { [Math]::Min(2, $taskExpectedCount) * $ColorHoldMilliseconds }
    'Sequential' { $taskExpectedCount * $taskLampHoldMs }
    default { $ColorHoldMilliseconds }
}
# Setup/WinRT operations, foreground wait, pattern, initial black and cleanup
# each have budget. Even maximum explicit holds cannot create an unbounded run.
$taskMaximumMs = if ($RunSequence) {
    [int][Math]::Max(29000, 21400 + $WaitForForegroundSeconds * 1000 + $taskPatternHoldMs)
} else { 29000 }
$taskIndices = [int[]](0..($taskExpectedCount - 1))
$taskStage = 'Initialization'
$taskLamp = $null
$taskColorType = $null
$taskForm = $null
$taskLabel = $null
$taskSequenceStarted = $false
$taskSteps = New-Object 'System.Collections.Generic.List[object]'
$taskReport = [ordered]@{
    ObservedAtUtc = [DateTime]::UtcNow.ToString('o')
    Mode = $(if ($RunSequence) { 'RunSequence' } else { 'InspectOnly' })
    TestPattern = $(if ($RunSequence) { $TestPattern } else { 'NoneReadOnly' })
    ExpectedLampCount = $taskExpectedCount
    ActualLampCount = $null
    ColorHoldMilliseconds = $ColorHoldMilliseconds
    LampHoldMilliseconds = $taskLampHoldMs
    OverallDeadlineMilliseconds = $taskMaximumMs
    Status = 'NotStarted'
    Target = [pscustomobject]@{
        Name = $taskExpectedName
        VendorId = '0x303A'
        ProductId = '0x1001'
        LampCount = $taskExpectedCount
    }
    MatchCount = 0
    Properties = $null
    FinalProperties = $null
    ControlWait = [ordered]@{
        Requested = [bool]$RunSequence
        SampleCount = 0
        WindowVisibleAtLeastOnce = $false
        LastWindowVisible = $false
        ForegroundMatchedAtLeastOnce = $false
        LastForegroundMatched = $false
        IsAvailableAtLeastOnce = $false
        LastIsAvailable = $null
        LastIsConnected = $null
        LastIsEnabled = $null
        AllConditionsMatchedAtLeastOnce = $false
        LastBlockingCondition = $(if ($RunSequence) { 'NotSampled' } else { 'NotRequested' })
    }
    Steps = @()
    CleanupBlack = 'NotNeededNoColourWrites'
    CleanupError = $null
    Error = $null
    ElapsedMs = 0
    PhysicalLedResult = 'Pending human observation; API success does not verify light output.'
    PhysicalLampCountVerified = $false
    UsbCurrentMeasured = $false
    UsbVoltageMeasured = $false
    ElectricalVerification = 'Pending independent meter measurements; firmware limits and real current are not read by LampArray.'
}

function Get-TaskSafeError {
    param([System.Exception]$Exception)
    $taskBaseError = $Exception.GetBaseException()
    return [pscustomobject]@{
        Stage = $script:taskStage
        Type = $taskBaseError.GetType().FullName
        HResult = ('0x{0:X8}' -f $taskBaseError.HResult)
    }
}

function Get-TaskWaitMs {
    $taskRemainingMs = $script:taskMaximumMs - $script:taskClock.ElapsedMilliseconds
    if ($taskRemainingMs -le 0) {
        throw [System.TimeoutException]::new('Overall inspection/test deadline reached.')
    }
    return [int][Math]::Min($script:AsyncTimeoutSeconds * 1000, $taskRemainingMs)
}

function Wait-TaskWinRt {
    param([object]$Operation, [Type]$ResultType)
    $taskBridge = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object {
            $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and
            $_.GetGenericArguments().Count -eq 1 -and
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } | Select-Object -First 1
    if ($null -eq $taskBridge) {
        throw [System.NotSupportedException]::new('WinRT task bridge unavailable.')
    }
    $taskAsync = $taskBridge.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    if (-not $taskAsync.Wait((Get-TaskWaitMs))) {
        try { $Operation.Cancel() } catch { }
        throw [System.TimeoutException]::new('WinRT operation timed out.')
    }
    # Preserve DeviceInformationCollection as one object instead of unrolling it.
    return ,$taskAsync.Result
}

function Get-TaskLampProperties {
    param([object]$Lamp)
    # None of these property accesses is a setter. DeviceId is intentionally absent.
    return [pscustomobject]@{
        VendorId = ('0x{0:X4}' -f [int]$Lamp.HardwareVendorId)
        ProductId = ('0x{0:X4}' -f [int]$Lamp.HardwareProductId)
        LampCount = [int]$Lamp.LampCount
        Kind = [string]$Lamp.LampArrayKind
        IsConnected = [bool]$Lamp.IsConnected
        IsEnabled = [bool]$Lamp.IsEnabled
        IsAvailable = [bool]$Lamp.IsAvailable
        BrightnessLevel = [double]$Lamp.BrightnessLevel
        MinUpdateIntervalMs = [double]$Lamp.MinUpdateInterval.TotalMilliseconds
    }
}

function New-TaskColor {
    param([byte]$Red, [byte]$Green, [byte]$Blue)
    # On .NET Framework, Color is a projected CLR struct in the runtime bridge,
    # not an activatable WinRT class in the Windows.UI metadata namespace.
    $taskColor = [Activator]::CreateInstance($script:taskColorType)
    $taskColor.A = 255
    $taskColor.R = $Red
    $taskColor.G = $Green
    $taskColor.B = $Blue
    return $taskColor
}

function New-TaskFrame {
    param([byte]$Red, [byte]$Green, [byte]$Blue)
    $taskTypedColors = [Array]::CreateInstance($script:taskColorType, $script:taskExpectedCount)
    $taskFillColor = New-TaskColor $Red $Green $Blue
    for ($taskFillIndex = 0; $taskFillIndex -lt $script:taskExpectedCount; ++$taskFillIndex) {
        $taskTypedColors.SetValue($taskFillColor, $taskFillIndex)
    }
    # Avoid PowerShell's normal pipeline conversion into an Object[] array.
    return ,$taskTypedColors
}

function Test-TaskControlAvailable {
    $taskWindowVisible = ($null -ne $script:taskForm -and
        -not $script:taskForm.IsDisposed -and $script:taskForm.Visible)
    $taskForegroundMatched = $false
    if ($taskWindowVisible) {
        # Compare handles in memory only; never report HWNDs or other windows.
        $taskForegroundMatched = ([LampArrayTest.Foreground]::GetForegroundWindow() -eq
            $script:taskForm.Handle)
    }
    # Read these even when focus is missing, so diagnostics can distinguish
    # focus failure from an unavailable/disconnected/disabled LampArray.
    $taskConnected = ($null -ne $script:taskLamp -and [bool]$script:taskLamp.IsConnected)
    $taskEnabled = ($null -ne $script:taskLamp -and [bool]$script:taskLamp.IsEnabled)
    $taskAvailable = ($null -ne $script:taskLamp -and [bool]$script:taskLamp.IsAvailable)
    $taskCountMatched = ($null -ne $script:taskLamp -and
        [int]$script:taskLamp.LampCount -eq $script:taskExpectedCount)
    $taskAllConditions = ($taskWindowVisible -and $taskForegroundMatched -and
        $taskConnected -and $taskEnabled -and $taskAvailable -and $taskCountMatched)
    if ($script:taskStage -eq 'AwaitForegroundAvailability') {
        $taskWaitDiagnostics = $script:taskReport.ControlWait
        $taskWaitDiagnostics.SampleCount++
        $taskWaitDiagnostics.LastWindowVisible = [bool]$taskWindowVisible
        $taskWaitDiagnostics.WindowVisibleAtLeastOnce =
            ($taskWaitDiagnostics.WindowVisibleAtLeastOnce -or $taskWindowVisible)
        $taskWaitDiagnostics.LastForegroundMatched = [bool]$taskForegroundMatched
        $taskWaitDiagnostics.ForegroundMatchedAtLeastOnce =
            ($taskWaitDiagnostics.ForegroundMatchedAtLeastOnce -or $taskForegroundMatched)
        $taskWaitDiagnostics.LastIsAvailable = [bool]$taskAvailable
        $taskWaitDiagnostics.IsAvailableAtLeastOnce =
            ($taskWaitDiagnostics.IsAvailableAtLeastOnce -or $taskAvailable)
        $taskWaitDiagnostics.LastIsConnected = [bool]$taskConnected
        $taskWaitDiagnostics.LastIsEnabled = [bool]$taskEnabled
        $taskWaitDiagnostics.AllConditionsMatchedAtLeastOnce =
            ($taskWaitDiagnostics.AllConditionsMatchedAtLeastOnce -or $taskAllConditions)
        $taskWaitDiagnostics.LastBlockingCondition = if (-not $taskWindowVisible) {
            'HelperWindowNotVisible'
        } elseif (-not $taskForegroundMatched) {
            'HelperWindowNotForeground'
        } elseif (-not $taskConnected) {
            'LampArrayDisconnected'
        } elseif (-not $taskEnabled) {
            'LampArrayDisabled'
        } elseif (-not $taskCountMatched) {
            'LampCountMismatch'
        } elseif (-not $taskAvailable) {
            'WindowsControlUnavailable'
        } else {
            'NoneAllConditionsReady'
        }
    }
    return [bool]$taskAllConditions
}

function Assert-TaskControl {
    if ($script:taskClock.ElapsedMilliseconds -ge $script:taskMaximumMs) {
        throw [System.TimeoutException]::new('Overall test deadline reached.')
    }
    if (-not (Test-TaskControlAvailable)) {
        throw [System.InvalidOperationException]::new('Foreground lighting control unavailable.')
    }
}

function Wait-TaskHold {
    param([int]$Milliseconds)
    $taskUntilMs = $script:taskClock.ElapsedMilliseconds + $Milliseconds
    while ($script:taskClock.ElapsedMilliseconds -lt $taskUntilMs) {
        [System.Windows.Forms.Application]::DoEvents()
        Assert-TaskControl
        Start-Sleep -Milliseconds 25
    }
}

function Send-TaskFrame {
    param([string]$Label, [System.Array]$Colors, [int]$HoldMs)
    Assert-TaskControl
    if ($Colors.Length -ne $script:taskExpectedCount) {
        throw [System.ArgumentException]::new('Frame length must match the configured lamp count.')
    }
    $script:taskLabel.Text = $Label + "`r`nObserve the strip; keep this window active."
    [System.Windows.Forms.Application]::DoEvents()
    Assert-TaskControl
    $script:taskLamp.SetColorsForIndices($Colors, $script:taskIndices)
    $script:taskSequenceStarted = $true
    $script:taskSteps.Add([pscustomobject]@{
        Stage = $Label
        Api = ('SetColorsForIndices(Color[{0}], Int32[{0}])' -f $script:taskExpectedCount)
        LampCount = $script:taskExpectedCount
        AcceptedAtMs = $script:taskClock.ElapsedMilliseconds
        IsAvailableAtCall = $true
        HoldMs = $HoldMs
    })
    Wait-TaskHold $HoldMs
}

try {
    if ($RunSequence -and [System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
        throw [System.NotSupportedException]::new('RunSequence requires powershell.exe -STA.')
    }
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $taskLampType = [Windows.Devices.Lights.LampArray, Windows.Devices.Lights, ContentType = WindowsRuntime]
    $null = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
    $taskCollectionType = [Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
    $taskColorType = [System.WindowsRuntimeSystemExtensions].Assembly.GetType('Windows.UI.Color', $true)

    $taskStage = 'EnumerateExactTarget'
    $taskSelector = [Windows.Devices.Lights.LampArray]::GetDeviceSelector()
    $taskEnumeration = [Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($taskSelector)
    $taskDevices = Wait-TaskWinRt $taskEnumeration $taskCollectionType
    $taskTargets = @($taskDevices | Where-Object {
        [string]::Equals([string]$_.Name, $taskExpectedName, [System.StringComparison]::Ordinal) -and
        ([string]$_.Id -match '(?i)VID_303A(?:[&#\\]|$)') -and
        ([string]$_.Id -match '(?i)PID_1001(?:[&#\\]|$)')
    })
    $taskReport.MatchCount = $taskTargets.Count
    if ($taskTargets.Count -ne 1) {
        throw [System.InvalidOperationException]::new('Exactly one matching target is required.')
    }

    $taskStage = 'FromIdAsync'
    $taskLamp = Wait-TaskWinRt ([Windows.Devices.Lights.LampArray]::FromIdAsync($taskTargets[0].Id)) $taskLampType
    if ($null -eq $taskLamp) {
        throw [System.InvalidOperationException]::new('LampArray object unavailable.')
    }
    $taskStage = 'ValidateProperties'
    $taskReport.Properties = Get-TaskLampProperties $taskLamp
    $taskReport.ActualLampCount = $taskReport.Properties.LampCount
    if ([int]$taskLamp.HardwareVendorId -ne $taskExpectedVendor -or
        [int]$taskLamp.HardwareProductId -ne $taskExpectedProduct -or
        [int]$taskLamp.LampCount -ne $taskExpectedCount) {
        throw [System.InvalidOperationException]::new('Hardware identity or LampCount mismatch.')
    }

    if (-not $RunSequence) {
        $taskReport.Status = 'InspectedNoColourWrites'
    } else {
        $taskStage = 'CreateForegroundWindow'
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        if ($null -eq ('LampArrayTest.Foreground' -as [Type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace LampArrayTest {
    public static class Foreground {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }
}
'@
        }
        $taskForm = New-Object System.Windows.Forms.Form
        $taskForm.Text = 'ESP32-S3 - Windows LampArray test'
        $taskForm.ClientSize = New-Object System.Drawing.Size(480, 150)
        $taskForm.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
        $taskForm.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
        $taskForm.MaximizeBox = $false
        $taskLabel = New-Object System.Windows.Forms.Label
        $taskLabel.Dock = [System.Windows.Forms.DockStyle]::Fill
        $taskLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
        $taskLabel.Text = "Waiting for Windows foreground lighting control...`r`nClose this window to abort."
        $taskForm.Controls.Add($taskLabel)
        $taskForm.Show()
        $taskForm.Activate()
        $taskStage = 'AwaitForegroundAvailability'
        $taskAvailabilityUntil = [Math]::Min($taskMaximumMs,
            $taskClock.ElapsedMilliseconds + $WaitForForegroundSeconds * 1000)
        while ($taskClock.ElapsedMilliseconds -lt $taskAvailabilityUntil) {
            [System.Windows.Forms.Application]::DoEvents()
            if (Test-TaskControlAvailable) { break }
            Start-Sleep -Milliseconds 25
        }
        Assert-TaskControl
        if ([double]$taskLamp.BrightnessLevel -le 0.0) {
            throw [System.InvalidOperationException]::new('Brightness is zero; settings remain unchanged.')
        }
        $taskReport.Properties = Get-TaskLampProperties $taskLamp
        $taskBlack = New-TaskColor 0 0 0
        $taskStage = 'InitialBlack'
        Assert-TaskControl
        $taskLamp.SetColor($taskBlack)
        $taskSequenceStarted = $true
        $taskSteps.Add([pscustomobject]@{
            Stage = 'InitialBlack'; Api = 'SetColor(Black)'
            AcceptedAtMs = $taskClock.ElapsedMilliseconds; IsAvailableAtCall = $true; HoldMs = 400
        })
        Wait-TaskHold 400
        if ($TestPattern -eq 'RgbAndSequential') {
            $taskStage = 'Red64'
            Send-TaskFrame 'All red input 64/255; firmware-limited' (New-TaskFrame 64 0 0) $ColorHoldMilliseconds
            $taskStage = 'Green64'
            Send-TaskFrame 'All green input 64/255; firmware-limited' (New-TaskFrame 0 64 0) $ColorHoldMilliseconds
            $taskStage = 'Blue64'
            Send-TaskFrame 'All blue input 64/255; firmware-limited' (New-TaskFrame 0 0 64) $ColorHoldMilliseconds
        }
        if ($TestPattern -eq 'RgbAndSequential' -or $TestPattern -eq 'Sequential') {
            for ($taskIndex = 0; $taskIndex -lt $taskExpectedCount; ++$taskIndex) {
                $taskStage = 'SingleLamp' + $taskIndex
                $taskFrame = New-TaskFrame 0 0 0
                $taskFrame[$taskIndex] = New-TaskColor 48 48 48
                Send-TaskFrame ('Single lamp index ' + $taskIndex + ' - white input 48/255; firmware-limited') $taskFrame $taskLampHoldMs
            }
        } elseif ($TestPattern -eq 'FirstLast') {
            foreach ($taskIndex in @(@(0, ($taskExpectedCount - 1)) | Select-Object -Unique)) {
                $taskStage = 'EndIndex' + $taskIndex
                $taskFrame = New-TaskFrame 0 0 0
                $taskFrame[$taskIndex] = New-TaskColor 48 48 48
                Send-TaskFrame ('End index ' + $taskIndex + ' - white input 48/255; firmware-limited') $taskFrame $ColorHoldMilliseconds
            }
        } elseif ($TestPattern -eq 'Black') {
            $taskStage = 'HeldBlack'
            Send-TaskFrame 'All black; 5V remains connected, measure idle current separately' (New-TaskFrame 0 0 0) $ColorHoldMilliseconds
        } elseif ($TestPattern -eq 'White') {
            $taskStage = 'HeldWhite'
            Send-TaskFrame 'All white input 64/255; firmware-limited, not unrestricted white' (New-TaskFrame 64 64 64) $ColorHoldMilliseconds
        }
        $taskReport.Status = 'SequenceCallsAcceptedVisualVerificationPending'
    }
} catch {
    $taskReport.Status = 'Stopped'
    $taskReport.Error = Get-TaskSafeError $_.Exception
} finally {
    if ($RunSequence -and $taskSequenceStarted -and $null -ne $taskLamp) {
        $taskStage = 'CleanupBlack'
        try {
            # Respect lost focus/OS arbitration even during cleanup. Never use a
            # raw-HID fallback or modify global settings to force a final frame.
            if (Test-TaskControlAvailable) {
                $taskLamp.SetColor((New-TaskColor 0 0 0))
                $taskReport.CleanupBlack = 'AcceptedWhileForegroundAndAvailable'
                if ($null -ne $taskLabel -and -not $taskLabel.IsDisposed) {
                    $taskLabel.Text = 'Black frame sent. Test finished.'
                    [System.Windows.Forms.Application]::DoEvents()
                }
                # Give the final frame time to reach the device before release.
                $taskCleanupUntil = [Math]::Min($taskMaximumMs, $taskClock.ElapsedMilliseconds + 500)
                while ($taskClock.ElapsedMilliseconds -lt $taskCleanupUntil) {
                    [System.Windows.Forms.Application]::DoEvents()
                    Start-Sleep -Milliseconds 25
                }
            } else {
                $taskReport.CleanupBlack = 'NotSentControlUnavailable'
            }
        } catch {
            $taskReport.CleanupBlack = 'CallFailed'
            $taskReport.CleanupError = Get-TaskSafeError $_.Exception
        }
    }
    if ($null -ne $taskLamp) {
        try { $taskReport.FinalProperties = Get-TaskLampProperties $taskLamp } catch { }
    }
    if ($null -ne $taskForm) {
        if (-not $taskForm.IsDisposed) { $taskForm.Close(); $taskForm.Dispose() }
    }
    if ($taskLamp -is [System.IDisposable]) { $taskLamp.Dispose() }
    $taskReport.Steps = @($taskSteps.ToArray())
    $taskReport.ElapsedMs = $taskClock.ElapsedMilliseconds
    [pscustomobject]$taskReport | ConvertTo-Json -Depth 7
}
