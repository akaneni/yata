# Measures how smooth the notch moves (Yata.exe --bench transicoes --frames): it opens, goes through
# three tabs, closes and shows a notice, over and over, and every frame of the notch's spring loop
# is timed. Each run uses a throwaway profile (YATA_DATA_DIR), like tools/measure.ps1; a running
# Yata is closed and reopened.
#
# Usage: ./tools/measure-frames.ps1 [-Variants padrao,sem-sombra,gpu] [-Rounds 2] [-Skip 2]
#                                   [-Exe path\Yata.exe] [-OutFile quadros.csv]
#   padrao      as shipped (software rendering, drop shadows)
#   sem-sombra  the notch without its drop shadows
#   gpu         hardware rendering instead of software
param(
    [string[]]$Variants = @("padrao", "sem-sombra", "gpu"),
    [int]$Rounds = 2,
    # The first cycles still JIT-compile the code they run; they'd count as jank that isn't there later.
    [int]$Skip = 2,
    [string]$Exe,
    [string]$OutFile
)
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) "yata-measure"
New-Item -ItemType Directory -Force $work | Out-Null

if (-not $Exe) {
    # Same build as the installer (tools/build-installer.ps1).
    $publish = Join-Path $work "app"
    dotnet publish (Join-Path $root "src\Yata\Yata.csproj") -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=none -o $publish --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    $Exe = Join-Path $publish "Yata.exe"
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class YataDisplay {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);

    // Refresh rate of the primary display, where the bench notch sits.
    public static int PrimaryHz() {
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        return EnumDisplaySettings(null, -1, ref mode) ? mode.dmDisplayFrequency : 0;
    }
}
"@

function Percentile([double[]]$values, [double]$p) {
    $sorted = [double[]]($values | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $sorted[[math]::Min($sorted.Count - 1, [math]::Floor($p * $sorted.Count))]
}

function Write-Profile([string]$dir) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $off = [ordered]@{
        HideInFullscreen = $false; LyricsEnabled = $true; ActivitiesEnabled = $false
        CompanionEnabled = $false; AwaySummaryEnabled = $false; PauseFocusWhenAway = $false
        HudVolume = $false; HudBrightness = $false; HudPower = $false; HudAudioDevice = $false
        HudCapsLock = $false; HudKeyboardLayout = $false; HudNetwork = $false; HudPrivacy = $false
    }
    @{ Settings = $off; Water = @{ RemindersEnabled = $false } } | ConvertTo-Json |
        Set-Content -Encoding utf8 (Join-Path $dir "data.json")
}

$hz = [YataDisplay]::PrimaryHz()
$period = if ($hz -gt 0) { 1000.0 / $hz } else { 1000.0 / 60 }

$userYata = Get-Process Yata -ErrorAction SilentlyContinue | Select-Object -First 1
$userYataPath = if ($userYata) { $userYata.Path } else { $null }
if ($userYata) {
    Write-Host "Fechando o Yata aberto ($userYataPath); ele volta no fim."
    Get-Process Yata | Stop-Process -Force
}

$frames = New-Object System.Collections.Generic.List[object]
$memory = @{}
try {
    foreach ($variant in $Variants) {
        for ($round = 1; $round -le $Rounds; $round++) {
            Write-Host ("{0}, rodada {1}/{2}..." -f $variant, $round, $Rounds)
            $profileDir = Join-Path $work "profile"
            Write-Profile $profileDir
            $file = Join-Path $profileDir "frames.txt"

            $arguments = @("--bench", "transicoes", "--frames", "`"$file`"")
            if ($variant -ne "padrao") { $arguments += @("--bench-variant", $variant) }
            $env:YATA_DATA_DIR = $profileDir
            $process = Start-Process $Exe -ArgumentList $arguments -PassThru
            Remove-Item Env:YATA_DATA_DIR

            $clock = [Diagnostics.Stopwatch]::StartNew()
            while ($clock.Elapsed.TotalMinutes -lt 4 -and
                   -not ((Test-Path $file) -and (Select-String -Path $file -Pattern "^#fim" -Quiet))) {
                Start-Sleep -Seconds 1
            }
            $process.Refresh()
            $memory["$variant/$round"] = $process.PrivateMemorySize64 / 1MB
            $process | Stop-Process -Force
            $process.WaitForExit()

            foreach ($line in Get-Content $file) {
                if ($line.StartsWith("#")) { continue }
                $label, $cycle, $frame, $interval, $workMs = $line -split ";"
                if ($label -eq "outro" -or [int]$cycle -le $Skip) { continue }
                $frames.Add([pscustomobject]@{
                    Variante = $variant; Rodada = $round; Passo = $label; Ciclo = [int]$cycle
                    Quadro = [int]$frame; IntervaloMs = [double]$interval; TrabalhoMs = [double]$workMs
                })
            }
        }
    }
}
finally {
    if ($env:YATA_DATA_DIR) { Remove-Item Env:YATA_DATA_DIR }
    if ($userYataPath) {
        Start-Process $userYataPath
        Write-Host "Yata do usuario reaberto."
    }
}

Write-Host ""
Write-Host ("Monitor principal: {0} Hz (um quadro a cada {1:0.0} ms). {2} rodada(s) por variante, {3:yyyy-MM-dd HH:mm}" -f $hz, $period, $Rounds, (Get-Date))
Write-Host ("Engasgo: intervalo acima de 1,5 quadro ({0:0.0} ms). Trabalho: parte da thread da interface (molas, forma, layout)." -f (1.5 * $period))
$frames | Group-Object Variante, Passo | ForEach-Object {
    $first = $_.Group | Where-Object Quadro -eq 0
    $moving = $_.Group | Where-Object Quadro -gt 0
    $intervals = [double[]]($moving | ForEach-Object IntervaloMs)
    $animations = ($_.Group | Group-Object Rodada, Ciclo).Count
    $duration = ($intervals | Measure-Object -Sum).Sum
    [pscustomobject]@{
        Variante        = $_.Group[0].Variante
        Passo           = $_.Group[0].Passo
        Animacoes       = $animations
        "Duracao (ms)"  = [math]::Round($duration / [math]::Max(1, $animations))
        FPS             = [math]::Round($intervals.Count / ($duration / 1000), 1)
        "1o quadro (ms)" = [math]::Round((Percentile ([double[]]($first | ForEach-Object IntervaloMs)) 0.5), 1)
        "p50 (ms)"      = [math]::Round((Percentile $intervals 0.5), 1)
        "p95 (ms)"      = [math]::Round((Percentile $intervals 0.95), 1)
        "max (ms)"      = [math]::Round(($intervals | Measure-Object -Maximum).Maximum, 1)
        "Engasgos %"    = [math]::Round(100.0 * ($intervals | Where-Object { $_ -gt 1.5 * $period }).Count / [math]::Max(1, $intervals.Count), 1)
        "Trab p50 (ms)" = [math]::Round((Percentile ([double[]]($moving | ForEach-Object TrabalhoMs)) 0.5), 2)
        "Trab p95 (ms)" = [math]::Round((Percentile ([double[]]($moving | ForEach-Object TrabalhoMs)) 0.95), 2)
    }
} | Format-Table -AutoSize | Out-String -Width 220 | Write-Host

Write-Host "Memoria privada no fim de cada rodada (MB):"
$memory.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host ("  {0}: {1:0.0}" -f $_.Name, $_.Value) }

if ($OutFile) {
    $frames | Export-Csv -NoTypeInformation -Encoding utf8 $OutFile
    Write-Host "Quadros gravados em $OutFile"
}
