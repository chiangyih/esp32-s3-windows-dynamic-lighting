[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$buildDirectory = Join-Path $projectDirectory 'build\host-tests'
$sourcePath = Join-Path $PSScriptRoot 'protocol_tests.cpp'
$executablePath = Join-Path $buildDirectory 'protocol_tests.exe'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null

$gxx = Get-Command 'g++' -ErrorAction SilentlyContinue
$clangxx = Get-Command 'clang++' -ErrorAction SilentlyContinue
if ($gxx -or $clangxx) {
    $compilerPath = if ($gxx) { $gxx.Source } else { $clangxx.Source }
    & $compilerPath '-std=c++17' '-Wall' '-Wextra' '-Werror' '-pedantic' $sourcePath '-o' $executablePath
    if ($LASTEXITCODE -ne 0) { throw "C++ compilation failed ($LASTEXITCODE)." }
} else {
    $cl = Get-Command 'cl.exe' -ErrorAction SilentlyContinue
    if ($cl -and $env:INCLUDE -and $env:LIB) {
        & $cl.Source '/nologo' '/std:c++17' '/EHsc' '/W4' '/WX' '/utf-8' $sourcePath "/Fe$executablePath" "/Fo$buildDirectory\protocol_tests.obj"
        if ($LASTEXITCODE -ne 0) { throw "MSVC compilation failed ($LASTEXITCODE)." }
    } else {
        $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswherePath)) {
            throw 'No host C++ compiler found. Install/use Visual Studio C++ tools, g++, or clang++; no tools were installed by this script.'
        }
        $visualStudioPath = & $vswherePath -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if (-not $visualStudioPath) { throw 'Visual Studio C++ tools were not found.' }
        $devCommandPath = Join-Path $visualStudioPath 'Common7\Tools\VsDevCmd.bat'
        if (-not (Test-Path -LiteralPath $devCommandPath)) { throw "Missing developer environment: $devCommandPath" }
        # All arguments below are local paths. This temporary batch only compiles;
        # it performs no destructive filesystem operations and installs nothing.
        $batchPath = Join-Path $buildDirectory 'compile-host-tests.cmd'
        $batchText = @"
@echo off
chcp 65001 >nul
call "$devCommandPath" -no_logo -arch=x64 -host_arch=x64
if errorlevel 1 exit /b %errorlevel%
cl.exe /nologo /std:c++17 /EHsc /W4 /WX /utf-8 "$sourcePath" /Fe"$executablePath" /Fo"$buildDirectory\protocol_tests.obj"
exit /b %errorlevel%
"@
        $batchText = $batchText -replace "`r?`n", "`r`n"
        [System.IO.File]::WriteAllText($batchPath, $batchText, [System.Text.UTF8Encoding]::new($false))
        & $env:ComSpec /d /c "`"$batchPath`""
        if ($LASTEXITCODE -ne 0) { throw "MSVC compilation failed ($LASTEXITCODE)." }
    }
}

& $executablePath
if ($LASTEXITCODE -ne 0) { throw "Host protocol tests failed ($LASTEXITCODE)." }
