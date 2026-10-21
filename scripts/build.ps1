param([string]$Cli = 'arduino-cli')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskSketch = Join-Path $taskRoot '01-8_ws2812'
$taskOutput = Join-Path $taskRoot 'build'
$taskBoard = 'esp32:esp32:esp32s3:USBMode=default,CDCOnBoot=default,MSCOnBoot=default,DFUOnBoot=default,FlashMode=opi,FlashSize=32M,PSRAM=opi,PartitionScheme=app5M_fat24M_32MB,CPUFreq=80'
& $Cli compile --fqbn $taskBoard --warnings all --output-dir $taskOutput $taskSketch
if ($LASTEXITCODE -ne 0) { throw "Arduino compilation failed with exit code $LASTEXITCODE" }
Write-Output "Build completed: $taskOutput"
Write-Output 'N32R16 OPI memory settings are based on the board-family documentation; verify the real board before upload.'
