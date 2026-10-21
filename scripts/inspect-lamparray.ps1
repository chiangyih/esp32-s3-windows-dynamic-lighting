#requires -Version 5.1
<#
Read-only Windows LampArray inspection. This script does not open a COM port,
call LampArray.FromIdAsync, acquire lighting control, or change device settings.
Run with Windows PowerShell 5.1 on Windows 11 after connecting the device:
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\inspect-lamparray.ps1
The output is JSON. Original interface/instance IDs and USB serials are omitted.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 30)]
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'

function Get-SanitizedDeviceId {
    param([string]$DeviceId)
    # Keep only non-unique USB interface markers, never the instance suffix.
    $markers = [regex]::Matches(
        $DeviceId,
        '(?i)VID_[0-9A-F]{4}|PID_[0-9A-F]{4}|MI_[0-9A-F]{2}|COL[0-9A-F]{2}'
    ) | ForEach-Object { $_.Value.ToUpperInvariant() } | Select-Object -Unique
    if (@($markers).Count -gt 0) {
        return ('HID\' + (@($markers) -join '&') + '\[instance-redacted]')
    }
    return 'HID\[instance-redacted; no USB markers supplied]'
}

function Get-SanitizedDeviceName {
    param([string]$DeviceName)
    if ([string]::IsNullOrWhiteSpace($DeviceName)) { return '(not supplied)' }
    # Redact MAC-shaped values and this prototype's MAC-derived USB serial form.
    $clean = $DeviceName -replace '(?i)(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}', '[redacted]'
    $clean = $clean -replace '(?i)\b(?:S3-)?[0-9A-F]{12}\b', '[redacted]'
    return $clean
}

function Get-SafeError {
    param([System.Exception]$Exception)
    $base = $Exception.GetBaseException()
    # Exception messages can contain complete interface paths. Omit them.
    return [pscustomobject]@{
        Type = $base.GetType().FullName
        HResult = ('0x{0:X8}' -f $base.HResult)
    }
}

$winrtResult = [ordered]@{
    Status = 'Unavailable'
    Count = 0
    Devices = @()
    Error = $null
}

try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Devices.Lights.LampArray, Windows.Devices.Lights, ContentType = WindowsRuntime]
    $null = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
    $collectionType = [Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType = WindowsRuntime]

    # Enumeration only: never construct LampArray objects or request control.
    $selector = [Windows.Devices.Lights.LampArray]::GetDeviceSelector()
    $operation = [Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($selector)
    $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object {
            $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and
            $_.GetGenericArguments().Count -eq 1 -and
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } | Select-Object -First 1
    if ($null -eq $asTask) {
        throw [System.NotSupportedException]::new('WinRT task bridge unavailable.')
    }
    $task = $asTask.MakeGenericMethod($collectionType).Invoke($null, @($operation))
    if (-not $task.Wait($TimeoutSeconds * 1000)) {
        # Cancel this inspection request, not a device or lighting operation.
        try { $operation.Cancel() } catch { }
        $winrtResult.Status = 'TimedOut'
    } else {
        $devices = New-Object 'System.Collections.Generic.List[object]'
        foreach ($device in $task.Result) {
            $devices.Add([pscustomobject]@{
                Name = Get-SanitizedDeviceName ([string]$device.Name)
                SanitizedId = Get-SanitizedDeviceId ([string]$device.Id)
                IsEnabled = [bool]$device.IsEnabled
            })
        }
        $winrtResult.Devices = @($devices.ToArray())
        $winrtResult.Count = $devices.Count
        $winrtResult.Status = if ($devices.Count -gt 0) { 'Detected' } else { 'NotDetectedThisQuery' }
    }
} catch {
    $winrtResult.Status = 'Unavailable'
    $winrtResult.Error = Get-SafeError $_.Exception
}

$pnpResult = [ordered]@{
    Status = 'Unavailable'
    Count = 0
    Devices = @()
    Error = $null
}
try {
    # CIM exposes hardware/compatible IDs without opening HID device handles.
    # OperationTimeoutSec bounds the local enumeration request. Only currently
    # present HID devices with the LampArray usage pair are included below.
    $pnpDevices = Get-CimInstance -ClassName Win32_PnPEntity `
        -Filter "PNPClass='HIDClass' AND Present=TRUE" `
        -Property Name, PNPDeviceID, HardwareID, CompatibleID -OperationTimeoutSec $TimeoutSeconds
    $taskPnpMatches = New-Object 'System.Collections.Generic.List[object]'
    foreach ($device in $pnpDevices) {
        $hardwareMatch = @($device.HardwareID) |
            Where-Object { $_ -match '(?i)UP:0059_U:0001$' }
        $compatibleMatch = @($device.CompatibleID) |
            Where-Object { $_ -match '(?i)UP:0059_U:0001$' }
        if (@($hardwareMatch).Count -gt 0 -or @($compatibleMatch).Count -gt 0) {
            $sources = @()
            if (@($hardwareMatch).Count -gt 0) { $sources += 'HardwareID' }
            if (@($compatibleMatch).Count -gt 0) { $sources += 'CompatibleID' }
            $taskPnpMatches.Add([pscustomobject]@{
                Name = Get-SanitizedDeviceName ([string]$device.Name)
                SanitizedId = Get-SanitizedDeviceId ([string]$device.PNPDeviceID)
                LampArrayUsageId = 'HID_DEVICE_UP:0059_U:0001'
                UsageEvidenceSources = $sources
            })
        }
    }
    $pnpResult.Devices = @($taskPnpMatches.ToArray())
    $pnpResult.Count = $taskPnpMatches.Count
    $pnpResult.Status = if ($taskPnpMatches.Count -gt 0) { 'Detected' } else { 'NotDetectedThisQuery' }
} catch {
    $pnpResult.Error = Get-SafeError $_.Exception
}

$os = [ordered]@{ ProductName = $null; DisplayVersion = $null; Build = $null; ProductNameSource = $null }
try {
    $version = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    # Registry ProductName can retain "Windows 10" on Windows 11. Do not use it.
    $os.DisplayVersion = $version.DisplayVersion
    $os.Build = [string]$version.CurrentBuildNumber + '.' + [string]$version.UBR
} catch { }
try {
    $taskOsInfo = Get-CimInstance -ClassName Win32_OperatingSystem `
        -Property Caption, BuildNumber -OperationTimeoutSec $TimeoutSeconds
    $os.ProductName = [string]$taskOsInfo.Caption
    $os.ProductNameSource = 'Win32_OperatingSystem.Caption'
    if ([string]::IsNullOrWhiteSpace($os.Build)) { $os.Build = [string]$taskOsInfo.BuildNumber }
} catch { }

[pscustomobject]@{
    ObservedAtUtc = [DateTime]::UtcNow.ToString('o')
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    OperatingSystem = [pscustomobject]$os
    ReadOnly = $true
    WinRTLampArrayEnumeration = [pscustomobject]$winrtResult
    PnpLampArrayUsage = [pscustomobject]$pnpResult
    Interpretation = 'Enumeration evidence only; does not verify physical LEDs, brightness, or Windows effects.'
} | ConvertTo-Json -Depth 6
