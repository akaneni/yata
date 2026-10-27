using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>A WSL distro as Settings → AI agents shows it.</summary>
/// <param name="Name">The distro's name.</param>
/// <param name="HasClaude">Claude Code has been used there (~/.claude exists).</param>
/// <param name="CanRunCli">yata-cli.exe starts from inside it (some distros are cut off from Windows).</param>
/// <param name="Status">Yata's hooks there: on, off, or brought by the plugin.</param>
internal sealed record WslDistro(string Name, bool HasClaude, bool CanRunCli, AgentStatus Status);

/// <summary>
/// Claude Code inside WSL: its hooks live in the distro's own ~/.claude and run on Linux. Linux can
/// still start yata-cli.exe (it runs on Windows, as this user, so it reaches the notch), so a distro
/// the user picks gets the same hooks pointing at the .exe through /mnt/c, plus a "yata-cli" link in
/// ~/.local/bin for the skill. One distro at a time, only the ones picked, and only if the .exe
/// actually runs from there. The files are edited from Windows through \\wsl.localhost.
/// </summary>
internal static class WslAgents
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    // $1: yata-cli.exe's Windows path; $2: probe, on or off. Prints the home folder, the .exe's Linux
    // path, and whether the .exe runs. The link is only made where the .exe runs, and only removed if
    // it's Yata's own.
    private const string Script = """
        exe=$(wslpath -u "$1" 2>/dev/null) || exit 3
        runs=no
        "$exe" --help >/dev/null 2>&1 && runs=yes
        link="$HOME/.local/bin/yata-cli"
        if [ "$2" = on ] && [ "$runs" = yes ]; then mkdir -p "$HOME/.local/bin" && ln -sfn "$exe" "$link"
        elif [ "$2" = off ] && [ "$(readlink "$link")" = "$exe" ]; then rm -f "$link"; fi
        printf '%s\n%s\n%s\n' "$HOME" "$exe" "$runs"
        """;

    /// <summary>The installed distros, or none if WSL isn't there.</summary>
    public static async Task<List<string>> DistrosAsync()
    {
        var (code, output) = await RunAsync(Encoding.Unicode, "-l", "-q");
        if (code != 0)
            return new List<string>();
        return output.Split('\n').Select(l => l.Trim().Trim('\0')).Where(l => l.Length > 0).ToList();
    }

    /// <summary>Looks into each distro (starting it if needed) without changing anything.</summary>
    public static async Task<List<WslDistro>> ScanAsync(string cli, string skill)
    {
        var found = new List<WslDistro>();
        foreach (string distro in await DistrosAsync())
        {
            if (await RunScriptAsync(distro, cli, "probe") is not { } inside)
                continue;
            var status = inside.Home != null ? new AgentSetup(inside.Home, inside.Exe, skill).Status("claude") : AgentStatus.NotFound;
            found.Add(new WslDistro(distro, status != AgentStatus.NotFound, inside.Runs, status));
        }
        return found;
    }

    /// <summary>
    /// Turns Claude Code's hooks on or off in one distro. Returns false (changing nothing) where the
    /// .exe can't run, Claude Code isn't there, or the plugin already brings the hooks.
    /// </summary>
    public static async Task<bool> SetAsync(string distro, bool on, string cli, string skill)
    {
        // Looked at first, so a distro that can't run the .exe is never written to.
        if (await RunScriptAsync(distro, cli, "probe") is not { Home: string home, Runs: true } probe)
            return false;
        var setup = new AgentSetup(home, probe.Exe, skill);
        if (setup.Status("claude") is AgentStatus.NotFound or AgentStatus.OnByPlugin)
            return false;
        await RunScriptAsync(distro, cli, on ? "on" : "off");
        setup.Set("claude", on);
        return true;
    }

    private sealed record Inside(string? Home, string Exe, bool Runs);

    private static async Task<Inside?> RunScriptAsync(string distro, string cli, string action)
    {
        // Checked out on Windows, the source may have CRLF line ends, which sh reads as part of the commands.
        string script = Script.Replace("\r", "");
        var (code, output) = await RunAsync(Encoding.UTF8, "-d", distro, "--cd", "~", "-e", "sh", "-c", script, "sh", cli, action);
        var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (code != 0 || lines.Count < 3 || !lines[0].StartsWith('/'))
        {
            Log.Warn("agents", $"WSL: não foi possível olhar a distro (saída {code})");
            return null;
        }
        return new Inside(WindowsPath(distro, lines[0]), lines[1], lines[2] == "yes");
    }

    /// <summary>The distro's folder as Windows sees it, through \\wsl.localhost (or \\wsl$ on older Windows).</summary>
    private static string? WindowsPath(string distro, string linux)
    {
        string relative = linux.TrimStart('/').Replace('/', '\\');
        foreach (string root in new[] { @"\\wsl.localhost\", @"\\wsl$\" })
        {
            string path = Path.Combine(root + distro, relative);
            if (Directory.Exists(path))
                return path;
        }
        return null;
    }

    private static async Task<(int Code, string Output)> RunAsync(Encoding encoding, params string[] args)
    {
        var start = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
        };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process == null)
                return (-1, "");
            var read = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExitAsync();
            if (await Task.WhenAny(exited, Task.Delay(Limit)) != exited)
            {
                process.Kill(entireProcessTree: true);
                return (-1, "");
            }
            return (process.ExitCode, await read);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, ""); // No WSL on this PC.
        }
    }
}
