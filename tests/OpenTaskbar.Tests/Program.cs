using System.Globalization;
using System.IO;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

var checks = 0;
CountdownChecks();
StopwatchChecks();
CatalogChecks();
LocalizationChecks();
TaskManagerTextChecks();
SettingsChecks();
InitialLanguageChecks();
ProcessControlChecks();
SuggestionChecks();
UpdateFileChecks();
PersonalizationChecks();
AiUsageChecks();
checks += RunningAppChecks.Run();
checks += WidgetViewChecks.Run();
Console.WriteLine($"{checks} behavior checks passed.");

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}

void CountdownChecks()
{
    var clock = new TestClock();
    var timer = new CountdownDockWidget(clock);
    timer.Refresh();
    Check(timer.State == CountdownState.NotConfigured && !timer.CanToggle && timer.Value == string.Empty,
        "A timer without a duration must not look configured.");

    var errorChanges = 0;
    timer.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(timer.HasValidationError)) errorChanges++; };
    foreach (var invalid in new[] { "0", "1441", "-1", "abc", "1.5", "99999999999999" })
    {
        timer.MinutesText = invalid;
        Check(timer.HasValidationError && !timer.CanToggle, $"Reject invalid duration: {invalid}");
        timer.Toggle();
        Check(!timer.IsRunning, "An invalid duration must not start through a direct model action.");
    }
    Check(errorChanges > 0, "Invalid input must notify the visible validation state.");
    timer.MinutesText = " ";
    Check(!timer.HasValidationError && !timer.CanToggle, "Clearing the input must remove its validation error.");

    timer.MinutesText = "1";
    Check(timer.State == CountdownState.Ready && timer.Value == "00:01:00", "One minute must prepare a sixty-second timer.");
    timer.Toggle();
    clock.Advance(TimeSpan.FromSeconds(20));
    timer.Refresh();
    Check(timer.IsRunning && timer.Value == "00:00:40" && !timer.CanEditDuration, "Running time must count down.");
    timer.MinutesText = "5";
    Check(timer.MinutesText == "1", "Running duration must not be editable.");

    timer.Toggle();
    clock.Advance(TimeSpan.FromHours(1));
    timer.Refresh();
    Check(timer.State == CountdownState.Paused && timer.Value == "00:00:40", "Paused time must stay fixed.");
    timer.Toggle();
    clock.Advance(TimeSpan.FromSeconds(39.5));
    timer.Refresh();
    Check(timer.IsRunning && timer.Value == "00:00:01", "A fractional final second must remain visible.");
    clock.Advance(TimeSpan.FromSeconds(0.5));
    timer.Refresh();
    Check(timer.State == CountdownState.Finished && !timer.IsRunning && timer.Value == "00:00:00",
        "The timer must finish exactly at zero.");
    Check(timer.StatusText == UiText.Get("CountdownFinished") && timer.CanEditDuration,
        "Completion must have visible text and allow another duration.");
    clock.Advance(TimeSpan.FromHours(10));
    timer.Refresh();
    Check(timer.Value == "00:00:00", "A finished timer must never show negative time.");
    timer.Toggle();
    Check(timer.IsRunning && timer.Value == "00:01:00", "Restart must use the configured duration.");
    timer.Reset();
    Check(timer.State == CountdownState.Ready && !timer.IsRunning && timer.Value == "00:01:00",
        "Reset must stop the timer and restore its duration.");
    timer.MinutesText = "1440";
    Check(timer.Value == "24:00:00", "The upper duration boundary must not wrap at midnight.");
}

void StopwatchChecks()
{
    var clock = new TestClock();
    var stopwatch = new StopwatchDockWidget(clock);
    stopwatch.Refresh();
    Check(!stopwatch.IsRunning && stopwatch.Value == "00:00:00", "The stopwatch must start idle.");
    stopwatch.Toggle();
    clock.Advance(TimeSpan.FromSeconds(65));
    stopwatch.Refresh();
    Check(stopwatch.Value == "00:01:05", "Elapsed time must cross minute boundaries.");
    stopwatch.Toggle();
    clock.Advance(TimeSpan.FromHours(1));
    stopwatch.Refresh();
    Check(!stopwatch.IsRunning && stopwatch.Value == "00:01:05", "Paused stopwatch time must stay fixed.");
    stopwatch.Toggle();
    clock.Advance(TimeSpan.FromSeconds(5));
    stopwatch.Refresh();
    Check(stopwatch.Value == "00:01:10", "Resume must accumulate elapsed time.");
    clock.Advance(TimeSpan.FromHours(26));
    stopwatch.Refresh();
    Check(stopwatch.Value == "26:01:10", "Stopwatch hours must not wrap at midnight.");
    stopwatch.Reset();
    Check(!stopwatch.IsRunning && stopwatch.Value == "00:00:00", "Reset must clear and stop the stopwatch.");
}

void CatalogChecks()
{
    var clock = new TestClock();
    var catalog = new DockWidgetService(clock);
    Check(catalog.CreateWidgets(null).Count == 0, "New settings must not install widgets implicitly.");
    Check(catalog.CreateOptions([], []).All(option => !option.IsInstalled && !option.IsEnabled),
        "The available catalog must be optional.");
    var first = catalog.CreateWidgets([DockWidgetService.StopwatchId, DockWidgetService.CountdownId]);
    Check(first.Count == 2 && first.OfType<ITimedDockWidget>().Count() == 2, "Both time widgets must be available.");
    var stopwatch = first.OfType<StopwatchDockWidget>().Single();
    var timer = first.OfType<CountdownDockWidget>().Single();
    timer.MinutesText = "1";
    stopwatch.Toggle();
    Check(stopwatch.IsRunning && !timer.IsRunning, "Time widgets must have independent state.");
    timer.Toggle();
    catalog.CreateWidgets([]);
    clock.Advance(TimeSpan.FromSeconds(30));
    var second = catalog.CreateWidgets([DockWidgetService.StopwatchId, DockWidgetService.CountdownId]);
    DockWidgetService.Refresh(second, AppLanguage.Turkish);
    Check(ReferenceEquals(stopwatch, second[0]) && ReferenceEquals(timer, second[1]), "Hiding must preserve the session instances.");
    Check(stopwatch.Value == "00:00:30" && timer.Value == "00:00:30", "Hidden widgets must retain elapsed time.");
}

void LocalizationChecks()
{
    var option = new DockWidgetOption("Clock", "Saat", "Yerel saat", true, false, new DockWidget("Clock", "Saat"));
    var changes = new List<string?>();
    option.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
    option.UpdateText("Clock", "Local time");
    Check(option.IsInstalled && !option.IsEnabled, "Localization must preserve installation and visibility.");
    Check(changes.SequenceEqual([nameof(option.Label), nameof(option.Description)]), "Both localized fields must notify bindings.");
    changes.Clear();
    option.UpdateText("Clock", "Local time");
    Check(changes.Count == 0, "Unchanged text must not notify.");
    Check(option.MatchesSearch("TIME", CultureInfo.GetCultureInfo("en-US")), "English search must ignore English casing.");
    Check(!option.MatchesSearch("missing", CultureInfo.GetCultureInfo("en-US")), "An unmatched query must stay empty.");
    option.UpdateText("Pil", "Şarj durumu");
    Check(option.MatchesSearch("PİL", CultureInfo.GetCultureInfo("tr-TR")), "Turkish search must match dotted capital I.");

    UiText.Current.SetLanguage(AppLanguage.English);
    Check(UiText.Current.IsEnglish && UiText.Current.Language == AppLanguage.English, "UiText must reflect English language selection.");
    Check(UiText.LocalizeError("ErrorDockAttach") == "Cannot connect the dock window to Windows.", "Key-based error lookup must resolve to English.");
    Check(UiText.LocalizeError("Dock penceresi Windows'a bağlanamadı.") == "Cannot connect the dock window to Windows.", "Legacy Turkish error string lookup must resolve to English.");

    UiText.Current.SetLanguage(AppLanguage.Turkish);
    Check(!UiText.Current.IsEnglish && UiText.Current.Language == AppLanguage.Turkish, "UiText must reflect Turkish language selection.");
    Check(UiText.LocalizeError("ErrorDockAttach") == "Dock penceresi Windows'a bağlanamadı.", "Key-based error lookup must resolve to Turkish.");
}

void TaskManagerTextChecks()
{
    try
    {
        UiText.Current.SetLanguage(AppLanguage.Turkish);
        Check(UiText.Get("TaskManager") == "Görev Yöneticisi", "The Task Manager menu item must have a Turkish label.");
        Check(UiText.Get("ErrorTaskManagerOpen") == "Görev Yöneticisi açılamadı.", "The Task Manager failure must have Turkish text.");
        UiText.Current.SetLanguage(AppLanguage.English);
        Check(UiText.Get("TaskManager") == "Task Manager", "The Task Manager menu item must have an English label.");
        Check(UiText.LocalizeError("ErrorTaskManagerOpen") == "Cannot open Task Manager.",
            "Key-based Task Manager failure lookup must resolve to English.");
        Check(UiText.LocalizeError("Görev Yöneticisi açılamadı.") == "Cannot open Task Manager.",
            "A Task Manager failure shown in Turkish must be translated after switching to English.");
    }
    finally
    {
        UiText.Current.SetLanguage(AppLanguage.Turkish);
    }
}

void SettingsChecks()
{
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    try
    {
        var store = new DockSettingsStore(path);
        File.WriteAllText(path, "{\"StyleVersion\":1,\"Widgets\":[\"Clock\",\"Date\"]}");
        var legacy = store.Load();
        Check(legacy.Error is null && legacy.Settings.InstalledWidgets?.SequenceEqual(["Clock", "Date"]) == true,
            "Legacy selections must migrate before validation.");
        File.WriteAllText(path, "{\"StyleVersion\":1,\"Widgets\":[\"Clock\",\"Memory\"],\"InstalledWidgets\":[\"Clock\",\"Memory\",\"Storage\"]}");
        var removedMemory = store.Load();
        Check(removedMemory.Error is null && removedMemory.Settings.Widgets?.SequenceEqual(["Clock"]) == true
            && removedMemory.Settings.InstalledWidgets?.SequenceEqual(["Clock", "Storage"]) == true,
            "The removed memory widget must be dropped from stored settings without resetting them.");
        Check(store.Save(new DockSettings(Widgets: ["Countdown"], InstalledWidgets: [])) is not null,
            "Enabled widgets must be installed.");
        Check(store.Save(new DockSettings(Widgets: ["Unknown"], InstalledWidgets: ["Unknown"])) is not null,
            "Unknown widget identifiers must be rejected.");
        Check(store.Save(new DockSettings(InstalledWidgets: ["Clock", "Clock"])) is not null,
            "Duplicate installations must be rejected.");
        Check(store.Save(new DockSettings(Widgets: [], InstalledWidgets: ["Countdown"])) is null,
            "An installed widget may remain hidden.");
        var hidden = store.Load();
        Check(hidden.Error is null && hidden.Settings.Widgets?.Count == 0
            && hidden.Settings.InstalledWidgets?.Contains("Countdown") == true, "Hidden installation must survive reload.");
        Check(store.Save(new DockSettings(DockScale: 1.25)) is null && store.Load().Settings.DockScale == 1.25,
            "A valid dock scale must persist and reload.");
        Check(store.Save(new DockSettings(DockScale: 0.5)) is not null && store.Save(new DockSettings(DockScale: 2.0)) is not null,
            "Out-of-range dock scale values must be rejected.");
        File.WriteAllText(path, "{\"Layout\":\"Floating\",\"Size\":\"Compact\",\"MagnificationEnabled\":true,\"Widgets\":[\"Media\"],\"InstalledWidgets\":[\"Media\",\"Focus\"],\"PinOrder\":[\"C:\\\\a.exe\"]}");
        var preTiles = store.Load();
        Check(preTiles.Error is null && preTiles.Settings is { Layout: DockLayout.FullWidth, Size: DockSize.Medium, MagnificationEnabled: false, IconTiles: true, StyleVersion: DockSettings.CurrentStyleVersion }
            && preTiles.Settings.Widgets?.SequenceEqual(["Media", "Calendar", "Tray", "Clock"]) == true
            && preTiles.Settings.InstalledWidgets?.SequenceEqual(["Media", "Focus", "Calendar", "Tray", "Clock"]) == true
            && preTiles.Settings.PinOrder?.SequenceEqual(["C:\\a.exe"]) == true,
            "Pre-tile settings must migrate once to the tile look while keeping pins and widgets.");
        Check(store.Save(preTiles.Settings with { Layout = DockLayout.Floating }) is null && store.Load().Settings.Layout == DockLayout.Floating,
            "After migration the user's own layout choice must stick.");
        File.Delete(path);
        Check(store.Load().Settings is { Layout: DockLayout.FullWidth, IconTiles: true } fresh
            && fresh.Widgets?.SequenceEqual(DockSettings.DefaultWidgets) == true,
            "A first launch must start with the tile look and the calendar, tray and clock cards.");
        Check(store.Save(new DockSettings(AppGroups: [new DockAppGroup("", ["C:\\a.exe"])])) is not null
            && store.Save(new DockSettings(AppGroups: [new DockAppGroup("Work", ["C:\\a.exe", "C:\\b.exe"])])) is null
            && store.Load().Settings.AppGroups?.Single().Apps.Count == 2,
            "App groups must persist and reject unnamed groups.");
    }
    finally
    {
        File.Delete(path);
        File.Delete(path + ".tmp");
    }
}

void InitialLanguageChecks()
{
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    var turkish = new DockSettingsStore(path, CultureInfo.GetCultureInfo("tr-TR"));
    var english = new DockSettingsStore(path, CultureInfo.GetCultureInfo("en-US"));
    var german = new DockSettingsStore(path, CultureInfo.GetCultureInfo("de-DE"));
    try
    {
        Check(turkish.Load() is { Error: null, Settings.Language: AppLanguage.Turkish }, "First launch must follow Turkish system UI language.");
        Check(english.Load() is { Error: null, Settings.Language: AppLanguage.English }, "First launch must follow English system UI language.");
        Check(german.Load() is { Error: null, Settings.Language: AppLanguage.English }, "Unsupported system languages must use the documented English default.");
        var portuguese = new DockSettingsStore(path, CultureInfo.GetCultureInfo("pt-BR"));
        Check(portuguese.Load() is { Error: null, Settings.Language: AppLanguage.Portuguese }, "First launch must follow a Portuguese system UI language.");
        Check(new DockSettingsStore(path, CultureInfo.GetCultureInfo("pt-PT")).Load() is { Settings.Language: AppLanguage.Portuguese },
            "Any Portuguese Windows must start in Portuguese.");
        Check(UiText.MissingPortugueseKeys().Count == 0, "Every UI text needs pt-BR: " + string.Join(", ", UiText.MissingPortugueseKeys()));
        Check(UiText.StalePortugueseKeys().Count == 0, "pt-BR has keys missing from the main table: " + string.Join(", ", UiText.StalePortugueseKeys()));
        Check(UiText.MissingPortugueseErrors().Count == 0, "Every error message needs pt-BR: " + string.Join(" | ", UiText.MissingPortugueseErrors()));
        var previousLanguage = UiText.Current.Language;
        UiText.Current.SetLanguage(AppLanguage.Portuguese);
        Check(UiText.Get("Settings") == "Configurações" && UiText.Get("NoEvents") == "Sem eventos"
            && UiText.LocalizeError("Ses düzeyi değiştirilemedi.") == "Não foi possível mudar o volume."
            && UiText.Format("DesktopState", 1, 2) == "Área de trabalho 1 de 2",
            "Portuguese must translate texts, Turkish-source errors and formatted strings.");
        Check(UiText.CultureFor(AppLanguage.Portuguese).Name == "pt-BR"
            && DateTime.Parse("2026-11-17").ToString("dddd", UiText.CultureFor(AppLanguage.Portuguese)).ToUpper() == "TERÇA-FEIRA",
            "Portuguese dates must use pt-BR (TERÇA-FEIRA).");
        UiText.Current.SetLanguage(previousLanguage);
        File.WriteAllText(path, "{}");
        Check(turkish.Load() is { Error: null, Settings.Language: AppLanguage.Turkish }, "A legacy file without a language must follow Turkish system UI language.");
        Check(english.Load() is { Error: null, Settings.Language: AppLanguage.English }, "A legacy file without a language must follow English system UI language.");
        Check(english.Save(new DockSettings(Language: AppLanguage.Turkish)) is null, "An explicit Turkish preference must save.");
        Check(english.Load() is { Error: null, Settings.Language: AppLanguage.Turkish }, "An explicit Turkish preference must survive an English system language.");
        Check(turkish.Save(new DockSettings(Language: AppLanguage.English)) is null, "An explicit English preference must save.");
        Check(turkish.Load() is { Error: null, Settings.Language: AppLanguage.English }, "An explicit English preference must survive a Turkish system language.");
        File.WriteAllText(path, "{\"Language\":\"Unknown\"}");
        Check(english.Load() is { Error: not null, Settings.Language: AppLanguage.English }, "An invalid stored language must report an error in system-default settings.");
        File.WriteAllText(path, "{\"Language\":42}");
        Check(turkish.Load() is { Error: not null, Settings.Language: AppLanguage.Turkish }, "An undefined language value must report an error.");
        File.WriteAllText(path, "null");
        Check(english.Load() is { Error: not null }, "Null settings must remain an explicit error.");
    }
    finally
    {
        File.Delete(path);
        File.Delete(path + ".tmp");
    }
}

void ProcessControlChecks()
{
    // Never end the shell or Windows core processes, nor the dock itself.
    Check(AppProcessControl.IsProtected(@"C:\Windows\explorer.exe") && AppProcessControl.IsProtected(@"C:\Windows\System32\svchost.exe")
        && AppProcessControl.IsProtected(@"F:\x\OpenTaskbar.exe") && !AppProcessControl.IsProtected(@"C:\Program Files\Discord\Discord.exe"),
        "Critical Windows processes and the dock itself must be protected from End task.");
    Check(AppProcessControl.Terminate(@"C:\Windows\explorer.exe").MessageKey == "ErrorAppProtected", "Ending explorer must be refused with a message.");
    Check(new TerminateResult(2, 0, 0, 0, 0).MessageKey is null
        && new TerminateResult(1, 1, 0, 0, 0).MessageKey == "ErrorAppElevated"
        && new TerminateResult(0, 0, 1, 0, 0).MessageKey == "ErrorAppIsService"
        && new TerminateResult(1, 0, 0, 0, 1).MessageKey == "AppStillRunning"
        && new TerminateResult(0, 0, 0, 0, 0).MessageKey == "ErrorAppNoProcesses",
        "End task outcomes must map to one clear message each.");
    using (var self = System.Diagnostics.Process.GetCurrentProcess())
        Check(string.Equals(AppProcessControl.GetImagePath(self.Id), self.MainModule?.FileName, StringComparison.OrdinalIgnoreCase),
            "Process paths must resolve without MainModule (works for elevated processes too).");

    // End task kills every process of the app: a uniquely named copy of ping.exe stands in for a background app.
    var directory = Path.Combine(Path.GetTempPath(), "OpenTaskbarProcessChecks");
    Directory.CreateDirectory(directory);
    var executable = Path.Combine(directory, $"ot-bg-{Guid.NewGuid():N}.exe");
    File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), executable);
    var started = Enumerable.Range(0, 2).Select(_ => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, "-n 60 127.0.0.1")
    {
        CreateNoWindow = true, UseShellExecute = false
    })!).ToList();
    try
    {
        Check(AppProcessControl.FindProcesses(executable) is { Count: 2 } found && found.All(process => { process.Dispose(); return true; }),
            "All processes started from the app's executable must be found.");
        var result = AppProcessControl.Terminate(executable);
        Check(result is { Ended: 2, StillRunning: 0 } && result.MessageKey is null && started.All(process => process.HasExited),
            "End task must end every process of the app and confirm nothing is left running.");
    }
    finally
    {
        foreach (var process in started)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
            process.Dispose();
        }

        File.Delete(executable);
    }
}

void SuggestionChecks()
{
    var path = Path.Combine(Path.GetTempPath(), $"sugestoes-{Guid.NewGuid():N}.md");
    var store = new SuggestionStore(path);
    var at = new DateTime(2026, 11, 17, 2, 10, 0);
    try
    {
        Check(store.Load().Count == 0 && store.ReadAll() == string.Empty, "A missing suggestions file must read as empty.");
        Check(store.Add("   \n  ", "Ideia", "0.3.2", at) is not null && !File.Exists(path), "An empty suggestion must be refused without creating the file.");
        Check(store.Add("Barra mais escura", "Ideia", "0.3.2", at) is null, "A suggestion must save.");
        Check(File.ReadAllText(path) == "# Sugestões\r\n\r\n- [ ] 2026-11-17 02:10 · Ideia · v0.3.2 — Barra mais escura\r\n",
            "Suggestions must use the documented Markdown line with date, category and version.");
        var bytes = File.ReadAllBytes(path);
        Check(bytes is [0xEF, 0xBB, 0xBF, ..], "The suggestions file must be UTF-8 (with BOM so Notepad shows accents).");

        Check(store.Add("Relógio some às vezes\r\n\r\nPassos: abrir # menu — fechar 🙂\n  depois reabrir  ", "Bug", "0.3.2", at.AddMinutes(1)) is null,
            "Multi-line text with special characters must save.");
        Check(store.Add("Cor do ponto", "Desconhecida", "0.3.2", at.AddMinutes(2)) is null, "An unknown category must still save.");
        var items = store.Load();
        Check(items.Count == 3 && items[1] is { Done: false, Header: "2026-11-17 02:11 · Bug · v0.3.2" }
            && items[1].Text == "Relógio some às vezes\n.\nPassos: abrir # menu — fechar 🙂\n  depois reabrir"
            && items[2].Header.Contains("· Ideia ·"),
            "Multi-line items must round-trip (blank lines kept as '.', indentation kept) and unknown categories become Ideia.");

        Check(store.Toggle(1) is null && store.Load() is [{ Done: false }, { Done: true }, { Done: false }]
            && File.ReadAllText(path).Contains("- [x] 2026-11-17 02:11 · Bug"), "Marking an item done must write [x] on that item only.");
        Check(store.Toggle(1) is null && !store.Load()[1].Done, "Toggling again must reopen it.");
        Check(store.Delete(1) is null && store.Load() is [{ Text: "Barra mais escura" }, { Text: "Cor do ponto" }]
            && !File.ReadAllText(path).Contains("depois reabrir"), "Deleting a multi-line item must remove all of its lines and nothing else.");
        Check(store.Toggle(7) is null && store.Delete(-1) is null && store.Load().Count == 2, "Out-of-range edits must change nothing.");

        // Edits made by hand in Notepad (extra text, LF endings) must survive.
        File.WriteAllText(path, "# Sugestões\n\nMinhas notas soltas\n- [X] 2026-11-01 10:00 · Visual · v0.3.0 — feito à mão\n");
        Check(store.Load() is [{ Done: true, Text: "feito à mão" }] && store.Toggle(0) is null
            && File.ReadAllText(path).Contains("Minhas notas soltas") && !store.Load()[0].Done,
            "Hand-written notes and capital [X] must be kept and understood.");
    }
    finally
    {
        File.Delete(path);
        File.Delete(path + ".tmp");
    }
}

void UpdateFileChecks()
{
    Check(UpdateChecker.ParseChecksum("9A46E9DB6FBC5837E84C0BCAF4531EC8B430A6C8D2243A3B3219433164BC8BA3  OpenTaskbar.exe\r\n")
          == "9a46e9db6fbc5837e84c0bcaf4531ec8b430a6c8d2243a3b3219433164bc8ba3"
          && UpdateChecker.ParseChecksum("not a hash") is null && UpdateChecker.ParseChecksum("") is null,
        "The .sha256 asset must parse to a lowercase hash or nothing.");
    Check(UpdateChecker.IsNewer(new Version(0, 3, 2, 0), new Version(0, 3, 1)) && !UpdateChecker.IsNewer(new Version(0, 3, 1, 0), new Version(0, 3, 1))
          && !UpdateChecker.IsNewer(new Version(0, 2, 9), new Version(0, 3, 1)), "Only strictly newer versions may install.");
    var release = UpdateChecker.ParseRelease("""
        {"tag_name":"v9.0.0","html_url":"https://example.invalid/r","assets":[
          {"name":"OpenTaskbar.exe.sha256","browser_download_url":"https://example.invalid/OpenTaskbar.exe.sha256","size":82},
          {"name":"OpenTaskbar.exe","browser_download_url":"https://example.invalid/OpenTaskbar.exe","size":71000000}]}
        """, new Version(0, 3, 1));
    Check(release is { DownloadUrl: "https://example.invalid/OpenTaskbar.exe", ChecksumUrl: "https://example.invalid/OpenTaskbar.exe.sha256", Size: 71000000 },
        "The checksum asset must be found and must not be mistaken for the executable.");

    var text = Path.Combine(Path.GetTempPath(), $"not-an-exe-{Guid.NewGuid():N}.exe");
    File.WriteAllText(text, "hello");
    try
    {
        Check(UpdateChecker.ValidateLocalFile(text, new Version(0, 3, 1)).ErrorKey == "UpdateFileInvalid", "A non-executable file must be refused.");
    }
    finally
    {
        File.Delete(text);
    }

    Check(UpdateChecker.ValidateLocalFile(Path.Combine(Environment.SystemDirectory, "notepad.exe"), new Version(0, 3, 1)).ErrorKey is "UpdateFileNotOpenTaskbar" or "UpdateFileInvalid",
        "Another program must never be installed over OpenTaskbar.");
    var app = typeof(UpdateChecker).Assembly.Location;
    Check(UpdateChecker.ValidateLocalFile(app, UpdateChecker.CurrentVersion).ErrorKey == "UpdateFileNotNewer"
          && UpdateChecker.ValidateLocalFile(app, new Version(0, 0, 1)) is { ErrorKey: null, Version: not null },
        "An OpenTaskbar build must install only when it is newer than the running one.");
    Check(UpdateChecker.ReleasesPage == "https://github.com/jvsleiman/barra-tarefas/releases/latest", "Downloads must point at this fork's releases.");
}
void PersonalizationChecks()
{
    var store = new DockSettingsStore(Path.Combine(Path.GetTempPath(), $"p-{Guid.NewGuid():N}.json"));
    Check(store.Save(new DockSettings(AccentColor: "#3B82F6", BarColor: "", TileOpacity: 0.1, CornerRadius: 8,
              QuickSettingsItems: ["Wifi", "Notifications"], StartIcon: StartIconStyle.Text, AiUsageProviders: ["Codex"])) is null,
        "Valid personalization must save.");
    Check(store.Save(new DockSettings(AccentColor: "blue")) is not null && store.Save(new DockSettings(TileColor: "#12345")) is not null
          && store.Save(new DockSettings(CornerRadius: 40)) is not null && store.Save(new DockSettings(TileOpacity: 0.9)) is not null
          && store.Save(new DockSettings(StartIconScale: 3)) is not null && store.Save(new DockSettings(QuickSettingsItems: ["Wifi", "Wifi"])) is not null
          && store.Save(new DockSettings(QuickSettingsItems: ["Radio"])) is not null && store.Save(new DockSettings(AiUsageProviders: ["Gemini"])) is not null,
        "Invalid colors, ranges, quick-settings items and providers must be rejected.");
    Check(new DockSettings() is { QuickSettingsCompact: true, StartIcon: StartIconStyle.GreyLogo, IndicatorStyle: RunningIndicatorStyle.Dot, CornerRadius: 15 }
          && new DockSettings().EffectiveQuickSettingsItems.SequenceEqual(["Wifi", "Bluetooth", "Notifications"]),
        "Defaults must keep the 0.3.x look with a cleaner quick-settings set.");
    Check(OpenTaskbar.Widgets.QuickSettingsWidget.Arrange(["Notifications", "Wifi", "Panel"]).SequenceEqual(["Notifications", "Wifi", "Divider", "Panel"])
          && OpenTaskbar.Widgets.QuickSettingsWidget.Arrange(["Wifi", "Bluetooth"]).SequenceEqual(["Wifi", "Bluetooth"])
          && OpenTaskbar.Widgets.QuickSettingsWidget.Arrange(["Wifi", "Bogus", "Wifi"]).SequenceEqual(["Wifi"]),
        "Quick settings keep the chosen order, drop unknown/duplicate items and divide radios from Windows panels.");
    Check(ThemeManager.ParseColor("#3B82F6") is { R: 0x3B, G: 0x82, B: 0xF6 } && ThemeManager.ParseColor("") is null && ThemeManager.ParseColor("red") is null
          && ThemeManager.TileColor("", 0.055, System.Windows.Media.Colors.White) is { A: 14, R: 255 }
          && ThemeManager.TileColor("#FF0000", 0.2, System.Windows.Media.Colors.White) is { A: 51, R: 255, G: 0 },
        "Colors parse as #RRGGBB and tiles combine the chosen color with their opacity.");
    Check(WirelessDevices.Classify("WH-1000XM4", majorClass: 4) == WirelessKind.Headset
          && WirelessDevices.Classify("Keys", majorClass: 5, minorClass: 0x10) == WirelessKind.Keyboard
          && WirelessDevices.Classify("MX Master 3", majorClass: 5, minorClass: 0x20) == WirelessKind.Mouse
          && WirelessDevices.Classify("Wireless Controller", majorClass: 5, minorClass: 0x08 | 0x02) == WirelessKind.Controller
          && WirelessDevices.Classify("Galaxy S23", majorClass: 2) == WirelessKind.Phone
          && WirelessDevices.Classify("Xbox Wireless Controller") == WirelessKind.Controller
          && WirelessDevices.Classify("Fone JBL") == WirelessKind.Headset && WirelessDevices.Classify("Lâmpada") == WirelessKind.Other,
        "Wireless devices must be recognised by class of device, then by name.");
    var pt = CultureInfo.GetCultureInfo("pt-BR");
    Check(OpenTaskbar.Widgets.NetworkSpeedWidget.FormatRate(850_000, pt) == "850 KB/s" && OpenTaskbar.Widgets.NetworkSpeedWidget.FormatRate(12_400_000, pt) == "12,4 MB/s"
          && OpenTaskbar.Widgets.NetworkSpeedWidget.FormatRate(0, pt) == "0 B/s", "Network speed must format like Task Manager.");
    Check(OpenTaskbar.Widgets.QuickNotesWidget.PreviewOf("\r\n  \r\n  Comprar café  \r\nligar", "Notas") == "Comprar café"
          && OpenTaskbar.Widgets.QuickNotesWidget.PreviewOf("", "Notas") == "Notas", "Notes preview shows the first written line.");
    Check(OpenTaskbar.SettingsWindow.Move(["Wifi", "Bluetooth", "Panel"], "Panel", -1).SequenceEqual(["Wifi", "Panel", "Bluetooth"])
          && OpenTaskbar.SettingsWindow.Move(["Wifi", "Bluetooth"], "Wifi", -1).SequenceEqual(["Wifi", "Bluetooth"])
          && OpenTaskbar.SettingsWindow.Move(["Wifi"], "Missing", 1).SequenceEqual(["Wifi"]), "Quick settings reorder one step and stop at the ends.");
    Check(SystemIconLibrary.Parse("imageres.dll,15") is { Index: 15 } spec && spec.Path.EndsWith(@"System32\imageres.dll", StringComparison.OrdinalIgnoreCase)
          && SystemIconLibrary.Parse("imageres.dll") is null && SystemIconLibrary.Parse("evil.exe,1") is null && SystemIconLibrary.Parse("imageres.dll,-2") is null
          && SystemIconLibrary.Count("imageres.dll") > 50 && SystemIconLibrary.Load("imageres.dll,1") is { PixelWidth: > 0 } && SystemIconLibrary.Load("imageres.dll,999999") is null,
        "Windows icons must load from imageres.dll by index and reject anything else.");
}

void AiUsageChecks()
{
    var now = new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    var reset = now.AddHours(2).AddMinutes(10).ToUnixTimeSeconds();
    var weekly = now.AddDays(3).ToUnixTimeSeconds();
    var codexLine = "{\"timestamp\":\"2026-11-17T14:59:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":42.5,\"window_minutes\":300,\"resets_at\":" + reset + "},\"secondary\":{\"used_percent\":16.0,\"window_minutes\":10080,\"resets_at\":" + weekly + "}}}}";
    var codex = AiUsage.ParseCodexLine(codexLine, now);
    Check(codex is { Session.Percent: 42.5, Weekly.Percent: 16.0 } && codex.Session!.ResetsAt == DateTimeOffset.FromUnixTimeSeconds(reset)
          && AiUsage.FormatRemaining(codex.Session.ResetsAt!.Value, now) == "2h 10m" && AiUsage.FormatRemaining(codex.Weekly!.ResetsAt!.Value, now) == "3d 0h",
        "Codex rate limits must map 5-hour and weekly windows with their reset countdown.");
    var weeklyOnly = "{\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":16.0,\"window_minutes\":10080,\"resets_at\":" + now.AddMinutes(-1).ToUnixTimeSeconds() + "},\"secondary\":null}}}";
    Check(AiUsage.ParseCodexLine(weeklyOnly, now) is { Session: null, Weekly: { Percent: 0, ResetsAt: null } },
        "A weekly-only primary window (as in João's logs) must count as weekly, and a passed reset reads 0%.");
    Check(AiUsage.ParseCodexLine("not json", now) is null && AiUsage.ParseCodexLine("""{"payload":{}}""", now) is null, "Broken Codex lines must be skipped.");

    string Claude(string at, string id, int input, int output) =>
        "{\"timestamp\":\"" + at + "\",\"requestId\":\"r-" + id + "\",\"message\":{\"id\":\"m-" + id + "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";
    var lines = new[]
    {
        Claude("2026-11-10T09:00:00Z", "old", 999, 1),          // outside the 7 days
        Claude("2026-11-16T08:30:00Z", "a", 1000, 500),        // earlier block
        Claude("2026-11-17T13:40:00Z", "b", 2000, 1000),       // current block starts 13:00
        Claude("2026-11-17T13:40:00Z", "b", 2000, 1000),       // streamed duplicate
        Claude("2026-11-17T14:55:00Z", "c", 4000, 2000),
        "garbage line with \"usage\""
    };
    var claude = AiUsage.SumClaude(lines, now);
    Check(claude is { Session: { Tokens: 9000, Percent: 100, Estimated: true }, Weekly.Tokens: 10500 }
          && claude.Session.ResetsAt == new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero),
        "Claude Code usage must sum the current 5-hour block and 7 days, counting streamed messages once.");
    Check(claude.Daily is { Count: 7 } days && days[^1] == 9000 && days.Sum() == 10500, "The 7-day chart must hold one total per day, today last.");
    Check(AiUsage.SumClaude([Claude("2026-11-17T08:00:00Z", "x", 10, 10)], now) is { Session: null, Weekly.Tokens: 20 },
        "An expired 5-hour block must show no session usage.");
    Check(AiUsage.FormatTokens(1_234_567, CultureInfo.GetCultureInfo("pt-BR")) == "1,2M" && AiUsage.FormatTokens(9_000, CultureInfo.InvariantCulture) == "9k",
        "Token counts must be short.");

    // Fixture folders: missing data reads as "Sem dados", files are found recursively.
    var root = Path.Combine(Path.GetTempPath(), $"ai-{Guid.NewGuid():N}");
    try
    {
        Check(!AiUsage.ReadCodex(now, Path.Combine(root, "missing")).HasData && !AiUsage.ReadClaude(now, Path.Combine(root, "missing")).HasData,
            "Missing log folders must give 'no data', not an error.");
        Directory.CreateDirectory(Path.Combine(root, "codex", "2026", "11"));
        File.WriteAllLines(Path.Combine(root, "codex", "2026", "11", "s.jsonl"), ["{\"type\":\"other\"}", codexLine, "{\"type\":\"other\"}"]);
        Check(AiUsage.ReadCodex(now, Path.Combine(root, "codex")) is { Session.Percent: 42.5 }, "The newest Codex rate-limit event must be found in nested folders.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
sealed class TestClock : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
}

