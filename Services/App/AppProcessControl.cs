using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTaskbar.Services;

/// <summary>What happened when the dock tried to end an app.</summary>
public sealed record TerminateResult(int Ended, int Elevated, int Services, int Protected, int StillRunning)
{
    /// <summary>The single status message to show (null = everything ended).</summary>
    public string? MessageKey => this switch
    {
        { Protected: > 0, Ended: 0 } => "ErrorAppProtected",
        { Services: > 0, Ended: 0, Elevated: 0 } => "ErrorAppIsService",
        { Elevated: > 0 } => "ErrorAppElevated",
        { StillRunning: > 0 } => "AppStillRunning",
        { Ended: 0 } => "ErrorAppNoProcesses",
        _ => null
    };
}

/// <summary>
/// Closes or ends every process of an app (all processes started from the same executable, each with its child tree).
/// Background/tray apps often have no visible main window and helper processes, and elevated ones refuse access:
/// the old path matched processes through MainModule, which throws for elevated processes, so those were silently
/// skipped and "End task" appeared to do nothing.
/// </summary>
public static class AppProcessControl
{
    private const uint WmClose = 0x0010;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // Never offered for ending: Windows would log off, crash or lose the shell.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "csrss", "winlogon", "wininit", "services", "smss", "lsass", "svchost", "dwm", "sihost",
        "fontdrvhost", "ctfmon", "System", "Registry", "MsMpEng", "SecurityHealthService", "audiodg", "OpenTaskbar"
    };

    public static bool IsProtected(string executablePath) =>
        ProtectedNames.Contains(Path.GetFileNameWithoutExtension(executablePath));

    /// <summary>Full image path even for elevated processes (PROCESS_QUERY_LIMITED_INFORMATION).</summary>
    public static string? GetImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == 0) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static List<Process> FindProcesses(string executablePath)
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath)))
        {
            if (process.Id != Environment.ProcessId
                && string.Equals(GetImagePath(process.Id), executablePath, StringComparison.OrdinalIgnoreCase))
                result.Add(process);
            else
                process.Dispose();
        }

        return result;
    }

    /// <summary>
    /// Polite close: WM_CLOSE to every visible top-level window of the app; tray apps without visible windows get it
    /// on their titled hidden windows. Returns how many windows were asked to close.
    /// </summary>
    public static int CloseWindows(string executablePath)
    {
        if (IsProtected(executablePath)) return 0;
        var processIds = FindProcesses(executablePath).Select(process =>
        {
            using (process) return (uint)process.Id;
        }).ToHashSet();
        if (processIds.Count == 0) return 0;

        var visible = new List<nint>();
        var titledHidden = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (!processIds.Contains(processId) || GetWindow(window, 4) != 0) return true; // skip owned windows
            if (IsWindowVisible(window)) visible.Add(window);
            else if (GetWindowTextLength(window) > 0) titledHidden.Add(window);
            return true;
        }, 0);

        var targets = visible.Count > 0 ? visible : titledHidden;
        return targets.Count(window => PostMessage(window, WmClose, 0, 0));
    }

    /// <summary>Kills each process tree of the app, waits briefly and reports what is still running and why.</summary>
    public static TerminateResult Terminate(string executablePath)
    {
        if (IsProtected(executablePath)) return new TerminateResult(0, 0, 0, 1, 0);
        int ended = 0, elevated = 0, services = 0;
        var processes = FindProcesses(executablePath);
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == 0)
                    {
                        // Session 0 = a Windows service; services are managed in services.msc, not killed from the dock.
                        services++;
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    if (process.WaitForExit(2000)) ended++;
                }
                catch (Win32Exception)
                {
                    elevated++; // access denied: runs as administrator (or is protected)
                }
                catch (InvalidOperationException)
                {
                    ended++; // already exited
                }
            }
        }

        var stillRunning = FindProcesses(executablePath);
        var remaining = stillRunning.Count;
        foreach (var process in stillRunning) process.Dispose();
        return new TerminateResult(ended, elevated, services, 0, Math.Max(0, remaining - elevated - services));
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
