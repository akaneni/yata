# Generates Yata's larger artwork with PhotoCraft (ArtCraft suite, headless CLI):
#   docs/yata-hero.png               README hero (1024x384)
#   installer/wizard-image-*.png     Inno Setup welcome image, one per display scale
#   installer/wizard-small-*.png     Inno Setup header image, one per display scale
# Everything is drawn at 1 pixel per art pixel, dithered to 1-bit (ordered/Bayer) and scaled
# up with nearest-neighbour by whole numbers, so the output is pure black and white like the
# notch sprite. The installer never stretches them (WizardImageStretch=no): each file is exactly
# the size of the wizard's image area at one scale, art centred on black.
param([string]$PhotoCraft = (Join-Path $env:ProgramFiles "PhotoCraft\photocraft-cli.exe"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $PhotoCraft)) { throw "PhotoCraft not found ($PhotoCraft); install it from the ArtCraft Launcher" }

$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) "yata-art"
New-Item -ItemType Directory -Force $work, (Join-Path $root "docs") | Out-Null

# Yata at twice the sprite's resolution, redrawn by hand rather than scaled, with three legs
# like the Yatagarasu of the myth (the notch sprite keeps two: three don't read at 16 px).
$big = @(
  "................................",
  "................................",
  "...................######.......",
  ".................##########.....",
  "................############....",
  "................#############...",
  "................###..#########..",
  "................###..##########.",
  "................################",
  "................##############..",
  "...............##########.......",
  "..............###########.......",
  ".............############.......",
  "............#############.......",
  "...........###########.##.......",
  "..........###########.###.......",
  ".........###########.###........",
  "........###########.###.........",
  ".......###########.###..........",
  "......###########.###...........",
  ".....###########.###............",
  "....###########.####............",
  "..##########.#..#..#............",
  ".######.###..#..#..#............",
  "#####...#....#..#..#............",
  "##...........#..#..#............",
  ".............##.##.##...........",
  "................................")

# The notch sprite's resting pose, read from the source so the art follows any redraw.
$source = Get-Content -Raw (Join-Path $root "src\Yata\Views\RavenSprite.cs")
$perchBlock = [regex]::Match($source, '\[RavenFrame\.Perch\]\s*=\s*Parse\((.*?)\)', 'Singleline').Groups[1].Value
$perch = [regex]::Matches($perchBlock, '"([.#]{16})"') | ForEach-Object { $_.Groups[1].Value }

$font = @{
  'Y' = "#...#", ".#.#.", "..#..", "..#..", "..#.."
  'A' = ".###.", "#...#", "#####", "#...#", "#...#"
  'T' = "#####", "..#..", "..#..", "..#..", "..#.."
}

# Writes lit pixels as white on transparency; $outline adds a 1 px black ring so Yata stands
# out against the dithered sun.
function Save-Pixels([string[]]$rows, [string]$path, [int]$scale = 1, [switch]$outline) {
  $pad = if ($outline) { 1 } else { 0 }
  $w = $rows[0].Length + 2 * $pad; $h = $rows.Count + 2 * $pad
  $bmp = New-Object System.Drawing.Bitmap ($w * $scale), ($h * $scale), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  $lit = for ($y = 0; $y -lt $rows.Count; $y++) { for ($x = 0; $x -lt $rows[$y].Length; $x++) { if ($rows[$y][$x] -eq '#') { ,@(($x + $pad), ($y + $pad)) } } }
  if ($outline) {
    foreach ($p in $lit) { $g.FillRectangle([System.Drawing.Brushes]::Black, ($p[0] - 1) * $scale, ($p[1] - 1) * $scale, 3 * $scale, 3 * $scale) }
  }
  foreach ($p in $lit) { $g.FillRectangle([System.Drawing.Brushes]::White, $p[0] * $scale, $p[1] * $scale, $scale, $scale) }
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

function Invoke-PhotoCraft([string[]]$arguments) {
  # Windows PowerShell (and pwsh before 7.3) drops the quotes inside native arguments.
  if ($PSVersionTable.PSVersion -lt [version]"7.3") { $arguments = $arguments | ForEach-Object { $_ -replace '"', '\"' } }
  $output = & $PhotoCraft @arguments 2>&1
  if ($LASTEXITCODE -ne 0) { throw "photocraft-cli failed: $output" }
}

# PhotoCraft takes each --params as one JSON argument.
function Step([string]$command, [hashtable]$params) {
  if ($params) { return @("--cmd", $command, "--params", (ConvertTo-Json $params -Compress -Depth 5)) }
  return @("--cmd", $command)
}

$ravenPng = Join-Path $work "raven-big.png"; Save-Pixels $big $ravenPng -outline
$wordRows = 0..4 | ForEach-Object { $r = $_; ("YATA".ToCharArray() | ForEach-Object { $font[[string]$_][$r] }) -join "." }
$wordPng = Join-Path $work "word.png"; Save-Pixels $wordRows $wordPng

# A dithered sun rising behind Yata (the Yatagarasu is the sun's messenger), clipped at the horizon.
function Sun([int]$x, [int]$y, [int]$size, [int]$horizon, [int]$width, [int]$height) {
  $cx = $x + $size / 2; $cy = $y + $size / 2
  return (Step "select.rect" @{ x = $x; y = $y; width = $size; height = $size; ellipse = $true; antiAlias = $false }) +
    (Step "paint.gradient" @{ from = @($cx, $cy); to = @($cx, $y); style = "radial"; stops = @(@(0, "#a8a8a8"), @(0.5, "#646464"), @(1, "#000000")) }) +
    (Step "select.rect" @{ x = 0; y = $horizon; width = $width; height = $height - $horizon; antiAlias = $false }) +
    (Step "edit.fill" @{ contents = "black" }) +
    (Step "select.deselect") +
    (Step "image.mode.bitmap" @{ method = "pattern" }) +
    (Step "image.mode.grayscale")
}

function Place([string]$path, [int]$cx, [int]$cy) {
  return Step "file.placeEmbedded" @{ path = $path; scale = 100; fit = $false; smartObject = $false; center = @($cx, $cy) }
}

function Line([int]$x, [int]$y, [int]$width) {
  return (Step "select.rect" @{ x = $x; y = $y; width = $width; height = 1; antiAlias = $false }) +
    (Step "edit.fill" @{ contents = "white" }) + (Step "select.deselect")
}

# Scales the art up by whole pixels, then centres it on a black canvas of the given size.
function Scale([string]$doc, [string]$out, [int]$artWidth, [int]$artHeight, [int]$width, [int]$height) {
  $k = [Math]::Min([Math]::Floor($width / $artWidth), [Math]::Floor($height / $artHeight))
  Invoke-PhotoCraft (@("run", $doc) +
    (Step "image.imageSize" @{ width = $artWidth * $k; height = $artHeight * $k; resample = "nearest" }) +
    (Step "image.canvasSize" @{ width = $width; height = $height; anchor = "center"; extensionColor = "black" }) +
    @("--out", $out))
}

# Hero: 128x48 art pixels, x8.
$hero = Join-Path $work "hero.pcraft"
Invoke-PhotoCraft (@("run", "--new", '{"width":128,"height":48,"mode":"gray","background":"black","name":"Yata"}') +
  (Sun 36 4 56 40 128 48) + (Place $ravenPng 64 27) + (Step "layer.flattenImage") + (Line 14 40 100) + @("--out", $hero))
Scale $hero (Join-Path $root "docs\yata-hero.png") 128 48 1024 384

# Inno Setup 6.6+ image areas at 100/125/150/175/200% (larger scales pick the 200% file and
# centre it); see WizardImageFile and WizardSmallImageFile in the Inno Setup help.
# (String keys: an ordered dictionary indexed by an integer reads it as a position.)
$areas = [ordered]@{ "100" = @(202, 386, 58); "125" = @(269, 515, 77); "150" = @(336, 643, 97); "175" = @(403, 772, 116); "200" = @(430, 824, 124) }

# Welcome image: 41x79 art pixels.
$wizard = Join-Path $work "wizard.pcraft"
Invoke-PhotoCraft (@("run", "--new", '{"width":41,"height":79,"mode":"gray","background":"black","name":"Yata"}') +
  (Sun 1 20 40 54 41 79) + (Place $ravenPng 21 41) + (Place $wordPng 21 65) + (Step "layer.flattenImage") + (Line 3 54 35) + @("--out", $wizard))

# Header image: the notch sprite itself, with a pixel of margin.
$small = Join-Path $work "small.png"; Save-Pixels $perch $small
$smallDoc = Join-Path $work "small.pcraft"
Invoke-PhotoCraft (@("run", "--new", '{"width":18,"height":16,"mode":"gray","background":"black"}') +
  (Place $small 9 8) + (Step "layer.flattenImage") + @("--out", $smallDoc))

foreach ($scale in $areas.Keys) {
  $w, $h, $side = $areas[$scale]
  Scale $wizard (Join-Path $root "installer\wizard-image-$scale.png") 41 79 $w $h
  Scale $smallDoc (Join-Path $root "installer\wizard-small-$scale.png") 18 16 $side $side
}
"ok"
