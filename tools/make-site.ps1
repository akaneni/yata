# Builds getyata.app into _site/ (deployed to GitHub Pages by .github/workflows/pages.yml):
#   site/index.html is the English page; site/pt.json translates every data-i18n,
#   data-i18n-html, data-i18n-label (aria-label) and data-i18n-content (meta content) into
#   pt/index.html. {{LOCKUP}} becomes the inline lockup from docs/brand (run make-brand.ps1
#   after redrawing the raven). Fails on a missing translation or a leftover {{token}}.
# Runs on Windows PowerShell 5.1 and on pwsh (the Pages workflow uses Linux).
param([string]$Out = (Join-Path $PSScriptRoot "../_site"))
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$site = Join-Path $root "site"
$brand = Join-Path $root "docs/brand"
$utf8 = New-Object System.Text.UTF8Encoding $false

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory -Force $Out | Out-Null
Copy-Item -Recurse (Join-Path $site "*") $Out -Exclude "pt.json"
Copy-Item (Join-Path $brand "yata-favicon.svg") (Join-Path $Out "favicon.svg")
Copy-Item (Join-Path $brand "yata-og.png") (Join-Path $Out "og.png")
Copy-Item (Join-Path $root "src/Yata/Assets/yata.ico") (Join-Path $Out "favicon.ico")
[IO.File]::WriteAllText((Join-Path $Out "CNAME"), "getyata.app`n", $utf8)

# The lockup takes the text colour of wherever it sits.
$lockup = [IO.File]::ReadAllText((Join-Path $brand "yata-lockup-white.svg"), $utf8).Trim()
$lockup = $lockup.Replace('<g fill="#fff">', '<g fill="currentColor">').Replace('role="img"', 'aria-hidden="true"')
$lockup = [regex]::Replace($lockup, '^(<svg[^>]*?) width="\d+" height="\d+"', '$1')
$lockup = [regex]::Replace($lockup, '<title>.*?</title>', '')

$pt = @{}
(ConvertFrom-Json ([IO.File]::ReadAllText((Join-Path $site "pt.json"), $utf8))).PSObject.Properties | ForEach-Object { $pt[$_.Name] = $_.Value }

function Get-Pt([string]$key) {
  if (-not $pt.ContainsKey($key)) { throw "site/pt.json has no translation for '$key'" }
  $pt[$key]
}

# Only what HTML needs escaped; accents stay as they are.
function ConvertTo-Html([string]$text) { $text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;") }

function Convert-ToPortuguese([string]$html) {
  $html = [regex]::Replace($html, '(?s)<(\w+)([^>]*?\sdata-i18n(-html)?="([^"]+)"[^>]*)>(.*?)</\1>', [Text.RegularExpressions.MatchEvaluator] {
    param($m)
    $value = Get-Pt $m.Groups[4].Value
    if (-not $m.Groups[3].Success) { $value = ConvertTo-Html $value }
    "<$($m.Groups[1].Value)$($m.Groups[2].Value)>$value</$($m.Groups[1].Value)>"
  })
  foreach ($pair in @(@("label", "aria-label"), @("content", "content"))) {
    $html = [regex]::Replace($html, "<[^>]*\sdata-i18n-$($pair[0])=`"([^`"]+)`"[^>]*>", [Text.RegularExpressions.MatchEvaluator] {
      param($m)
      $value = ConvertTo-Html (Get-Pt $m.Groups[1].Value)
      [regex]::Replace($m.Value, "\s$($pair[1])=`"[^`"]*`"", " $($pair[1])=`"$value`"")
    })
  }
  $html.Replace('href="/privacy"', 'href="/pt/privacy"')
}

function Save-Page([string]$html, [string]$path) {
  $html = $html.Replace("{{LOCKUP}}", $lockup)
  $left = [regex]::Match($html, '\{\{\w+\}\}')
  if ($left.Success) { throw "$path still has $($left.Value)" }
  New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
  [IO.File]::WriteAllText($path, $html, $utf8)
}

$index = [IO.File]::ReadAllText((Join-Path $site "index.html"), $utf8)
$en = $index.Replace("{{LANG}}", "en").Replace("{{CANONICAL}}", "https://getyata.app/").Replace("{{CUR_EN}}", ' aria-current="page"').Replace("{{CUR_PT}}", "")
Save-Page $en (Join-Path $Out "index.html")
$ptPage = $index.Replace("{{LANG}}", "pt-BR").Replace("{{CANONICAL}}", "https://getyata.app/pt/").Replace("{{CUR_EN}}", "").Replace("{{CUR_PT}}", ' aria-current="page"')
Save-Page (Convert-ToPortuguese $ptPage) (Join-Path $Out "pt/index.html")

foreach ($page in "privacy.html", "pt/privacy.html") {
  Save-Page ([IO.File]::ReadAllText((Join-Path $site $page), $utf8)) (Join-Path $Out $page)
}
"ok: $Out"
