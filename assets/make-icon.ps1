Add-Type -AssemblyName System.Drawing
$sizes = 256, 128, 64, 48, 32, 24, 16
$pngData = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap([int]$s, [int]$s)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
  $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
  $rectF = New-Object System.Drawing.RectangleF 0, 0, ([float]$s), ([float]$s)
  $c1 = [System.Drawing.Color]::FromArgb(255, 124, 92, 255)
  $c2 = [System.Drawing.Color]::FromArgb(255, 77, 123, 255)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ($rect, $c1, $c2, 45.0)
  $g.FillRectangle($brush, $rect)
  $fontSize = [float]([math]::Max($s * 0.55, 6))
  $font = New-Object System.Drawing.Font ('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object System.Drawing.StringFormat
  $sf.Alignment = [System.Drawing.StringAlignment]::Center
  $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
  $g.DrawString('Z', $font, [System.Drawing.Brushes]::White, $rectF, $sf)
  $font.Dispose(); $brush.Dispose(); $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngData += ,$ms.ToArray()
  $ms.Dispose(); $bmp.Dispose()
}

$icoPath = "D:\UPROJECT\ZapretGUI\assets\app.ico"
$fs = [System.IO.File]::Open($icoPath, 'Create')
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $bw.Write([byte]($(if ($s -ge 256) {0} else {$s})))
  $bw.Write([byte]($(if ($s -ge 256) {0} else {$s})))
  $bw.Write([byte]0)
  $bw.Write([byte]0)
  $bw.Write([uint16]1)
  $bw.Write([uint16]32)
  $bw.Write([uint32]$pngData[$i].Length)
  $bw.Write([uint32]$offset)
  $offset += $pngData[$i].Length
}
foreach ($d in $pngData) { $bw.Write($d) }
$bw.Close(); $fs.Close()
"Icon: {0:N1} KB" -f ((Get-Item $icoPath).Length / 1KB)
