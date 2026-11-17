using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenTaskbar.Services;

public enum LauncherKind { App, Setting, Recent, Calculator, FileSearch, WebSearch }

public sealed record LauncherEntry(string Name, string Target, LauncherKind Kind, string? Detail = null);

/// <summary>
/// Search sources for the launcher: every app in Shell's Applications folder (desktop and Store apps alike), Windows
/// settings pages, recent files, an arithmetic evaluator, and hand-offs to Windows file search and the web.
/// </summary>
public sealed class LauncherCatalog
{
    private static readonly (string Turkish, string English, string Uri)[] SettingsPages =
    [
        ("Ekran", "Display", "ms-settings:display"),
        ("Ses", "Sound", "ms-settings:sound"),
        ("Bildirimler", "Notifications", "ms-settings:notifications"),
        ("Güç ve pil", "Power & battery", "ms-settings:powersleep"),
        ("Depolama", "Storage", "ms-settings:storagesense"),
        ("Bluetooth ve cihazlar", "Bluetooth & devices", "ms-settings:bluetooth"),
        ("Yazıcılar ve tarayıcılar", "Printers & scanners", "ms-settings:printers"),
        ("Fare", "Mouse", "ms-settings:mousetouchpad"),
        ("Wi-Fi", "Wi-Fi", "ms-settings:network-wifi"),
        ("Ağ ve internet", "Network & internet", "ms-settings:network-status"),
        ("VPN", "VPN", "ms-settings:network-vpn"),
        ("Kişiselleştirme", "Personalization", "ms-settings:personalization"),
        ("Arka plan", "Background", "ms-settings:personalization-background"),
        ("Renkler", "Colors", "ms-settings:colors"),
        ("Uygulamalar", "Apps", "ms-settings:appsfeatures"),
        ("Varsayılan uygulamalar", "Default apps", "ms-settings:defaultapps"),
        ("Başlangıç uygulamaları", "Startup apps", "ms-settings:startupapps"),
        ("Hesaplar", "Accounts", "ms-settings:yourinfo"),
        ("Tarih ve saat", "Date & time", "ms-settings:dateandtime"),
        ("Dil ve bölge", "Language & region", "ms-settings:regionlanguage"),
        ("Oyun", "Gaming", "ms-settings:gaming-gamebar"),
        ("Erişilebilirlik", "Accessibility", "ms-settings:easeofaccess"),
        ("Gizlilik ve güvenlik", "Privacy & security", "ms-settings:privacy"),
        ("Windows Update", "Windows Update", "ms-settings:windowsupdate"),
        ("Windows Güvenliği", "Windows Security", "windowsdefender:"),
        ("Görev çubuğu", "Taskbar", "ms-settings:taskbar"),
        ("Sistem hakkında", "About", "ms-settings:about"),
        ("Denetim Masası", "Control Panel", "control"),
        ("Görev Yöneticisi", "Task Manager", "taskmgr")
    ];

    private IReadOnlyList<LauncherEntry> _apps = [];
    private DateTime _loadedAt = DateTime.MinValue;
    private Task? _loading;

    public bool IsLoaded => _loadedAt != DateTime.MinValue;

    /// <summary>Reloads the app list in the background when it is older than a few minutes.</summary>
    public Task RefreshAsync()
    {
        if (_loading is { IsCompleted: false }) return _loading;
        if (DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(5)) return Task.CompletedTask;
        var completion = new TaskCompletionSource();
        // Shell.Application is an apartment-threaded COM object.
        var thread = new Thread(() =>
        {
            try
            {
                _apps = LoadApps();
                _loadedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                AppLog.Warning($"Launcher app list unavailable: {ex.Message}");
            }
            finally
            {
                completion.SetResult();
            }
        }) { IsBackground = true, Name = "Launcher catalog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _loading = completion.Task;
        return _loading;
    }

    private static List<LauncherEntry> LoadApps()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new COMException("Shell.Application is not registered.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var apps = new List<LauncherEntry>();
        try
        {
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (dynamic item in folder.Items())
            {
                string name = item.Name;
                string path = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                // Uninstallers and help links clutter results.
                if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("Kaldır", StringComparison.OrdinalIgnoreCase)) continue;
                apps.Add(new LauncherEntry(name, "shell:AppsFolder\\" + path, LauncherKind.App));
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return apps.DistinctBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public IReadOnlyList<LauncherEntry> Search(string query, bool english, int limit)
    {
        query = query.Trim();
        var results = new List<(LauncherEntry Entry, int Score)>();
        if (query.Length == 0)
        {
            return RecentFiles(english).Take(limit).ToList();
        }

        if (TryCalculate(query, out var value))
        {
            results.Add((new LauncherEntry(value, value, LauncherKind.Calculator, query + " ="), 1000));
        }

        foreach (var app in _apps)
        {
            var score = Score(app.Name, query);
            if (score > 0) results.Add((app, score + 20));
        }

        foreach (var page in SettingsPages)
        {
            var name = english ? page.English : page.Turkish;
            var score = Math.Max(Score(name, query), Score(english ? page.Turkish : page.English, query));
            if (score > 0) results.Add((new LauncherEntry(name, page.Uri, LauncherKind.Setting), score));
        }

        foreach (var recent in RecentFiles(english))
        {
            var score = Score(recent.Name, query);
            if (score > 0) results.Add((recent, score - 10));
        }

        var ordered = results.OrderByDescending(result => result.Score)
            .ThenBy(result => result.Entry.Name.Length)
            .Select(result => result.Entry)
            .Take(Math.Max(1, limit - 2))
            .ToList();
        ordered.Add(new LauncherEntry(query, "search-ms:query=" + Uri.EscapeDataString(query), LauncherKind.FileSearch));
        ordered.Add(new LauncherEntry(query, "https://www.google.com/search?q=" + Uri.EscapeDataString(query), LauncherKind.WebSearch));
        return ordered;
    }

    private static IEnumerable<LauncherEntry> RecentFiles(bool english)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(folder).EnumerateFiles("*.lnk").OrderByDescending(file => file.LastWriteTimeUtc).Take(40).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return new LauncherEntry(Path.GetFileNameWithoutExtension(file.Name), file.FullName, LauncherKind.Recent,
                file.LastWriteTime.ToString("g", UiText.CultureFor(UiText.Current.Language)));
        }
    }

    /// <summary>Prefix beats word-prefix beats initials beats substring; zero means no match.</summary>
    private static int Score(string name, string query)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        if (compare.IsPrefix(name, query, options)) return 300 - Math.Min(100, name.Length);
        var words = name.Split([' ', '-', '_', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(word => compare.IsPrefix(word, query, options))) return 200 - Math.Min(100, name.Length);
        var initials = new string(words.Select(word => word[0]).ToArray());
        if (query.Length >= 2 && compare.IsPrefix(initials, query, options)) return 150;
        if (compare.IndexOf(name, query, options) >= 0) return 100 - Math.Min(90, name.Length);
        return 0;
    }

    private static bool TryCalculate(string query, out string value)
    {
        value = string.Empty;
        var expression = query.TrimEnd('=').Replace(',', '.').Replace('×', '*').Replace('x', '*').Replace('÷', '/');
        if (!expression.Any(char.IsDigit) || !expression.Any(c => "+-*/%^()".Contains(c))
            || expression.Any(c => !(char.IsDigit(c) || char.IsWhiteSpace(c) || "+-*/%().".Contains(c))))
        {
            return false;
        }

        try
        {
            using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
            var result = Convert.ToDouble(table.Compute(expression, null), CultureInfo.InvariantCulture);
            if (double.IsNaN(result) || double.IsInfinity(result)) return false;
            value = result.ToString("G12", CultureInfo.CurrentCulture);
            return true;
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or InvalidCastException or OverflowException or FormatException or DivideByZeroException)
        {
            return false;
        }
    }

    public static string? Launch(LauncherEntry entry)
    {
        if (entry.Kind == LauncherKind.Calculator)
        {
            try
            {
                System.Windows.Clipboard.SetText(entry.Target);
                return null;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
            {
                // Another app holds the clipboard open.
                AppLog.Warning($"Clipboard unavailable: {ex.Message}");
                return UiText.Get("ErrorClipboardCopy");
            }
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(entry.Target) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            AppLog.Warning($"Launcher could not open {entry.Target}: {ex.Message}");
            return UiText.Get("ErrorOpenItem");
        }
    }
}
