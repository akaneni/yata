# Builds getyata.app into _site/ (deployed to GitHub Pages by .github/workflows/pages.yml):
#   site/index.html is the English page; site/pt.json translates every data-i18n,
#   data-i18n-html, data-i18n-label (aria-label), data-i18n-content (meta content), data-i18n-alt
#   and data-i18n-src (the screenshot in each language) into
#   pt/index.html, and {{JSONLD}} and {{FAQ_JSONLD}} become the schema.org data in each
#   language (the FAQ one is read from the page's <details class="faq-item">). robots.txt,
#   sitemap.xml and llms.txt are copied as they are. {{LOCKUP}} becomes the inline lockup from docs/brand (run make-brand.ps1
#   after redrawing the raven). CHANGELOG.md and CHANGELOG.pt-BR.md become /changelog and
#   /pt/changelog through the site/changelog.html template. Fails on a missing translation, a
#   leftover {{token}} or a version (src/Yata/Yata.csproj) with no changelog entry.
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
  $html = $html.Replace('href="/privacy"', 'href="/pt/privacy"').Replace('href="/code-signing"', 'href="/pt/code-signing"')
  # Every link to the changelog but the language switch's own EN link.
  [regex]::Replace($html, '<a href="/changelog"(?![^>]*hreflang)', '<a href="/pt/changelog"')
}

# The changelog's small Markdown: # and ## headings, - lists (one level of nesting, two spaces),
# **bold**, `code` and [links](url). Everything is escaped first, so raw HTML stays text.
function ConvertFrom-Changelog([string]$markdown) {
  $inline = {
    param($s)
    $s = ConvertTo-Html $s
    $s = [regex]::Replace($s, '`([^`]+)`', '<code>$1</code>')
    $s = [regex]::Replace($s, '\*\*(.+?)\*\*', '<strong>$1</strong>')
    [regex]::Replace($s, '\[([^\]]+)\]\(([^)\s]+)\)', '<a href="$2">$1</a>')
  }
  $out = New-Object System.Collections.Generic.List[string]
  $depth = 0
  foreach ($raw in ($markdown -split "\r?\n") + "") {
    $line = $raw.TrimEnd()
    $item = [regex]::Match($line, '^( *)- (.+)$')
    if ($item.Success) {
      $level = [Math]::Min(2, [int][Math]::Floor($item.Groups[1].Value.Length / 2) + 1)
      if ($level -gt $depth) {
        while ($depth -lt $level) { $out.Add("<ul>"); $depth++ }
      } else {
        $out.Add("</li>")
        while ($depth -gt $level) { $out.Add("</ul></li>"); $depth-- }
      }
      $out.Add("<li>" + (& $inline $item.Groups[2].Value))
      continue
    }
    if ($depth -gt 0) {
      $out.Add("</li>")
      while ($depth -gt 1) { $out.Add("</ul></li>"); $depth-- }
      $out.Add("</ul>")
      $depth = 0
    }
    if ($line -eq "") { continue }
    if ($line -match '^## (.+)$') {
      $id = "v" + ($Matches[1] -replace '\s.*$', '')
      $out.Add("<h2 id=`"$id`">$(& $inline $Matches[1])</h2>")
    } elseif ($line -match '^# (.+)$') {
      $out.Add("<h1>$(& $inline $Matches[1])</h1>")
    } else {
      $out.Add("<p>$(& $inline $line)</p>")
    }
  }
  $out -join "`n"
}

# schema.org data for search engines and AI assistants, in the page's language.
function Get-JsonLd([string]$lang, [string]$url, [string]$description) {
  $app = [ordered]@{
    "@context" = "https://schema.org"
    "@type" = "SoftwareApplication"
    name = "Yata"
    alternateName = @("Yata Notch", "Yata for Windows")
    url = $url
    inLanguage = $lang
    description = $description
    applicationCategory = "UtilitiesApplication"
    operatingSystem = "Windows 10, Windows 11"
    downloadUrl = "https://github.com/akaneni/yata/releases/latest"
    fileSize = "3MB"
    image = "https://getyata.app/og.png"
    screenshot = "https://getyata.app/img/notch-open-$(if ($lang -eq "en") { "en" } else { "pt" }).png"
    license = "https://opensource.org/licenses/MIT"
    isAccessibleForFree = $true
    offers = [ordered]@{ "@type" = "Offer"; price = "0"; priceCurrency = "USD" }
    author = [ordered]@{ "@type" = "Person"; name = "akaneni"; url = "https://github.com/akaneni" }
    sameAs = @("https://github.com/akaneni/yata", "https://alternativeto.net/software/yata-notch/about/")
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

# /changelog and /pt/changelog, from CHANGELOG.md and CHANGELOG.pt-BR.md (written by hand in each
# version's PR). The version being built must have an entry in both.
$appVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $root "src/Yata/Yata.csproj"), $utf8), '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $appVersion) { throw "src/Yata/Yata.csproj has no <Version>" }
$changelog = [IO.File]::ReadAllText((Join-Path $site "changelog.html"), $utf8)
foreach ($lang in "en", "pt") {
  $isEn = $lang -eq "en"
  $file = if ($isEn) { "CHANGELOG.md" } else { "CHANGELOG.pt-BR.md" }
  $markdown = [IO.File]::ReadAllText((Join-Path $root $file), $utf8)
  if (-not [regex]::IsMatch($markdown, "(?m)^## $([regex]::Escape($appVersion))(\s|$)")) { throw "$file has no entry for version $appVersion" }
  $page = $changelog.Replace("{{LANG}}", $(if ($isEn) { "en" } else { "pt-BR" })).Replace("{{HOME}}", $(if ($isEn) { "/" } else { "/pt/" }))
  $page = $page.Replace("{{CANONICAL}}", "https://getyata.app/$(if ($isEn) { '' } else { 'pt/' })changelog")
  $page = $page.Replace("{{OG_LOCALE}}", $(if ($isEn) { "en_US" } else { "pt_BR" })).Replace("{{OG_LOCALE_ALT}}", $(if ($isEn) { "pt_BR" } else { "en_US" }))
  $page = $page.Replace("{{CUR_EN}}", $(if ($isEn) { ' aria-current="page"' } else { "" })).Replace("{{CUR_PT}}", $(if ($isEn) { "" } else { ' aria-current="page"' }))
  if (-not $isEn) { $page = Convert-ToPortuguese $page }
  # After the translation: the entries are already in the page's language.
  $page = $page.Replace("{{CHANGELOG}}", (ConvertFrom-Changelog $markdown))
  Save-Page $page (Join-Path $Out $(if ($isEn) { "changelog.html" } else { "pt/changelog.html" }))
}
"ok: $Out"
