[CmdletBinding()]
param(
    [string]$Cli = 'arduino-cli',
    [ValidateRange(1, 64)][int]$LampCount = 8,
    [ValidateRange(1, 100)][int]$LedBudgetMa = 50,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskSketch = Join-Path $taskRoot '01-8_ws2812'
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot 'build' }
$taskBoard = 'esp32:esp32:esp32s3:USBMode=default,CDCOnBoot=default,MSCOnBoot=default,DFUOnBoot=default,FlashMode=opi,FlashSize=32M,PSRAM=opi,PartitionScheme=app5M_fat24M_32MB,CPUFreq=80'
$taskDefines = "-DCONFIG_LAMP_COUNT=$LampCount -DCONFIG_LED_BUDGET_MA=$LedBudgetMa"
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& $Cli compile --fqbn $taskBoard --warnings all --output-dir $taskOutput --build-property "compiler.cpp.extra_flags=$taskDefines" $taskSketch
if ($LASTEXITCODE -ne 0) { throw "Arduino compilation failed with exit code $LASTEXITCODE" }
$taskFiles = @(Get-ChildItem -LiteralPath $taskOutput -File | Where-Object {
    $_.Name -like '01-8_ws2812.ino*' -and $_.Extension -in @('.bin', '.elf', '.map')
} | ForEach-Object {
    [ordered]@{ File = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$taskReport = [ordered]@{
    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Status = 'Compiled'
    Sketch = '01-8_ws2812'
    BoardFqbn = $taskBoard
    LampCount = $LampCount
    LedBranchBudgetMa = $LedBudgetMa
    BuildDefines = $taskDefines
    ConfigSourceSha256 = (Get-FileHash -LiteralPath (Join-Path $taskSketch 'Config.h') -Algorithm SHA256).Hash
    Uploaded = $false
    PhysicalLampCountVerified = $false
    UsbCurrentMeasured = $false
    Files = $taskFiles
}
$taskReport | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'firmware-build-report.json') -Encoding utf8
Write-Output "Build completed: $taskOutput"
Write-Output "Lamp count: $LampCount; LED branch estimate: $LedBudgetMa mA. No upload or USB-current measurement was performed."
Write-Output 'N32R16 OPI memory settings are based on the board-family documentation; verify the real board before upload.'
