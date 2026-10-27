# Builds getyata.app into _site/ (deployed to GitHub Pages by .github/workflows/pages.yml):
#   site/index.html is the English page; site/pt.json translates every data-i18n,
#   data-i18n-html, data-i18n-label (aria-label), data-i18n-content (meta content), data-i18n-alt
#   and data-i18n-src (the screenshot in each language) into
#   pt/index.html, and {{JSONLD}} and {{FAQ_JSONLD}} become the schema.org data in each
#   language (the FAQ one is read from the page's <details class="faq-item">). robots.txt,
#   sitemap.xml and llms.txt are copied as they are. {{LOCKUP}} becomes the inline lockup from docs/brand (run make-brand.ps1
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
  foreach ($pair in @(@("label", "aria-label"), @("content", "content"), @("alt", "alt"), @("src", "src"))) {
    $html = [regex]::Replace($html, "<[^>]*\sdata-i18n-$($pair[0])=`"([^`"]+)`"[^>]*>", [Text.RegularExpressions.MatchEvaluator] {
      param($m)
      $value = ConvertTo-Html (Get-Pt $m.Groups[1].Value)
      [regex]::Replace($m.Value, "\s$($pair[1])=`"[^`"]*`"", " $($pair[1])=`"$value`"")
    })
  }
  $html.Replace('href="/privacy"', 'href="/pt/privacy"').Replace('href="/code-signing"', 'href="/pt/code-signing"')
}

# schema.org data for search engines and AI assistants, in the page's language.
function Get-JsonLd([string]$lang, [string]$url, [string]$description) {
  $app = [ordered]@{
    "@context" = "https://schema.org"
    "@type" = "SoftwareApplication"
    name = "Yata"
    alternateName = "Yata for Windows"
    url = $url
    inLanguage = $lang
    description = $description
    applicationCategory = "UtilitiesApplication"
    operatingSystem = "Windows 10, Windows 11"
    downloadUrl = "https://github.com/akaneni/yata/releases/latest"
    fileSize = "6MB"
    image = "https://getyata.app/og.png"
    screenshot = "https://getyata.app/img/notch-open-$(if ($lang -eq "en") { "en" } else { "pt" }).png"
    license = "https://opensource.org/licenses/MIT"
    isAccessibleForFree = $true
    offers = [ordered]@{ "@type" = "Offer"; price = "0"; priceCurrency = "USD" }
    author = [ordered]@{ "@type" = "Person"; name = "akaneni"; url = "https://github.com/akaneni" }
    sameAs = @("https://github.com/akaneni/yata")
  }
  "<script type=`"application/ld+json`">$(ConvertTo-Json $app -Depth 4 -Compress)</script>"
}

# FAQPage data read back from the finished page, so the questions live only in the HTML.
function Add-FaqJsonLd([string]$html) {
  $plain = { param($s) [Net.WebUtility]::HtmlDecode(([regex]::Replace($s, '<[^>]+>', ''))) }
  $questions = @([regex]::Matches($html, '(?s)<details class="faq-item"><summary[^>]*>(.*?)</summary><p[^>]*>(.*?)</p></details>') | ForEach-Object {
    [ordered]@{
      "@type" = "Question"
      name = & $plain $_.Groups[1].Value
      acceptedAnswer = [ordered]@{ "@type" = "Answer"; text = & $plain $_.Groups[2].Value }
    }
  })
  if ($questions.Count -eq 0) { throw "index.html has {{FAQ_JSONLD}} but no FAQ items" }
  $faq = [ordered]@{ "@context" = "https://schema.org"; "@type" = "FAQPage"; mainEntity = $questions }
  $html.Replace("{{FAQ_JSONLD}}", "<script type=`"application/ld+json`">$(ConvertTo-Json $faq -Depth 5 -Compress)</script>")
}

# Cloudflare and browsers keep style.css and main.js for 4 hours, so a deploy could pair new
# HTML with old CSS. A content hash in the URL makes every change a new file.
function Get-Version([string]$file) {
  $sha = [Security.Cryptography.SHA256]::Create()
  $hash = $sha.ComputeHash([IO.File]::ReadAllBytes((Join-Path $site $file)))
  -join ($hash[0..3] | ForEach-Object { $_.ToString("x2") })
}
$assets = @{ '"/style.css"' = "`"/style.css?v=$(Get-Version 'style.css')`""; '"/main.js"' = "`"/main.js?v=$(Get-Version 'main.js')`"" }

function Save-Page([string]$html, [string]$path) {
  $html = $html.Replace("{{LOCKUP}}", $lockup)
  foreach ($a in $assets.GetEnumerator()) { $html = $html.Replace($a.Key, $a.Value) }
  $left = [regex]::Match($html, '\{\{\w+\}\}')
  if ($left.Success) { throw "$path still has $($left.Value)" }
  New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
  [IO.File]::WriteAllText($path, $html, $utf8)
}

$index = [IO.File]::ReadAllText((Join-Path $site "index.html"), $utf8)
$enDescription = [regex]::Match($index, '<meta name="description" content="([^"]+)"').Groups[1].Value
$en = $index.Replace("{{LANG}}", "en").Replace("{{CANONICAL}}", "https://getyata.app/").Replace("{{CUR_EN}}", ' aria-current="page"').Replace("{{CUR_PT}}", "")
$en = $en.Replace("{{OG_LOCALE}}", "en_US").Replace("{{OG_LOCALE_ALT}}", "pt_BR").Replace("{{JSONLD}}", (Get-JsonLd "en" "https://getyata.app/" $enDescription))
Save-Page (Add-FaqJsonLd $en) (Join-Path $Out "index.html")
$ptPage = $index.Replace("{{LANG}}", "pt-BR").Replace("{{CANONICAL}}", "https://getyata.app/pt/").Replace("{{CUR_EN}}", "").Replace("{{CUR_PT}}", ' aria-current="page"')
$ptPage = $ptPage.Replace("{{OG_LOCALE}}", "pt_BR").Replace("{{OG_LOCALE_ALT}}", "en_US").Replace("{{JSONLD}}", (Get-JsonLd "pt-BR" "https://getyata.app/pt/" (Get-Pt "meta.description")))
Save-Page (Add-FaqJsonLd (Convert-ToPortuguese $ptPage)) (Join-Path $Out "pt/index.html")

foreach ($page in "privacy.html", "pt/privacy.html", "code-signing.html", "pt/code-signing.html", "404.html") {
  Save-Page ([IO.File]::ReadAllText((Join-Path $site $page), $utf8)) (Join-Path $Out $page)
}
"ok: $Out"
