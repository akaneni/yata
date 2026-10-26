# Generates Yata's brand marks as pixel-exact SVG in docs/brand/:
#   yata-mark.svg          the notch sprite's resting pose (16x14)
#   yata-mark-large.svg    the three-legged raven of the larger art (32x28)
#   yata-wordmark.svg      "yata" in pixel letters drawn on the sprite's grid
#   yata-lockup.svg        raven perched on the wordmark's baseline
#   yata-favicon.svg       the site's favicon, styled like the app icon
#   yata-og.png            the site's link preview
# The four marks come in -white and -black, plus an unsuffixed one that follows the viewer's light or
# dark theme (for the README). One <rect> per horizontal run, crispEdges, no anti-aliasing.
param([string]$Out = (Join-Path $PSScriptRoot "..\docs\brand"))
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force $Out | Out-Null

# Both ravens are read from their sources so the marks follow any redraw.
$source = Get-Content -Raw (Join-Path $root "src\Yata\Views\RavenSprite.cs")
$block = [regex]::Match($source, '\[RavenFrame\.Perch\]\s*=\s*Parse\((.*?)\)', 'Singleline').Groups[1].Value
$perch = [regex]::Matches($block, '"([.#]{16})"') | ForEach-Object { $_.Groups[1].Value }
$art = Get-Content -Raw (Join-Path $PSScriptRoot "make-art.ps1")
$block = [regex]::Match($art, '\$big = @\((.*?)\)', 'Singleline').Groups[1].Value
$big = [regex]::Matches($block, '"([.#]{32})"') | ForEach-Object { $_.Groups[1].Value }

# Lowercase, 2 px strokes, x-height 7, 2 rows of ascender and 3 of descender. The y's tail runs
# down-left at 45 degrees, the same slope as the raven's back.
$glyphs = @{
  'y' = ".......", ".......",
        "##...##", "##...##", "##...##", ".##.##.", ".##.##.", "..###..", "..##...",
        ".##....", ".##....", "##....."
  'a' = "......", "......",
        ".####.", "....##", ".#####", "##..##", "##..##", "##..##", ".#####",
        "......", "......", "......"
  't' = ".##...", ".##...",
        "#####.", ".##...", ".##...", ".##...", ".##...", ".##...", "..###.",
        "......", "......", "......"
}
$baseline = 9   # first row below the x-height

$word = @("") * 12
$letters = "yata".ToCharArray()
for ($i = 0; $i -lt $letters.Count; $i++) {
  $g = $glyphs[[string]$letters[$i]]
  $w = ($g | Measure-Object -Property Length -Maximum).Maximum
  for ($r = 0; $r -lt 12; $r++) { $word[$r] += $g[$r].PadRight($w, '.') + $(if ($i -lt $letters.Count - 1) { "." } else { "" }) }
}

# The raven's feet stand on the wordmark's baseline, two pixels to its left.
$gap = 2
$top = $perch.Count - $baseline
$lockW = 16 + $gap + $word[0].Length; $lockH = [Math]::Max($perch.Count, $top + $word.Count)
$lockup = for ($r = 0; $r -lt $lockH; $r++) {
  $left = if ($r -lt $perch.Count) { $perch[$r] } else { "." * 16 }
  $right = if ($r -ge $top -and $r - $top -lt $word.Count) { $word[$r - $top] } else { "." * $word[0].Length }
  $left + ("." * $gap) + $right
}

function Save-Svg([string[]]$rows, [string]$name, [string]$title) {
  $w = $rows[0].Length; $h = $rows.Count
  $rects = for ($y = 0; $y -lt $h; $y++) {
    $x = 0
    while ($x -lt $w) {
      if ($rows[$y][$x] -ne '#') { $x++; continue }
      $start = $x; while ($x -lt $w -and $rows[$y][$x] -eq '#') { $x++ }
      "<rect x=`"$start`" y=`"$y`" width=`"$($x - $start)`" height=`"1`"/>"
    }
  }
  $body = $rects -join ""
  $themes = @{
    "-white" = "<g fill=`"#fff`">$body</g>"
    "-black" = "<g fill=`"#0a0a0a`">$body</g>"
    "" = "<style>g{fill:#0a0a0a}@media (prefers-color-scheme:dark){g{fill:#fff}}</style><g>$body</g>"
  }
  foreach ($suffix in $themes.Keys) {
    $svg = "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"0 0 $w $h`" width=`"$($w * 8)`" height=`"$($h * 8)`" shape-rendering=`"crispEdges`" role=`"img`"><title>$title</title>$($themes[$suffix])</svg>"
    [IO.File]::WriteAllText((Join-Path $Out "$name$suffix.svg"), $svg + "`n")
  }
}

Save-Svg $perch "yata-mark" "Yata"
Save-Svg $big "yata-mark-large" "Yata"
Save-Svg $word "yata-wordmark" "yata"
Save-Svg $lockup "yata-lockup" "Yata"

# Favicon: the app icon's look (white raven on a rounded black square), as SVG for the site.
$rects = for ($y = 0; $y -lt $perch.Count; $y++) {
  foreach ($m in [regex]::Matches($perch[$y], '#+')) { "<rect x=`"$($m.Index + 2)`" y=`"$($y + 3)`" width=`"$($m.Length)`" height=`"1`"/>" }
}
$favicon = "<svg xmlns=`"http://www.w3.org/2000/svg`" viewBox=`"0 0 20 20`" shape-rendering=`"crispEdges`"><rect width=`"20`" height=`"20`" rx=`"4.4`" fill=`"#0a0a0a`" shape-rendering=`"geometricPrecision`"/><g fill=`"#fff`">$($rects -join '')</g></svg>"
[IO.File]::WriteAllText((Join-Path $Out "yata-favicon.svg"), $favicon + "`n")

# Link preview for the site (1200x630): the lockup, centred, white on black.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 1200, 630
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 10, 10, 10))
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 242, 242, 239))
$p = 16; $ox = [int]((1200 - $lockW * $p) / 2); $oy = [int]((630 - $lockH * $p) / 2)
for ($y = 0; $y -lt $lockH; $y++) { for ($x = 0; $x -lt $lockW; $x++) {
  if ($lockup[$y][$x] -eq '#') { $g.FillRectangle($brush, $ox + $x * $p, $oy + $y * $p, $p, $p) } } }
$bmp.Save((Join-Path $Out "yata-og.png"), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
"ok: $Out"
