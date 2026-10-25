# Generates src/Yata/Assets/yata.ico from Yata's pixel-art "perch" frame.
# White raven on a rounded black square, PNG-compressed ICO entries.
param([string]$Out = (Join-Path $PSScriptRoot "..\src\Yata\Assets\yata.ico"))
Add-Type -AssemblyName System.Drawing
$sprite = @(
  "................",
  ".........####...",
  "........######..",
  "........##.####.",
  "........########",
  ".......#####....",
  "......######....",
  ".....#######....",
  "....########....",
  "...########.....",
  "..#######.......",
  ".####.#.#.......",
  "##....#.#.......",
  ".....##.##......")
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $r = [Math]::Max(2, [int]($s * 0.22)); $d = $r * 2
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
  $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90); $path.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 10, 10, 10))), $path)
  $g.SmoothingMode = 'None'
  $p = [Math]::Max(1, [Math]::Round($s * 0.7 / 16))
  $ox = [int](($s - 16 * $p) / 2); $oy = [int](($s - 14 * $p) / 2)
  for ($y = 0; $y -lt 14; $y++) { for ($x = 0; $x -lt 16; $x++) {
    if ($sprite[$y][$x] -eq '#') { $g.FillRectangle([System.Drawing.Brushes]::White, $ox + $x * $p, $oy + $y * $p, $p, $p) } } }
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,@($s, $ms.ToArray()); $g.Dispose(); $bmp.Dispose()
}
$fs = [System.IO.File]::Create($Out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
  $s = $e[0]; $data = $e[1]; $dim = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($e in $pngs) { $w.Write([byte[]]$e[1]) }
$w.Close()
# Preview sheet next to the icon for review.
$sheet = New-Object System.Drawing.Bitmap 520, 270; $g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(255, 240, 240, 240))
$x = 4; foreach ($e in $pngs) { $img = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream (,$e[1]))); $g.DrawImage($img, $x, 4, $e[0], $e[0]); $x += $e[0] + 6; if ($x -gt 260) { break } }
$sheet.Save((Join-Path $PSScriptRoot "yata-icon.preview.png"))
"ok: $Out"
