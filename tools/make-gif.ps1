# Generates docs/yata.gif: Yata's repertoire in a stylised notch, drawn from the sprite frames in
# src/Yata/Views/RavenSprite.cs with the timings of RavenAnimator. The frames are rendered here as a
# 25 fps PNG sequence; EffectCraft (ArtCraft suite, headless CLI) encodes the GIF.
param([string]$EffectCraft = (Join-Path $env:ProgramFiles "EffectCraft\effectcraft-cli.exe"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $EffectCraft)) { throw "EffectCraft not found ($EffectCraft); install it from the ArtCraft Launcher" }

$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) "yata-gif"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

$source = Get-Content -Raw (Join-Path $root "src\Yata\Views\RavenSprite.cs")
$frames = @{}
foreach ($m in [regex]::Matches($source, '\[RavenFrame\.(\w+)\]\s*=\s*Parse\((.*?)\)', 'Singleline')) {
  $frames[$m.Groups[1].Value] = @([regex]::Matches($m.Groups[2].Value, '"([.#]{16})"') | ForEach-Object { $_.Groups[1].Value })
}
$envelope = "#####", "##.##", "#.#.#", "#####"

$fps = 25; $px = 6; $width = 480; $height = 150
$notch = @{ Left = 70; Right = 410; Bottom = 132; Radius = 30 }

# Timeline: frame, milliseconds, facing left, horizontal offset (px), carrying the letter.
$timeline = New-Object System.Collections.Generic.List[object]
function Add([string]$frame, [int]$ms, [bool]$left = $false, [int]$dx = 0, [bool]$letter = $false) {
  $timeline.Add(@($frame, $ms, $left, $dx, $letter))
}

Add Perch 900; Add Blink 130; Add Perch 900
Add Perch 1400 $true; Add Perch 500
Add LookUp 1500; Add Perch 500
Add Caw 170; Add Perch 140; Add Caw 170; Add Perch 700
foreach ($i in 1, 2) { Add Crouch 90; Add Hop 160; Add Crouch 90; Add Perch $(if ($i -eq 1) { 250 } else { 700 }) }
Add Stretch 900; Add Blink 130; Add Perch 700
Add Crouch 110; foreach ($i in 1..3) { Add Fluff 80; Add FluffSplash 80 $true }; Add Perch 250; Add Blink 130; Add Perch 700
Add Preen 700; Add Perch 500
foreach ($i in 1, 2) { Add Sleep 1900; Add SleepIn 1300 }
Add Perch 400
# Flies off to the right and comes back with a letter.
$flap = "FlyUp", "FlyMid", "FlyDown", "FlyMid"
for ($i = 0; $i -lt 10; $i++) { Add $flap[$i % 4] 85 $false ([int][Math]::Truncate(220 * [Math]::Pow(($i + 1) / 10, 2))) }
for ($i = 0; $i -lt 12; $i++) { Add $flap[$i % 4] 85 $true ([int][Math]::Truncate(220 * [Math]::Sqrt(1 - ($i + 1) / 12))) $true }
Add Crouch 120 $false 0 $true; Add Perch 1400 $false 0 $true; Add Blink 130 $false 0 $true; Add Perch 900 $false 0 $true

$background = [System.Drawing.Color]::FromArgb(38, 38, 38)
function Render([string]$frame, [bool]$left, [int]$dx, [bool]$letter, [string]$path) {
  $bmp = New-Object System.Drawing.Bitmap $width, $height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear($background)
  $g.SmoothingMode = 'None'
  # The notch: flat top, rounded bottom corners.
  $r = $notch.Radius; $d = 2 * $r
  $path2 = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path2.AddLine($notch.Left, 0, $notch.Right, 0)
  $path2.AddArc($notch.Right - $d, $notch.Bottom - $d, $d, $d, 0, 90)
  $path2.AddArc($notch.Left, $notch.Bottom - $d, $d, $d, 90, 90)
  $path2.CloseFigure()
  $g.FillPath([System.Drawing.Brushes]::Black, $path2)

  $rows = @($frames[$frame] | ForEach-Object { , $_.ToCharArray() })
  if ($letter) {
    for ($y = 0; $y -lt 4; $y++) { for ($x = 0; $x -lt 5; $x++) { if ($envelope[$y][$x] -eq '#') { $rows[10 + $y][11 + $x] = '#' } } }
  }
  $ox = [int](($width - 16 * $px) / 2) + $dx
  $oy = $notch.Bottom - 14 * $px - 18
  for ($y = 0; $y -lt 14; $y++) {
    for ($x = 0; $x -lt 16; $x++) {
      if ($rows[$y][$x] -ne '#') { continue }
      $col = if ($left) { 15 - $x } else { $x }
      $left0 = $ox + $col * $px
      # Yata disappears past the notch's edge.
      if ($left0 -lt $notch.Left + 14 -or $left0 + $px -gt $notch.Right - 14) { continue }
      $g.FillRectangle([System.Drawing.Brushes]::White, $left0, $oy + $y * $px, $px, $px)
    }
  }
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

# Hold each pose for its duration, on a fixed 25 fps grid.
$t = 0.0; $n = 0
foreach ($step in $timeline) {
  $frame, $ms, $left, $dx, $letter = $step
  $t += $ms
  $first = $null
  while ($n * 1000 / $fps -lt $t) {
    $out = Join-Path $work ("yata_{0:D4}.png" -f $n)
    if ($first) { Copy-Item $first $out } else { Render $frame $left $dx $letter $out; $first = $out }
    $n++
  }
}

function Invoke-EffectCraft([string[]]$arguments) {
  # Windows PowerShell (and pwsh before 7.3) drops the quotes inside native arguments.
  if ($PSVersionTable.PSVersion -lt [version]"7.3") { $arguments = $arguments | ForEach-Object { $_ -replace '"', '\"' } }
  # Progress goes to stderr, which Windows PowerShell would turn into an error under Stop.
  $ErrorActionPreference = "Continue"
  $output = & $EffectCraft @arguments 2>&1
  if ($LASTEXITCODE -ne 0) { throw "effectcraft-cli failed: $output" }
}

$project = Join-Path $work "yata.ecproj"
$seconds = $n / $fps
$first = (Join-Path $work "yata_0000.png") -replace '\\', '/'
Invoke-EffectCraft @("run",
  "comp.new", (ConvertTo-Json @{ name = "Yata"; width = $width; height = $height; frameRate = $fps; duration = $seconds; background = "#262626" } -Compress),
  "file.import", (ConvertTo-Json @{ paths = @($first); sequence = $true; frameRate = $fps } -Compress),
  "comp.open", '{"comp":1}',
  "layer.addItem", '{"item":2,"time":0}',
  "--empty", "--save-as", $project)
Invoke-EffectCraft @("render", "--project", $project, "--comp", "Yata", "--out", (Join-Path $root "docs\yata.gif"), "--format", "gif", "--fps", "$fps", "--quality", "best")
"ok: $n frames, $seconds s"
