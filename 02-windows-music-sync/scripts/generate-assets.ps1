[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$specs = @(
    @{ Name = 'StoreLogo.png'; Width = 50; Height = 50 },
    @{ Name = 'Square44x44Logo.png'; Width = 44; Height = 44 },
    @{ Name = 'Square150x150Logo.png'; Width = 150; Height = 150 },
    @{ Name = 'Wide310x150Logo.png'; Width = 310; Height = 150 },
    @{ Name = 'AppIcon.ico'; Width = 256; Height = 256; IsIcon = $true }
)
foreach ($spec in $specs) {
    $bitmap = [Drawing.Bitmap]::new($spec.Width, $spec.Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#101828'))
        $unit = [Math]::Min($spec.Width, $spec.Height)
        $barWidth = $unit * 0.065
        $gap = $unit * 0.028
        $totalWidth = 8 * $barWidth + 7 * $gap
        $left = ($spec.Width - $totalWidth) / 2
        $heights = @(0.22, 0.34, 0.46, 0.61, 0.50, 0.38, 0.27, 0.42)
        $colors = @('#38BDF8', '#22D3EE', '#2DD4BF', '#34D399', '#A3E635', '#FACC15', '#FB923C', '#F472B6')
        for ($i = 0; $i -lt 8; $i++) {
            $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($colors[$i]))
            try {
                $height = $unit * $heights[$i]
                $graphics.FillRectangle($brush, [single]($left + $i * ($barWidth + $gap)), [single](($spec.Height + $unit * 0.61) / 2 - $height), [single]$barWidth, [single]$height)
            } finally { $brush.Dispose() }
        }
        $assetPath = Join-Path $OutputDirectory $spec.Name
        if ($spec.IsIcon) {
            # Standard ICONDIR + one ICONDIRENTRY containing a 256px PNG image.
            # A zero dimension byte represents 256 pixels in the ICO format.
            $pngStream = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
                $pngBytes = $pngStream.ToArray()
                $iconStream = [IO.File]::Create($assetPath)
                $writer = [IO.BinaryWriter]::new($iconStream)
                try {
                    $writer.Write([uint16]0)
                    $writer.Write([uint16]1)
                    $writer.Write([uint16]1)
                    $writer.Write([byte]0)
                    $writer.Write([byte]0)
                    $writer.Write([byte]0)
                    $writer.Write([byte]0)
                    $writer.Write([uint16]1)
                    $writer.Write([uint16]32)
                    $writer.Write([uint32]$pngBytes.Length)
                    $writer.Write([uint32]22)
                    $writer.Write($pngBytes)
                } finally {
                    $writer.Dispose()
                    $iconStream.Dispose()
                }
            } finally { $pngStream.Dispose() }
        } else {
            $bitmap.Save($assetPath, [Drawing.Imaging.ImageFormat]::Png)
        }
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
