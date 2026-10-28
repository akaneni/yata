using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Yata.Services;

/// <summary>
/// The window an activity comes from — the terminal or editor where the agent (or the script) runs —
/// so clicking the activity in the notch takes you there. Yata never decides for the agent: it only
/// brings the window forward, and you answer in the tool itself.
///
/// yata-cli finds it (<see cref="Find"/>) and sends it with the request; Yata checks it's still the
/// same window before bringing it forward (<see cref="Focus"/>). Only the handle and the process id
/// travel, nothing about what the window shows. yata-cli.exe compiles this file.
/// </summary>
public sealed record HostWindow(long Handle, int ProcessId)
{
    // A terminal sits a few processes up: yata-cli ← cmd/sh ← claude ← shell ← terminal.
    private const int MaxDepth = 12;

    /// <summary>
    /// Walks up from this process to the first one with a window of its own: Windows Terminal, Orca,
    /// VS Code... Stops at Explorer, which started it all and isn't where the work is. Then the old
    /// console window, which belongs to conhost.exe rather than to any of them. Null if there's none
    /// (a scheduled task, a service).
    /// </summary>
    public static HostWindow? Find()
    {
        try
        {
            int pid = Environment.ProcessId;
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (!Parent(pid, out int parent) || parent == pid || IsExplorer(parent))
                    break;
                pid = parent;
                if (TopWindow(pid) is nint window)
                    return new HostWindow(window, pid);
            }
            // Windows Terminal's console window is a hidden stand-in, already found above as its parent.
            nint console = GetConsoleWindow();
            if (console != 0 && IsWindowVisible(console) && GetWindowThreadProcessId(console, out uint host) != 0)
                return new HostWindow(console, (int)host);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A process that went away midway: no window, the activity works the same without it.
        }
        return null;
    }

    /// <summary>
    /// Brings the window forward, if it's still the one that was sent: same handle, same process,
    /// still there. Works right after a click on the notch, when Windows lets Yata hand over the focus.
    /// </summary>
    public bool Focus()
    {
        nint window = (nint)Handle;
        if (!IsWindow(window) || GetWindowThreadProcessId(window, out uint owner) == 0 || owner != ProcessId ||
            ProcessId == Environment.ProcessId)
            return false;
        if (IsIconic(window))
            ShowWindow(window, SW_RESTORE);
        return SetForegroundWindow(window);
    }

    /// <summary>
    /// The program the window belongs to, as it calls itself ("Orca", "Windows Terminal", "Visual
    /// Studio Code"): its product name, or its description when the product is Windows itself. Null
    /// for the plain console window, which says nothing about where you are, and when it can't be read.
    /// </summary>
    public string? AppName()
    {
        try
        {
            if (ImagePath(ProcessId) is not string path)
                return null;
            string exe = System.IO.Path.GetFileNameWithoutExtension(path);
            if (exe.Equals("conhost", StringComparison.OrdinalIgnoreCase) || exe.Equals("OpenConsole", StringComparison.OrdinalIgnoreCase))
                return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return Usable(info.ProductName) ?? Usable(info.FileDescription);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // "Microsoft® Windows® Operating System", in any language, names no program.
    private static string? Usable(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Contains("Windows®", StringComparison.Ordinal) ? null : name.Trim();

    private static string? ImagePath(int pid)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return null;
        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>The request's <c>"window"</c> field: <c>{"handle": 123, "pid": 456}</c>.</summary>
    public void WriteTo(System.Collections.Generic.IDictionary<string, object> request) =>
        request["window"] = new System.Collections.Generic.Dictionary<string, object> { ["handle"] = Handle, ["pid"] = ProcessId };

    /// <summary>Reads the <c>"window"</c> field back; null if it's malformed.</summary>
    public static HostWindow? Read(JsonElement window)
    {
        if (window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("handle", out var handle) || handle.ValueKind != JsonValueKind.Number ||
            !handle.TryGetInt64(out long h) || h <= 0 ||
            !window.TryGetProperty("pid", out var pid) || pid.ValueKind != JsonValueKind.Number ||
            !pid.TryGetInt32(out int p) || p <= 0)
            return null;
        return new HostWindow(h, p);
    }

    /// <summary>
    /// The parent process, if it's still the one that started this one: a parent that ended can see
    /// its id reused by a newer, unrelated process.
    /// </summary>
    private static bool Parent(int pid, out int parent)
    {
        parent = 0;
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return false;
        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(process, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
                return false;
            parent = (int)info.InheritedFromUniqueProcessId;
            return parent > 0 && Started(parent) is long born && Started(pid) is long child && born <= child;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static long? Started(int pid)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return null;
        try
        {
            return GetProcessTimes(process, out long created, out _, out _, out _) ? created : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsExplorer(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The process's frontmost main window: visible, on the taskbar's level (not owned by another
    /// window, not a tool window). Windows come in z-order, so with several (two VS Code windows)
    /// it's the one used last.
    /// </summary>
    private static nint? TopWindow(int pid)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            if (GetWindowThreadProcessId(window, out uint owner) == 0 || owner != pid || !IsWindowVisible(window) ||
                GetWindow(window, GW_OWNER) != 0 || (GetWindowLong(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0)
                return true;
            found = window;
            return false;
        }, 0);
        return found == 0 ? null : found;
    }

    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    private delegate bool EnumWindowsProc(nint window, nint param);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(nint process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(nint process, int flags, System.Text.StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint param);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
}
