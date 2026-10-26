# Measures Yata in fixed states (Yata.exe --bench): CPU, wake-ups (context switches), memory and
# time to show the notch. Each run uses a throwaway profile (YATA_DATA_DIR) with the companion,
# reminders and notices off, so the user's data, media sessions and network are never touched and
# what the user does meanwhile doesn't change the numbers. A running Yata is closed and reopened.
#
# Usage: ./tools/measure.ps1 [-States parado,musica,aberto,foco] [-Rounds 3] [-Seconds 20]
#                            [-Exe path\Yata.exe] [-OutFile medicao.csv] [-Phases]
#   parado  just the clock            musica  a track with synced lyrics, notch closed
#   aberto  the same, notch open      foco    Pomodoro running
param(
    [string[]]$States = @("parado", "musica", "aberto", "foco"),
    [int]$Rounds = 3,
    [int]$Seconds = 20,
    [int]$Warmup = 10,
    [string]$Exe,
    [string]$OutFile,
    [switch]$Phases
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
public static class YataProbe {
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int needed);

    public static bool HasVisibleWindow(int pid) {
        bool found = false;
        EnumWindows((hwnd, _) => {
            uint owner; GetWindowThreadProcessId(hwnd, out owner);
            if (owner == pid && IsWindowVisible(hwnd)) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Sum of the context switches of every live thread of the process (SystemProcessInformation, x64 layout).
    public static long ContextSwitches(int pid) {
        int size = 1 << 20;
        while (true) {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try {
                int needed;
                int status = NtQuerySystemInformation(5, buffer, size, out needed);
                if (status == unchecked((int)0xC0000004)) { size = Math.Max(needed, size) * 2; continue; }
                if (status != 0) throw new InvalidOperationException("NtQuerySystemInformation: 0x" + status.ToString("X"));
                long offset = 0;
                while (true) {
                    IntPtr entry = buffer + (int)offset;
                    int next = Marshal.ReadInt32(entry, 0);
                    int threads = Marshal.ReadInt32(entry, 4);
                    long id = Marshal.ReadIntPtr(entry, 0x50).ToInt64();
                    if (id == pid) {
                        long total = 0;
                        for (int i = 0; i < threads; i++)
                            total += (uint)Marshal.ReadInt32(entry, 0x100 + i * 80 + 64);
                        return total;
                    }
                    if (next == 0) return -1;
                    offset += next;
                }
            } finally {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
"@

function Median([double[]]$values) {
    $sorted = $values | Sort-Object
    $n = $sorted.Count
    if ($n % 2) { return $sorted[[math]::Floor($n / 2)] }
    return ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2
}

function Write-Profile([string]$dir) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $off = [ordered]@{
        CompanionEnabled = $false; AwaySummaryEnabled = $false; PauseFocusWhenAway = $false
        HudVolume = $false; HudBrightness = $false; HudPower = $false; HudAudioDevice = $false
        HudCapsLock = $false; HudKeyboardLayout = $false; HudNetwork = $false; HudPrivacy = $false
    }
    @{ Settings = $off; Water = @{ RemindersEnabled = $false } } | ConvertTo-Json |
        Set-Content -Encoding utf8 (Join-Path $dir "data.json")
}

# The bench instance needs the single-instance slot; the user's Yata comes back at the end.
$userYata = Get-Process Yata -ErrorAction SilentlyContinue | Select-Object -First 1
$userYataPath = if ($userYata) { $userYata.Path } else { $null }
if ($userYata) {
    Write-Host "Fechando o Yata aberto ($userYataPath); ele volta no fim."
    Get-Process Yata | Stop-Process -Force
}

$rows = New-Object System.Collections.Generic.List[object]
$phaseRows = New-Object System.Collections.Generic.List[object]
try {
    foreach ($state in $States) {
        for ($round = 1; $round -le $Rounds; $round++) {
            Write-Host ("{0}, rodada {1}/{2}..." -f $state, $round, $Rounds)
            $profileDir = Join-Path $work "profile"
            Write-Profile $profileDir
            $timings = Join-Path $profileDir "timings.txt"

            $env:YATA_DATA_DIR = $profileDir
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $process = Start-Process $Exe -ArgumentList "--bench", $state, "--timings", "`"$timings`"" -PassThru
            Remove-Item Env:YATA_DATA_DIR
            while (-not [YataProbe]::HasVisibleWindow($process.Id) -and $clock.ElapsedMilliseconds -lt 30000) {
                Start-Sleep -Milliseconds 5
            }
            $visibleMs = $clock.ElapsedMilliseconds

            Start-Sleep -Seconds $Warmup
            $process.Refresh()
            $cpu0 = $process.TotalProcessorTime.TotalMilliseconds
            $switches0 = [YataProbe]::ContextSwitches($process.Id)
            Start-Sleep -Seconds $Seconds
            $process.Refresh()
            $cpu1 = $process.TotalProcessorTime.TotalMilliseconds
            $switches1 = [YataProbe]::ContextSwitches($process.Id)
            $privateMb = $process.PrivateMemorySize64 / 1MB
            $process | Stop-Process -Force
            $process.WaitForExit()

            $marks = @{}
            if (Test-Path $timings) {
                foreach ($line in Get-Content $timings) {
                    $name, $ms = $line -split ";"
                    $marks[$name] = [double]$ms
                    $phaseRows.Add([pscustomobject]@{ Estado = $state; Rodada = $round; Fase = $name; Ms = [double]$ms })
                }
            }
            $rows.Add([pscustomobject]@{
                Estado         = $state
                Rodada         = $round
                VisivelMs      = $visibleMs
                PrimeiroQuadro = if ($marks.ContainsKey("first-frame")) { $marks["first-frame"] } else { [double]::NaN }
                CpuMsPorS      = ($cpu1 - $cpu0) / $Seconds
                # Threads that ended during the window take their count with them; close enough for a trend.
                DespertaresPorS = [math]::Max(0, $switches1 - $switches0) / $Seconds
                PrivadaMB      = $privateMb
            })
        }
    }
}
finally {
    if ($env:YATA_DATA_DIR) { Remove-Item Env:YATA_DATA_DIR }
    if ($userYataPath) {
        Start-Process $userYataPath
        Write-Host "Yata do usuário reaberto."
    }
}

function Range([object[]]$group, [string]$field, [string]$format) {
    $values = [double[]]($group | ForEach-Object { $_.$field })
    $min = ($values | Measure-Object -Minimum).Minimum
    $max = ($values | Measure-Object -Maximum).Maximum
    "{0} ({1}-{2})" -f (Median $values).ToString($format), $min.ToString($format), $max.ToString($format)
}

Write-Host ""
Write-Host ("Mediana (mín-máx) de {0} rodada(s) de {1} s por estado, {2:yyyy-MM-dd HH:mm}" -f $Rounds, $Seconds, (Get-Date))
$rows | Group-Object Estado | ForEach-Object {
    [pscustomobject]@{
        Estado           = $_.Name
        "Visível (ms)"   = Range $_.Group VisivelMs "0"
        "1º quadro (ms)" = Range $_.Group PrimeiroQuadro "0"
        "CPU (ms/s)"     = Range $_.Group CpuMsPorS "0.0"
        "Despertares/s"  = Range $_.Group DespertaresPorS "0"
        "Privada (MB)"   = Range $_.Group PrivadaMB "0.0"
    }
} | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

if ($Phases -and $phaseRows.Count) {
    Write-Host "Fases da abertura (ms desde o início do processo, mediana de todas as rodadas):"
    $phaseRows | Group-Object Fase | ForEach-Object {
        [pscustomobject]@{ Fase = $_.Name; Ms = Median ([double[]]($_.Group | ForEach-Object Ms)) }
    } | Sort-Object Ms | Format-Table -AutoSize | Out-String | Write-Host
}

if ($OutFile) {
    $rows | Export-Csv -NoTypeInformation -Encoding utf8 $OutFile
    Write-Host "Rodadas gravadas em $OutFile"
}
