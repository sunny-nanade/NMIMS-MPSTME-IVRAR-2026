param([string]$name = "shot", [int]$x = 0, [int]$y = 0, [int]$w = 0, [int]$h = 0)
# Captures the primary screen (or a region in logical pixels) to the scratchpad as PNG.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($w -le 0) { $x = 0; $y = 0; $w = $b.Width; $h = $b.Height }
$bmp = New-Object Drawing.Bitmap $w, $h
$g = [Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object Drawing.Size $w, $h))
$dir = Join-Path (Split-Path -Parent $PSScriptRoot) "Screenshots"; New-Item -ItemType Directory -Force $dir | Out-Null; $out = Join-Path $dir ($name + ".png")
$bmp.Save($out)
$out
