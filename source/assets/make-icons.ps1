<#
  Draws enable.ico and disable.ico. Frames below 256 px are classic 32-bit DIBs (read by
  every Windows API); the 256 px frame is PNG, as Windows expects. Also writes
  preview.png. ASCII-only on purpose (PowerShell 5.1).
#>
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$ain = [string][char]0x0639

function New-Frame([int]$s, [bool]$on) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.TextRenderingHint = 'AntiAliasGridFit'
  $g.Clear([System.Drawing.Color]::Transparent)

  $pad = [single][Math]::Max(1, [Math]::Round($s * 0.04))
  $r = [single]($s * 0.24)
  $w = [single]($s - 2 * $pad)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc($pad, $pad, 2 * $r, 2 * $r, 180, 90)
  $path.AddArc($pad + $w - 2 * $r, $pad, 2 * $r, 2 * $r, 270, 90)
  $path.AddArc($pad + $w - 2 * $r, $pad + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
  $path.AddArc($pad, $pad + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
  $path.CloseFigure()

  if ($on) { $c1 = [System.Drawing.Color]::FromArgb(255, 0xDE, 0x7E, 0x5D); $c2 = [System.Drawing.Color]::FromArgb(255, 0xB3, 0x50, 0x33) }
  else     { $c1 = [System.Drawing.Color]::FromArgb(255, 0x74, 0x73, 0x6E); $c2 = [System.Drawing.Color]::FromArgb(255, 0x3F, 0x3E, 0x3B) }
  $rect = New-Object System.Drawing.RectangleF $pad, $pad, $w, $w
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $c1, $c2, ([single]90)
  $g.FillPath($brush, $path)

  $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($s * 0.60)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $fmt = New-Object System.Drawing.StringFormat
  $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
  $textColor = if ($on) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(255, 0xE6, 0xE4, 0xDD) }
  $tb = New-Object System.Drawing.SolidBrush $textColor
  $g.DrawString($ain, $font, $tb, (New-Object System.Drawing.RectangleF 0, ([single]($s * -0.03)), $s, $s), $fmt)

  if (-not $on) {
    $penW = [single][Math]::Max(1.4, $s * 0.055)
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x3F, 0x3E, 0x3B)), ([single]($penW * 2.2))
    $edge.StartCap = 'Round'; $edge.EndCap = 'Round'
    $line = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0xF4, 0xF1, 0xEA)), $penW
    $line.StartCap = 'Round'; $line.EndCap = 'Round'
    $a = [single]($s * 0.24); $b = [single]($s * 0.76)
    $g.DrawLine($edge, $a, $a, $b, $b)
    $g.DrawLine($line, $a, $a, $b, $b)
  }
  $g.Dispose()
  return $bmp
}

function Get-Png([System.Drawing.Bitmap]$bmp) {
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  return ,$ms.ToArray()
}

function Get-Dib([System.Drawing.Bitmap]$bmp) {
  $s = $bmp.Width
  $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
  $stride = $data.Stride
  $raw = New-Object byte[] ($stride * $s)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
  $bmp.UnlockBits($data)

  $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter $ms
  $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
  $bw.Write([uint32]($s * $s * 4 + $maskRow * $s))
  $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
  for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($raw, $y * $stride, $s * 4) }
  $bw.Write((New-Object byte[] ($maskRow * $s)))
  $bw.Flush()
  return ,$ms.ToArray()
}

function Write-Ico([string]$file, [bool]$on, [ref]$previewFrames) {
  $sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
  $blobs = @()
  $previewFrames.Value = @()
  foreach ($s in $sizes) {
    $bmp = New-Frame $s $on
    if ($s -ge 256) { $blobs += ,(Get-Png $bmp) } else { $blobs += ,(Get-Dib $bmp) }
    if ($s -in 256, 48, 32, 16) { $previewFrames.Value += ,$bmp } else { $bmp.Dispose() }
  }
  $fs = [System.IO.File]::Create($file)
  $bw = New-Object System.IO.BinaryWriter $fs
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $blobs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
  }
  foreach ($b in $blobs) { $bw.Write($b) }
  $bw.Close()
}

$onFrames = $null; $offFrames = $null
Write-Ico (Join-Path $PSScriptRoot 'enable.ico') $true ([ref]$onFrames)
Write-Ico (Join-Path $PSScriptRoot 'disable.ico') $false ([ref]$offFrames)

$prev = New-Object System.Drawing.Bitmap 860, 290
$g = [System.Drawing.Graphics]::FromImage($prev)
$g.Clear([System.Drawing.Color]::FromArgb(255, 0x26, 0x26, 0x24))
$x = 14
foreach ($set in @($onFrames, $offFrames)) {
  foreach ($bmp in $set) { $g.DrawImageUnscaled($bmp, $x, 16); $x += $bmp.Width + 14 }
  $x += 20
}
$g.Dispose()
$prev.Save((Join-Path $PSScriptRoot 'preview.png'))
Write-Host 'icons written'
