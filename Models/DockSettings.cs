namespace OpenTaskbar.Models;

public enum DockPosition
{
    Left,
    Center,
    Right
}

public enum AppTheme { Dark, Light }
public enum AppLanguage { Turkish, English, Portuguese }
public enum AutoHideMode { Disabled, Smart, Always }
public enum DockLayout { Floating, FullWidth }
public enum DockSize { Compact, Medium, Large }
public enum DockEdge { Bottom, Left, Right }
public enum HotkeyChoice { Disabled, AltSpace, CtrlSpace, CtrlAltSpace, CtrlAltD, WinAltD }
public enum StartButtonAction { WindowsStart, Launcher }
public enum StartButtonPlacement { Start, End, Hidden }
public enum ClockDateStyle { DayAndShortDate, ShortDateOnly, NumericDate, Hidden }

/// <summary>The individual meters of the System meters widget; a cleared flag hides that meter.</summary>
[Flags]
public enum SystemMeters
{
    None = 0,
    Cpu = 1,
    Memory = 2,
    Battery = 4,
    CpuTemperature = 8,
    Gpu = 16,
    GpuTemperature = 32,
    All = Cpu | Memory | Battery | CpuTemperature | Gpu | GpuTemperature
}

/// <summary>A named view of the dock that hides some pinned apps; pins themselves are never removed.</summary>
public sealed record DockProfile(string Name, IReadOnlyList<string> HiddenApps);

/// <summary>Pinned apps shown together in one dock tile (2x2 preview) that opens a flyout.</summary>
public sealed record DockAppGroup(string Name, IReadOnlyList<string> Apps);

// Missing JSON properties fall back to these constructor defaults, so older settings files stay valid.
public sealed record DockSettings(
    DockPosition Position = DockPosition.Center,
    IReadOnlyList<string>? PinOrder = null,
    AppTheme Theme = AppTheme.Dark,
    AppLanguage Language = AppLanguage.English,
    bool AnimationsEnabled = true,
    bool PreviewsEnabled = true,
    bool NotificationBadgesEnabled = true,
    AutoHideMode AutoHide = AutoHideMode.Disabled,
    double SurfaceOpacity = 0.9,
    IReadOnlyList<string>? Widgets = null,
    IReadOnlyList<string>? InstalledWidgets = null,
    DockLayout Layout = DockLayout.FullWidth,
    DockSize Size = DockSize.Medium,
    bool MagnificationEnabled = false,
    bool ShowClock = false,
    bool ShowMedia = false,
    bool ShowSystemStats = false,
    bool ShowFocus = false,
    bool ShowAudio = false,
    bool ShowQuickSettings = false,
    int FocusMinutes = 25,
    int FocusBreakMinutes = 5,
    int FocusSessions = 4,
    bool ShowRecycleBin = true,
    HotkeyChoice LauncherHotkey = HotkeyChoice.AltSpace,
    HotkeyChoice DockHotkey = HotkeyChoice.CtrlAltD,
    StartButtonAction StartAction = StartButtonAction.WindowsStart,
    StartButtonPlacement StartPlacement = StartButtonPlacement.Start,
    bool WheelVolume = true,
    bool CheckUpdates = true,
    bool ShowWeather = false,
    string WeatherCity = "",
    bool ShowVirtualDesktops = false,
    bool CurrentDesktopOnly = false,
    bool ShowTray = false,
    bool ShowOnAllMonitors = true,
    DockEdge Edge = DockEdge.Bottom,
    // Changed from the dock itself, not the Settings edit buffer. Null stacks means "Downloads only".
    IReadOnlyList<string>? StackFolders = null,
    IReadOnlyList<DockProfile>? Profiles = null,
    string? ActiveProfile = null,
    double DockScale = 1.0,
    IReadOnlyList<string>? WidgetOrder = null,
    bool Clock24Hour = true,
    bool ClockShowSeconds = false,
    ClockDateStyle DateStyle = ClockDateStyle.DayAndShortDate,
    SystemMeters StatsMeters = SystemMeters.All,
    // Each dock item sits in its own rounded tile.
    bool IconTiles = true,
    bool ClockShowWeather = true,
    IReadOnlyList<DockAppGroup>? AppGroups = null,
    // Files written before the tile look have no StyleVersion; the store migrates them once.
    int StyleVersion = DockSettings.CurrentStyleVersion)
{
    public const int CurrentStyleVersion = 1;
    public static readonly IReadOnlyList<string> DefaultWidgets = ["Calendar", "Tray", "Clock"];
}
