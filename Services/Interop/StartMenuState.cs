using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTaskbar.Services;

/// <summary>
/// Tells whether the Windows Start menu is open so the dock's Start tile can act as a toggle.
/// Clicking the (non-activating) dock does not take focus from Start, and SC_TASKLIST only ever opens it,
/// so without this a second click left Start open (or closed and immediately reopened it).
/// </summary>
public static class StartMenuState
{
    // Windows 11 hosts Start in StartMenuExperienceHost and, on newer builds, SearchHost; older builds use SearchApp.
    private static readonly string[] HostProcesses = ["StartMenuExperienceHost", "SearchHost", "SearchApp"];

    public static bool IsStartMenuForeground() => IsShellSurfaceForeground(HostProcesses);

    // Notification center / calendar (Win+N) and quick settings (Win+A) are hosted by ShellExperienceHost.
    public static bool IsNotificationCenterForeground() => IsShellSurfaceForeground(["ShellExperienceHost"]);

    private static bool IsShellSurfaceForeground(IReadOnlyCollection<string> hostProcesses)
    {
        var window = GetForegroundWindow();
        if (window == 0) return false;
        var className = new StringBuilder(64);
        if (GetClassName(window, className, className.Capacity) <= 0 || className.ToString() != "Windows.UI.Core.CoreWindow") return false;
        // Shell panels stay the foreground window after they hide; hidden ones are cloaked by DWM.
        if (DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return hostProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private const int DwmwaCloaked = 14;
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}

/// <summary>
/// Pure toggle decision for a shell surface we cannot observe directly (Start menu): it counts as "open at press"
/// when it is open now or was seen open within <see cref="Grace"/>, which covers Start dismissing itself on the
/// same mouse press just before our handler runs.
/// </summary>
public sealed class ShellSurfaceToggle
{
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(400);
    private DateTime _lastSeenOpenUtc = DateTime.MinValue;

    public void RecordOpen(DateTime utcNow) => _lastSeenOpenUtc = utcNow;

    public bool WasOpen(DateTime utcNow, bool openNow) => openNow || utcNow - _lastSeenOpenUtc <= Grace;

    public void Reset() => _lastSeenOpenUtc = DateTime.MinValue;
}
