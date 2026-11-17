using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using OpenTaskbar.Models;
using OpenTaskbar.Services;
using OpenTaskbar.Widgets;

namespace OpenTaskbar;

public partial class SettingsWindow : Window
{
    private readonly ApplicationPreferences _preferences;
    private readonly StartupRegistration _startup = new();
    private readonly ObservableCollection<DockWidgetOption> _widgetOptions = [];
    private readonly DockWidgetService _widgetPreviewService = new(TimeProvider.System);
    private readonly DispatcherTimer _widgetPreviewTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _autoSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _startupEnabled;
    private bool _startupAvailable;
    private DockSettings _applied;
    private bool _closingApplication;
    private bool _initializing = true;
    private bool _sourceReady;
    private HwndSource? _source;

    public SettingsWindow(ApplicationPreferences preferences)
    {
        _preferences = preferences;
        _applied = preferences.Current;
        InitializeComponent();
        foreach (var option in _widgetPreviewService.CreateOptions(_applied.InstalledWidgets, _applied.Widgets))
        {
            _widgetOptions.Add(option);
        }
        AttachWidgetSettingsPanels();
        WidgetOptionsList.ItemsSource = _widgetOptions;
        WeatherCountryBox.ItemsSource = CountryCityService.GetInitialCountries().Select(c => c.Name).ToList();
        _ = InitializeWeatherLocationsAsync(_applied.WeatherCity);
        _preferences.Changed += Preferences_Changed;
        _widgetPreviewTimer.Tick += WidgetPreviewTimer_Tick;
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer.Stop();
            SaveSettings();
        };
        IsVisibleChanged += Settings_IsVisibleChanged;
        Closed += (_, _) =>
        {
            _autoSaveTimer.Stop();
            _widgetPreviewTimer.Stop();
            _widgetPreviewTimer.Tick -= WidgetPreviewTimer_Tick;
            IsVisibleChanged -= Settings_IsVisibleChanged;
            _preferences.Changed -= Preferences_Changed;
            if (_source is { IsDisposed: false }) _source.RemoveHook(WindowProc);
            ThemeManager.SetSurfaceOpacity(_preferences.Current.SurfaceOpacity);
            (Application.Current.MainWindow as MainWindow)?.SetSizePreview(null, null);
        };
        DarkTheme.IsChecked = _applied.Theme == AppTheme.Dark;
        LightTheme.IsChecked = _applied.Theme == AppTheme.Light;
        TurkishLanguage.IsChecked = _applied.Language == AppLanguage.Turkish;
        EnglishLanguage.IsChecked = _applied.Language == AppLanguage.English;
        PortugueseLanguage.IsChecked = _applied.Language == AppLanguage.Portuguese;
        SelectAlignment(_applied);
        AnimationsToggle.IsChecked = _applied.AnimationsEnabled;
        PreviewsToggle.IsChecked = _applied.PreviewsEnabled;
        BadgesToggle.IsChecked = _applied.NotificationBadgesEnabled;
        AutoHideDisabled.IsChecked = _applied.AutoHide == AutoHideMode.Disabled;
        AutoHideSmart.IsChecked = _applied.AutoHide == AutoHideMode.Smart;
        AutoHideAlways.IsChecked = _applied.AutoHide == AutoHideMode.Always;
        OpacitySlider.Value = _applied.SurfaceOpacity * 100;
        UpdateOpacityLabel();
        MagnificationToggle.IsChecked = _applied.MagnificationEnabled;
        IconTilesToggle.IsChecked = _applied.IconTiles;
        ClockWeatherToggle.IsChecked = _applied.ClockShowWeather;
        CompactSize.IsChecked = _applied.Size == DockSize.Compact;
        MediumSize.IsChecked = _applied.Size == DockSize.Medium;
        LargeSize.IsChecked = _applied.Size == DockSize.Large;
        DockScaleSlider.Value = Math.Round(_applied.DockScale * 100);
        UpdateDockScaleLabel();
        FocusMinutesSlider.Value = _applied.FocusMinutes;
        BreakMinutesSlider.Value = _applied.FocusBreakMinutes;
        FocusSessionsSlider.Value = _applied.FocusSessions;
        LoadFeatureSelection(_applied);
        UpdateWidgetOptionState();
        var startup = _startup.Read();
        _startupEnabled = startup.Enabled;
        _startupAvailable = startup.Error is null;
        StartupToggle.IsEnabled = _startupAvailable;
        if (startup.Error is not null)
        {
            StartupToggle.ToolTip = startup.Error;
            System.Windows.Controls.ToolTipService.SetShowOnDisabled(StartupToggle, true);
        }
        StartupToggle.IsChecked = _startupEnabled;
        var errors = new[] { preferences.LoadError, startup.Error }.OfType<string>();
        var message = string.Join(" ", errors.Select(UiText.LocalizeError));
        if (message.Length > 0) Trace.TraceWarning("Settings initialization warning: {0}", message);
        var workArea = SystemParameters.WorkArea;
        MinHeight = Math.Min(MinHeight, workArea.Height);
        MinWidth = Math.Min(MinWidth, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(PreferenceEdited));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(PreferenceEdited));
        _initializing = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WindowProc);
        _sourceReady = true;
        UpdateBackdrop();
        if (_preferences.Current.AnimationsEnabled)
        {
            PlayOpenAnimation();
        }
    }

    private void PlayOpenAnimation()
    {
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        var transform = new ScaleTransform(0.96, 0.96);
        SettingsFrame.RenderTransformOrigin = new Point(0.5, 0.5);
        SettingsFrame.RenderTransform = transform;
        SettingsFrame.BeginAnimation(UIElement.OpacityProperty, fade);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private nint WindowProc(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0024 || lParam == 0) return 0; // WM_GETMINMAXINFO
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            Trace.TraceWarning("Settings monitor work area unavailable: {0}", Marshal.GetLastWin32Error());
            return 0;
        }
        var sizing = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        sizing.MaxPosition = new NativePoint(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
        sizing.MaxSize = new NativePoint(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        var dpi = GetDpiForWindow(window);
        var scale = dpi > 0 ? dpi / 96d : 1;
        sizing.MinTrackSize = new NativePoint((int)Math.Ceiling(MinWidth * scale), (int)Math.Ceiling(MinHeight * scale));
        if (MaxWidth > 0 && !double.IsInfinity(MaxWidth) && !double.IsNaN(MaxWidth))
            sizing.MaxTrackSize = new NativePoint((int)Math.Ceiling(MaxWidth * scale), (int)Math.Ceiling(MaxHeight * scale));
        Marshal.StructureToPtr(sizing, lParam, false);
        handled = true;
        return 0;
    }

    private void Preferences_Changed(object? sender, EventArgs e)
    {
        if (_applied.Language != _preferences.Current.Language)
        {
            var localizedOptions = _widgetPreviewService.CreateOptions([], []).ToDictionary(option => option.Id, StringComparer.Ordinal);
            foreach (var option in _widgetOptions)
            {
                var localized = localizedOptions[option.Id];
                option.UpdateText(localized.Label, localized.Description);
            }
            RefreshWidgetPreviews();
        }
        ThemeManager.SetSurfaceOpacity(Math.Round(OpacitySlider.Value) / 100);
        UpdateOpacityLabel();
        UpdateDockScaleLabel();
        UpdateWidgetOptionState();
        UpdateBackdrop();
    }

    private void UpdateBackdrop()
    {
        if (!_sourceReady) return;
        var glass = SettingsBackdrop.TryEnable(this, _preferences.Current.Theme);
        SettingsFrame.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, glass ? "SettingsGlassMaterial" : "SettingsBackground");
        if (glass)
        {
            WindowChrome.GetWindowChrome(this).GlassFrameThickness = new Thickness(-1);
            SettingsFrame.ClearValue(System.Windows.Controls.Border.CornerRadiusProperty);
        }
        else
        {
            WindowChrome.GetWindowChrome(this).GlassFrameThickness = new Thickness(0);
            SettingsFrame.CornerRadius = new CornerRadius(0);
        }
    }

    private DockSettings ReadSelection()
    {
        var enabledWidgets = _widgetOptions.Where(option => option.IsInstalled && option.IsEnabled).Select(option => option.Id).ToArray();
        var installedWidgets = _widgetOptions.Where(option => option.IsInstalled).Select(option => option.Id).ToArray();
        bool IsEnabled(string id) => enabledWidgets.Contains(id, StringComparer.OrdinalIgnoreCase);
        return _preferences.Current with
        {
            Theme = LightTheme.IsChecked == true ? AppTheme.Light : AppTheme.Dark,
            Language = EnglishLanguage.IsChecked == true ? AppLanguage.English
                : PortugueseLanguage.IsChecked == true ? AppLanguage.Portuguese : AppLanguage.Turkish,
            // Full width keeps the last floating alignment so switching back restores it.
            Position = RightPosition.IsChecked == true ? DockPosition.Right : LeftPosition.IsChecked == true ? DockPosition.Left
                : CenterPosition.IsChecked == true ? DockPosition.Center : _preferences.Current.Position,
            AnimationsEnabled = AnimationsToggle.IsChecked == true,
            PreviewsEnabled = PreviewsToggle.IsChecked == true,
            NotificationBadgesEnabled = BadgesToggle.IsChecked == true,
            AutoHide = AutoHideAlways.IsChecked == true ? AutoHideMode.Always : AutoHideSmart.IsChecked == true ? AutoHideMode.Smart : AutoHideMode.Disabled,
            SurfaceOpacity = Math.Round(OpacitySlider.Value) / 100,
            Widgets = enabledWidgets,
            InstalledWidgets = installedWidgets,
            MagnificationEnabled = MagnificationToggle.IsChecked == true,
            IconTiles = IconTilesToggle.IsChecked == true,
            ClockShowWeather = ClockWeatherToggle.IsChecked == true,
            Layout = FullWidthLayout.IsChecked == true ? DockLayout.FullWidth : DockLayout.Floating,
            Size = LargeSize.IsChecked == true ? DockSize.Large : MediumSize.IsChecked == true ? DockSize.Medium : DockSize.Compact,
            DockScale = Math.Round(DockScaleSlider.Value) / 100,
            ShowClock = IsEnabled("Clock"),
            ShowMedia = IsEnabled("Media"),
            ShowSystemStats = IsEnabled("SystemStats"),
            ShowAudio = IsEnabled("Audio"),
            ShowQuickSettings = IsEnabled("QuickSettings"),
            ShowFocus = IsEnabled("Focus"),
            FocusMinutes = (int)Math.Round(FocusMinutesSlider.Value),
            FocusBreakMinutes = (int)Math.Round(BreakMinutesSlider.Value),
            FocusSessions = (int)Math.Round(FocusSessionsSlider.Value),
            ShowRecycleBin = RecycleBinToggle.IsChecked == true,
            ShowOnAllMonitors = AllMonitorsToggle.IsChecked == true,
            CurrentDesktopOnly = CurrentDesktopToggle.IsChecked == true,
            CheckUpdates = UpdatesToggle.IsChecked == true,
            Edge = LeftEdge.IsChecked == true ? DockEdge.Left : RightEdge.IsChecked == true ? DockEdge.Right : DockEdge.Bottom,
            ShowVirtualDesktops = IsEnabled("Desktops"),
            ShowTray = IsEnabled("Tray"),
            ShowWeather = IsEnabled("Weather"),
            WeatherCity = WeatherCityBox.Text.Trim(),
            LauncherHotkey = LauncherAltSpace.IsChecked == true ? HotkeyChoice.AltSpace
                : LauncherCtrlSpace.IsChecked == true ? HotkeyChoice.CtrlSpace
                : LauncherCtrlAltSpace.IsChecked == true ? HotkeyChoice.CtrlAltSpace : HotkeyChoice.Disabled,
            DockHotkey = DockCtrlAltD.IsChecked == true ? HotkeyChoice.CtrlAltD : DockWinAltD.IsChecked == true ? HotkeyChoice.WinAltD : HotkeyChoice.Disabled,
            StartAction = StartOpensLauncher.IsChecked == true ? StartButtonAction.Launcher : StartButtonAction.WindowsStart,
            StartPlacement = StartLast.IsChecked == true ? StartButtonPlacement.End : StartHidden.IsChecked == true ? StartButtonPlacement.Hidden : StartButtonPlacement.Start,
            WheelVolume = WheelVolumeToggle.IsChecked == true,
            Clock24Hour = Clock12HourRadio.IsChecked != true,
            ClockShowSeconds = ClockSecondsToggle.IsChecked == true,
            DateStyle = DateStyleHidden.IsChecked == true ? ClockDateStyle.Hidden
                : DateStyleNumeric.IsChecked == true ? ClockDateStyle.NumericDate
                : DateStyleShort.IsChecked == true ? ClockDateStyle.ShortDateOnly
                : ClockDateStyle.DayAndShortDate,
            StatsMeters = ReadStatsMeters()
        };
    }

    private SystemMeters ReadStatsMeters() =>
        (StatsCpuToggle.IsChecked == true ? SystemMeters.Cpu : 0)
        | (StatsMemoryToggle.IsChecked == true ? SystemMeters.Memory : 0)
        | (StatsBatteryToggle.IsChecked == true ? SystemMeters.Battery : 0)
        | (StatsCpuTemperatureToggle.IsChecked == true ? SystemMeters.CpuTemperature : 0)
        | (StatsGpuToggle.IsChecked == true ? SystemMeters.Gpu : 0)
        | (StatsGpuTemperatureToggle.IsChecked == true ? SystemMeters.GpuTemperature : 0);

    private void RefreshWidgetPreviews() => DockWidgetService.Refresh(
        _widgetOptions.Select(option => option.Preview), _preferences.Current.Language);

    private void WidgetPreviewTimer_Tick(object? sender, EventArgs e) => RefreshWidgetPreviews();

    private void Settings_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateWidgetPreviewRefresh();

    private void SettingsTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_initializing && ReferenceEquals(e.Source, SettingsTabs))
        {
            UpdateWidgetPreviewRefresh();
            if (_preferences.Current.AnimationsEnabled && SettingsTabs.Template?.FindName("PART_SelectedContentHost", SettingsTabs) is ContentPresenter host)
            {
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                host.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }
    }

    private void UpdateWidgetPreviewRefresh()
    {
        if (IsVisible && WidgetsTab.IsSelected)
        {
            RefreshWidgetPreviews();
            _widgetPreviewTimer.Start();
        }
        else _widgetPreviewTimer.Stop();
    }

    private void WidgetInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: DockWidgetOption option }) return;
        _initializing = true;
        if (option.IsInstalled)
        {
            option.IsInstalled = false;
            option.IsEnabled = false;
            option.IsSettingsExpanded = false;
        }
        else
        {
            option.IsInstalled = true;
            option.IsEnabled = true;
        }
        _initializing = false;
        UpdateWidgetOptionState();
        SaveSettings();
    }

    private void WidgetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: DockWidgetOption option }) return;
        if (!option.HasSettings) return;

        if (!option.IsInstalled)
        {
            _initializing = true;
            option.IsInstalled = true;
            option.IsEnabled = true;
            _initializing = false;
            SaveSettings();
        }

        option.IsSettingsExpanded = !option.IsSettingsExpanded;
        UpdateWidgetOptionState();

        if (option.IsSettingsExpanded)
        {
            FrameworkElement? target = option.Id switch
            {
                "Clock" => ClockOptions,
                "Weather" => WeatherOptions,
                "Focus" => FocusOptions,
                "SystemStats" => StatsOptions,
                _ => null
            };
            target?.BringIntoView();
        }
    }

    private void CollapseClockOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_widgetOptions.FirstOrDefault(o => o.Id == "Clock") is { } option)
            option.IsSettingsExpanded = false;
        UpdateWidgetOptionState();
    }

    private void CollapseWeatherOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_widgetOptions.FirstOrDefault(o => o.Id == "Weather") is { } option)
            option.IsSettingsExpanded = false;
        UpdateWidgetOptionState();
    }

    private void CollapseFocusOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_widgetOptions.FirstOrDefault(o => o.Id == "Focus") is { } option)
            option.IsSettingsExpanded = false;
        UpdateWidgetOptionState();
    }

    private void CollapseStatsOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_widgetOptions.FirstOrDefault(o => o.Id == "SystemStats") is { } option)
            option.IsSettingsExpanded = false;
        UpdateWidgetOptionState();
    }

    private void SelectAlignment(DockSettings settings)
    {
        var fullWidth = settings.Layout == DockLayout.FullWidth;
        FullWidthLayout.IsChecked = fullWidth;
        LeftPosition.IsChecked = !fullWidth && settings.Position == DockPosition.Left;
        CenterPosition.IsChecked = !fullWidth && settings.Position == DockPosition.Center;
        RightPosition.IsChecked = !fullWidth && settings.Position == DockPosition.Right;
    }

    private void LoadFeatureSelection(DockSettings settings)
    {
        RecycleBinToggle.IsChecked = settings.ShowRecycleBin;
        AllMonitorsToggle.IsChecked = settings.ShowOnAllMonitors;
        CurrentDesktopToggle.IsChecked = settings.CurrentDesktopOnly;
        UpdatesToggle.IsChecked = settings.CheckUpdates;
        BottomEdge.IsChecked = settings.Edge == DockEdge.Bottom;
        LeftEdge.IsChecked = settings.Edge == DockEdge.Left;
        RightEdge.IsChecked = settings.Edge == DockEdge.Right;
        Clock24HourRadio.IsChecked = settings.Clock24Hour;
        Clock12HourRadio.IsChecked = !settings.Clock24Hour;
        ClockSecondsToggle.IsChecked = settings.ClockShowSeconds;
        DateStyleDayShort.IsChecked = settings.DateStyle == ClockDateStyle.DayAndShortDate;
        DateStyleShort.IsChecked = settings.DateStyle == ClockDateStyle.ShortDateOnly;
        DateStyleNumeric.IsChecked = settings.DateStyle == ClockDateStyle.NumericDate;
        DateStyleHidden.IsChecked = settings.DateStyle == ClockDateStyle.Hidden;
        WeatherCityBox.Text = settings.WeatherCity;
        StatsCpuToggle.IsChecked = settings.StatsMeters.HasFlag(SystemMeters.Cpu);
        StatsMemoryToggle.IsChecked = settings.StatsMeters.HasFlag(SystemMeters.Memory);
        StatsBatteryToggle.IsChecked = settings.StatsMeters.HasFlag(SystemMeters.Battery);
        StatsCpuTemperatureToggle.IsChecked = settings.StatsMeters.HasFlag(SystemMeters.CpuTemperature);
        StatsGpuToggle.IsChecked = settings.StatsMeters.HasFlag(SystemMeters.Gpu);
        StatsGpuTemperatureToggle.IsChecked = settings.StatsMeters.HasFlag(SystemMeters.GpuTemperature);
        LauncherAltSpace.IsChecked = settings.LauncherHotkey == HotkeyChoice.AltSpace;
        LauncherCtrlSpace.IsChecked = settings.LauncherHotkey == HotkeyChoice.CtrlSpace;
        LauncherCtrlAltSpace.IsChecked = settings.LauncherHotkey == HotkeyChoice.CtrlAltSpace;
        LauncherOff.IsChecked = settings.LauncherHotkey is not (HotkeyChoice.AltSpace or HotkeyChoice.CtrlSpace or HotkeyChoice.CtrlAltSpace);
        DockCtrlAltD.IsChecked = settings.DockHotkey == HotkeyChoice.CtrlAltD;
        DockWinAltD.IsChecked = settings.DockHotkey == HotkeyChoice.WinAltD;
        DockOff.IsChecked = settings.DockHotkey is not (HotkeyChoice.CtrlAltD or HotkeyChoice.WinAltD);
        StartOpensWindows.IsChecked = settings.StartAction == StartButtonAction.WindowsStart;
        StartOpensLauncher.IsChecked = settings.StartAction == StartButtonAction.Launcher;
        StartFirst.IsChecked = settings.StartPlacement == StartButtonPlacement.Start;
        StartLast.IsChecked = settings.StartPlacement == StartButtonPlacement.End;
        StartHidden.IsChecked = settings.StartPlacement == StartButtonPlacement.Hidden;
        WheelVolumeToggle.IsChecked = settings.WheelVolume;
        _ = InitializeWeatherLocationsAsync(settings.WeatherCity);
    }

    private void AttachWidgetSettingsPanels()
    {
        WidgetPanelsPool.Children.Clear();
        if (_widgetOptions.FirstOrDefault(o => o.Id == "Clock") is { } clock) clock.SettingsView = ClockOptions;
        if (_widgetOptions.FirstOrDefault(o => o.Id == "Weather") is { } weather) weather.SettingsView = WeatherOptions;
        if (_widgetOptions.FirstOrDefault(o => o.Id == "Focus") is { } focus) focus.SettingsView = FocusOptions;
        if (_widgetOptions.FirstOrDefault(o => o.Id == "SystemStats") is { } stats) stats.SettingsView = StatsOptions;
    }

    private async Task InitializeWeatherLocationsAsync(string savedCity)
    {
        try
        {
            var countries = await CountryCityService.GetCountriesAsync();
            WeatherCountryBox.ItemsSource = countries.Select(c => c.Name).ToList();

            if (!string.IsNullOrWhiteSpace(savedCity))
            {
                var trCities = await CountryCityService.GetCitiesAsync("Turkey");
                string matchedCountry = trCities.Contains(savedCity, StringComparer.OrdinalIgnoreCase) ? "Turkey" : "Turkey";
                WeatherCountryBox.SelectedItem = matchedCountry;
                WeatherCityContainer.Visibility = Visibility.Visible;
                WeatherCityCombo.ItemsSource = trCities;
                WeatherCityCombo.SelectedItem = savedCity;
                WeatherCityBox.Text = savedCity;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Weather location initialization error: {ex.Message}");
        }
    }

    private async void WeatherCountryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var country = WeatherCountryBox.SelectedItem as string ?? WeatherCountryBox.Text.Trim();
        await OnCountrySelectedAsync(country);
    }

    private async void WeatherCountryBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Tab)
        {
            var country = WeatherCountryBox.Text.Trim();
            await OnCountrySelectedAsync(country);
        }
    }

    private async Task OnCountrySelectedAsync(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            WeatherCityContainer.Visibility = Visibility.Collapsed;
            WeatherCityBox.Text = string.Empty;
            SaveSettings();
            return;
        }

        WeatherCityContainer.Visibility = Visibility.Visible;
        var cities = await CountryCityService.GetCitiesAsync(country);
        WeatherCityCombo.ItemsSource = cities;
        if (cities.Count > 0)
        {
            WeatherCityCombo.IsDropDownOpen = true;
        }
    }

    private void WeatherCityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var city = WeatherCityCombo.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(city))
        {
            WeatherCityBox.Text = city;
            PreferenceEdited(sender, e);
        }
    }

    private void WeatherCityCombo_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Tab)
        {
            var city = WeatherCityCombo.Text.Trim();
            if (!string.IsNullOrWhiteSpace(city))
            {
                WeatherCityBox.Text = city;
                PreferenceEdited(sender, e);
            }
        }
    }

    private void WeatherCityBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => PreferenceEdited(sender, e);

    private void WeatherCityBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_autoSaveTimer.IsEnabled)
        {
            _autoSaveTimer.Stop();
            SaveSettings();
        }
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "OpenTaskbar-settings.json",
            Filter = "OpenTaskbar (*.json)|*.json",
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        var error = SettingsBackup.Export(dialog.FileName, _preferences.Current);
        MessageBox.Show(this, error ?? UiText.Get("ExportDone"), "OpenTaskbar", MessageBoxButton.OK,
            error is not null ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "OpenTaskbar (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var (settings, pins, error) = SettingsBackup.Read(dialog.FileName);
        if (settings is null)
        {
            MessageBox.Show(this, error ?? UiText.Get("ErrorSettingsFileInvalid"), "OpenTaskbar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var added = (Application.Current.MainWindow as MainWindow)?.ImportPins(pins) ?? 0;
        if (_preferences.Save(settings with { PinOrder = settings.PinOrder ?? _preferences.Current.PinOrder }) is { } saveError)
        {
            MessageBox.Show(this, saveError, "OpenTaskbar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _applied = _preferences.Current;
        _initializing = true;
        LoadAllSelections(_applied);
        _initializing = false;
        UpdateWidgetOptionState();
        MessageBox.Show(this, UiText.Format("ImportDone", added), "OpenTaskbar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadAllSelections(DockSettings settings)
    {
        DarkTheme.IsChecked = settings.Theme == AppTheme.Dark;
        LightTheme.IsChecked = settings.Theme == AppTheme.Light;
        TurkishLanguage.IsChecked = settings.Language == AppLanguage.Turkish;
        EnglishLanguage.IsChecked = settings.Language == AppLanguage.English;
        PortugueseLanguage.IsChecked = settings.Language == AppLanguage.Portuguese;
        SelectAlignment(settings);
        AnimationsToggle.IsChecked = settings.AnimationsEnabled;
        PreviewsToggle.IsChecked = settings.PreviewsEnabled;
        BadgesToggle.IsChecked = settings.NotificationBadgesEnabled;
        AutoHideDisabled.IsChecked = settings.AutoHide == AutoHideMode.Disabled;
        AutoHideSmart.IsChecked = settings.AutoHide == AutoHideMode.Smart;
        AutoHideAlways.IsChecked = settings.AutoHide == AutoHideMode.Always;
        OpacitySlider.Value = settings.SurfaceOpacity * 100;
        UpdateOpacityLabel();
        MagnificationToggle.IsChecked = settings.MagnificationEnabled;
        IconTilesToggle.IsChecked = settings.IconTiles;
        ClockWeatherToggle.IsChecked = settings.ClockShowWeather;
        CompactSize.IsChecked = settings.Size == DockSize.Compact;
        MediumSize.IsChecked = settings.Size == DockSize.Medium;
        LargeSize.IsChecked = settings.Size == DockSize.Large;
        DockScaleSlider.Value = Math.Round(settings.DockScale * 100);
        UpdateDockScaleLabel();
        foreach (var option in _widgetOptions)
        {
            option.IsInstalled = settings.InstalledWidgets?.Contains(option.Id, StringComparer.Ordinal) == true;
            option.IsEnabled = settings.Widgets?.Contains(option.Id, StringComparer.Ordinal) == true;
        }
        FocusMinutesSlider.Value = settings.FocusMinutes;
        BreakMinutesSlider.Value = _applied.FocusBreakMinutes;
        FocusSessionsSlider.Value = settings.FocusSessions;
        LoadFeatureSelection(settings);
    }

    private void UpdateWidgetOptionState()
    {
        bool IsExpanded(string id) => _widgetOptions.Any(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase) && o.IsSettingsExpanded);
        ClockOptions.Visibility = IsExpanded("Clock") ? Visibility.Visible : Visibility.Collapsed;
        FocusOptions.Visibility = IsExpanded("Focus") ? Visibility.Visible : Visibility.Collapsed;
        StatsOptions.Visibility = IsExpanded("SystemStats") ? Visibility.Visible : Visibility.Collapsed;
        WeatherOptions.Visibility = IsExpanded("Weather") ? Visibility.Visible : Visibility.Collapsed;
        FocusMinutesValue.Text = UiText.Format("MinutesFormat", (int)Math.Round(FocusMinutesSlider.Value));
        BreakMinutesValue.Text = UiText.Format("MinutesFormat", (int)Math.Round(BreakMinutesSlider.Value));
        FocusSessionsValue.Text = ((int)Math.Round(FocusSessionsSlider.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FocusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        PreferenceEdited(sender, e);
    }

    private void SaveSettings()
    {
        var startupChanged = _startupAvailable && StartupToggle.IsChecked != _startupEnabled;
        if (startupChanged && _startup.SetEnabled(StartupToggle.IsChecked == true) is { } startupError)
        {
            Trace.TraceWarning("Startup error: {0}", startupError);
            return;
        }
        var settings = ReadSelection();
        if (_preferences.Save(settings) is { } error)
        {
            Trace.TraceWarning("Save settings error: {0}", error);
            return;
        }
        _applied = settings;
        _startupEnabled = StartupToggle.IsChecked == true;
    }

    private bool _isClosing;
    private bool _isClosingCompleted;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingApplication || Application.Current.Dispatcher.HasShutdownStarted || _isClosingCompleted) return;
        if (_preferences.Current.AnimationsEnabled && IsLoaded && !_isClosing)
        {
            e.Cancel = true;
            AnimateAndClose();
            return;
        }
        if (_autoSaveTimer.IsEnabled)
        {
            _autoSaveTimer.Stop();
            SaveSettings();
        }
    }

    private void AnimateAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        if (_autoSaveTimer.IsEnabled)
        {
            _autoSaveTimer.Stop();
            SaveSettings();
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.95, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };

        var transform = new ScaleTransform(1.0, 1.0);
        SettingsFrame.RenderTransformOrigin = new Point(0.5, 0.5);
        SettingsFrame.RenderTransform = transform;

        fade.Completed += (_, _) =>
        {
            _isClosingCompleted = true;
            Close();
        };

        SettingsFrame.BeginAnimation(UIElement.OpacityProperty, fade);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void PreferenceEdited(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var selectedSize = LargeSize.IsChecked == true ? DockSize.Large : MediumSize.IsChecked == true ? DockSize.Medium : DockSize.Compact;
        (Application.Current.MainWindow as MainWindow)?.SetSizePreview(selectedSize, Math.Round(DockScaleSlider.Value) / 100);
        UpdateWidgetOptionState();
        TriggerAutoSave(immediate: sender is not (Slider or TextBox));
    }

    private void TriggerAutoSave(bool immediate)
    {
        if (_initializing) return;
        if (immediate)
        {
            _autoSaveTimer.Stop();
            SaveSettings();
        }
        else
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }

    private void UpdateOpacityLabel() => OpacityValue.Text = UiText.Format("OpacityPercent", (int)Math.Round(OpacitySlider.Value));

    private void UpdateDockScaleLabel() => DockScaleValue.Text = UiText.Format("OpacityPercent", (int)Math.Round(DockScaleSlider.Value));

    private void DockScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        UpdateDockScaleLabel();
        PreferenceEdited(sender, e);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        UpdateOpacityLabel();
        ThemeManager.SetSurfaceOpacity(Math.Round(e.NewValue) / 100);
        PreferenceEdited(sender, e);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    internal void CloseForShutdown()
    {
        _closingApplication = true;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
