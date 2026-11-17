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

sealed class TestClock : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
}
