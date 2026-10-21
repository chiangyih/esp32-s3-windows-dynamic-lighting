[CmdletBinding()]
param()
# Generates a quiet test signal in memory. This does not capture or save any audio.
# The current Windows volume and default output device are respected.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System
$rate = 48000
$seconds = 6
$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
$samples = $rate * $seconds
$writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
$writer.Write([int](36 + $samples * 2))
$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$writer.Write([int]16)
$writer.Write([int16]1)
$writer.Write([int16]1)
$writer.Write([int]$rate)
$writer.Write([int]($rate * 2))
$writer.Write([int16]2)
$writer.Write([int16]16)
$writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
$writer.Write([int]($samples * 2))
for ($i = 0; $i -lt $samples; $i++) {
    $time = $i / [double]$rate
    $amplitude = if ($time -lt 2) { 0.025 } elseif ($time -lt 4) { 0.07 } else { 0.18 }
    $ramp = [Math]::Min(1.0, [Math]::Min($time * 50, ($seconds - $time) * 50))
    $writer.Write([int16]([Math]::Sin(2 * [Math]::PI * 440 * $time) * $amplitude * $ramp * 32767))
}
$writer.Flush()
$stream.Position = 0
$player = [System.Media.SoundPlayer]::new($stream)
try {
    Write-Host 'Playing 6-second generated test signal (three increasing levels; no volume changes).'
    $player.PlaySync()
} finally {
    $player.Dispose()
    $writer.Dispose()
    $stream.Dispose()
}
