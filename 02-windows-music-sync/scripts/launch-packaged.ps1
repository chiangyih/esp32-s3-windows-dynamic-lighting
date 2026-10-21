[CmdletBinding()]
param(
    [string]$IdentityName = 'LocalDev.Esp32MusicSync',
    [string]$Arguments = '',
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$package = Get-AppxPackage -Name $IdentityName
if (@($package).Count -ne 1) { throw "Expected exactly one installed package for $IdentityName." }
$aumid = "$($package.PackageFamilyName)!App"
if (-not ('Esp32MusicSync.Packaging.Activation' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Esp32MusicSync.Packaging
{
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
        [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
        [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray, out uint processId);
    }
    public static class Activation
    {
        public static uint Launch(string appUserModelId, string arguments)
        {
            var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), true);
            var manager = (IApplicationActivationManager)Activator.CreateInstance(type);
            try
            {
                uint processId;
                int hr = manager.ActivateApplication(appUserModelId, arguments, 0, out processId);
                Marshal.ThrowExceptionForHR(hr);
                return processId;
            }
            finally { Marshal.ReleaseComObject(manager); }
        }
    }
}
'@
}
$processId = [Esp32MusicSync.Packaging.Activation]::Launch($aumid, $Arguments)
Write-Host "Activated $aumid (PID $processId)."
if ($PassThru) { [pscustomobject]@{ ProcessId = $processId; AppUserModelId = $aumid; Arguments = $Arguments } }
