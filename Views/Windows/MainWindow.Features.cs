using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>Global hotkeys, keyboard navigation, profiles, update checks, and the virtual desktop filter.</summary>
public partial class MainWindow
{
    private const int WmHotkey = 0x0312;
    private const int LauncherHotkeyId = 0x4F01;
    private const int DockHotkeyId = 0x4F02;
    private (HotkeyChoice Launcher, HotkeyChoice Dock)? _registeredHotkeys;
    private bool _keyboardNavigationActive;
    private nint _foregroundBeforeKeyboard;
    private LauncherWindow? _launcher;
    private UpdateChecker.Release? _availableUpdate;
    private DispatcherTimer? _updateTimer;
    private CancellationTokenSource? _updateCheck;
    private DispatcherTimer? _desktopTimer;
    private int _currentDesktop = 1;
    private IReadOnlyList<TrackedWindow>? _filteredSource;
    private IReadOnlyList<TrackedWindow> _filteredWindows = [];
    private int _filteredDesktop;
    private string? _appliedProfileSignature;
    private bool _profileFilterApplied;
    private readonly List<SecondaryDockWindow> _secondaryDocks = [];
    private DispatcherTimer? _secondaryTimer;

    private void InitializeFeatures()
    {
        Deactivated += (_, _) =>
        {
            EndKeyboardNavigation(restoreForeground: false);
            DockFlyout.CloseAll();
        };
        PreviewKeyDown += Dock_PreviewKeyDown;
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(12) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer!.Interval = TimeSpan.FromHours(24);
            await CheckForUpdatesAsync(userInitiated: false);
        };
        _desktopTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _desktopTimer.Tick += (_, _) => RefreshDesktopFilter();
        ApplyFeatureSettings();
    }

    private void ApplyFeatureSettings()
    {
        if (_updateTimer is null) return;
        RegisterHotkeys();
        if (_preferences.Current.CheckUpdates) { if (!_updateTimer.IsEnabled) _updateTimer.Start(); }
        else _updateTimer.Stop();
        if (_preferences.Current.CurrentDesktopOnly) _desktopTimer?.Start();
        else _desktopTimer?.Stop();
        _filteredSource = null;
        ApplyStartPlacement();
        ApplyEdge();
        ApplySecondaryDocks();
        RebuildAppGroups();
        ApplyProfileFilter();
    }

    private bool StartButtonShown => _preferences.Current.StartPlacement != StartButtonPlacement.Hidden;

    /// <summary>Moves the Start button (and its divider) to the front or the end of the dock, or hides it.</summary>
    private void ApplyStartPlacement()
    {
        var placement = _preferences.Current.StartPlacement;
        StartButton.Visibility = StartDivider.Visibility = placement == StartButtonPlacement.Hidden ? Visibility.Collapsed : Visibility.Visible;
        var children = DockContents.Children;
        children.Remove(StartButton);
        children.Remove(StartDivider);
        if (placement == StartButtonPlacement.End)
        {
            var index = children.IndexOf(StatusText);
            children.Insert(index, StartDivider);
            children.Insert(index + 1, StartButton);
        }
        else
        {
            children.Insert(0, StartButton);
            children.Insert(1, StartDivider);
        }
    }

    // ---- Global hotkeys (#2) ----

    private static (uint Modifiers, uint Key)? DescribeHotkey(HotkeyChoice choice) => choice switch
    {
        HotkeyChoice.AltSpace => (0x1, 0x20),
        HotkeyChoice.CtrlSpace => (0x2, 0x20),
        HotkeyChoice.CtrlAltSpace => (0x3, 0x20),
        HotkeyChoice.CtrlAltD => (0x3, 0x44),
        HotkeyChoice.WinAltD => (0x9, 0x44),
        _ => null
    };

    private void RegisterHotkeys()
    {
        if (_source is null) return;
        var wanted = (_preferences.Current.LauncherHotkey, _preferences.Current.DockHotkey);
        if (_registeredHotkeys == wanted) return;
        UnregisterHotkeys();
        _registeredHotkeys = wanted;
        var failed = false;
        foreach (var (id, choice) in new[] { (LauncherHotkeyId, wanted.LauncherHotkey), (DockHotkeyId, wanted.DockHotkey) })
        {
            if (DescribeHotkey(choice) is not { } hotkey) continue;
            const uint noRepeat = 0x4000;
            if (!RegisterHotKey(_source.Handle, id, hotkey.Modifiers | noRepeat, hotkey.Key))
            {
                AppLog.Warning($"RegisterHotKey {choice} failed: {Marshal.GetLastPInvokeError()}");
                failed = true;
            }
        }

        if (failed) ShowStatus(UiText.Get("ErrorHotkeyRegister"));
    }

    private void UnregisterHotkeys()
    {
        if (_source is null) return;
        UnregisterHotKey(_source.Handle, LauncherHotkeyId);
        UnregisterHotKey(_source.Handle, DockHotkeyId);
        _registeredHotkeys = null;
    }

    private bool HandleHotkeyMessage(int message, nint wParam)
    {
        if (message != WmHotkey) return false;
        switch (wParam.ToInt32())
        {
            case LauncherHotkeyId:
                ToggleLauncher();
                return true;
            case DockHotkeyId:
                if (_keyboardNavigationActive) EndKeyboardNavigation(restoreForeground: true);
                else StartKeyboardNavigation();
                return true;
            default:
                return false;
        }
    }

    // ---- Launcher (#12) ----

    private void ToggleLauncher()
    {
        _launcher ??= CreateLauncher();
        _launcher.Toggle();
    }

    private LauncherWindow CreateLauncher()
    {
        var launcher = new LauncherWindow(ShowStatus);
        launcher.Closed += (_, _) => _launcher = null;
        return launcher;
    }

    private void OpenLauncher_Click(object sender, RoutedEventArgs e) => ToggleLauncher();

    private void OpenTaskManager_Click(object sender, RoutedEventArgs e) => ReportResult(ShellShortcuts.OpenTaskManager());

    // ---- Keyboard navigation and accessibility (#16) ----

    private void StartKeyboardNavigation()
    {
        _foregroundBeforeKeyboard = NativeWindowTracker.GetForegroundApplicationWindow();
        _keyboardNavigationActive = true;
        _autoHideController?.AnimateReveal();
        _thumbnailPreview.Hide();
        // WM_HOTKEY grants this process foreground rights, so the dock can take keyboard focus for a moment.
        Activate();
        var first = GetAllDockButtons().FirstOrDefault(button => button.IsVisible);
        first?.Focus();
        Keyboard.Focus(first);
    }

    private void EndKeyboardNavigation(bool restoreForeground)
    {
        if (!_keyboardNavigationActive) return;
        _keyboardNavigationActive = false;
        Keyboard.ClearFocus();
        if (restoreForeground && _foregroundBeforeKeyboard != 0) NativeWindowTracker.Activate(_foregroundBeforeKeyboard);
        _autoHideController?.EvaluateState();
    }

    private void Dock_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_keyboardNavigationActive) return;
        var vertical = _preferences.Current.Edge != DockEdge.Bottom;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var forward = vertical ? Key.Down : Key.Right;
        var backward = vertical ? Key.Up : Key.Left;
        if (key == Key.Escape)
        {
            e.Handled = true;
            EndKeyboardNavigation(restoreForeground: true);
            return;
        }

        if (key != forward && key != backward && key != Key.Home && key != Key.End) return;
        var buttons = GetAllDockButtons().Where(button => button.IsVisible)
            .OrderBy(button => vertical ? button.TranslatePoint(default, DockContents).Y : button.TranslatePoint(default, DockContents).X)
            .ToList();
        if (buttons.Count == 0) return;
        var current = buttons.FindIndex(button => button.IsKeyboardFocusWithin);
        var target = key switch
        {
            Key.Home => 0,
            Key.End => buttons.Count - 1,
            _ when key == forward => Math.Min(buttons.Count - 1, current + 1),
            _ => Math.Max(0, current - 1)
        };
        buttons[target].Focus();
        e.Handled = true;
    }

    // ---- Profiles (#14) ----

    private DockProfile? ActiveProfile => _preferences.Current.Profiles?.FirstOrDefault(profile =>
        string.Equals(profile.Name, _preferences.Current.ActiveProfile, StringComparison.CurrentCultureIgnoreCase));

    private bool IsHiddenByProfile(DockApp app) =>
        ActiveProfile?.HiddenApps.Contains(app.ExecutablePath, StringComparer.OrdinalIgnoreCase) == true;

    private int VisiblePinnedCount => _pinnedApps.Count(app => !IsHiddenByProfile(app) && !IsGrouped(app)) + _appGroups.Count;

    private void ApplyProfileFilter()
    {
        // Reassigning the filter rebuilds every pinned button, so only do it when the hidden set actually changes.
        // Grouped apps live inside their group tile, so they leave the pinned row like profile-hidden apps.
        var profileSignature = ActiveProfile is { } profile
            ? string.Join("|", profile.HiddenApps.Order(StringComparer.OrdinalIgnoreCase))
            : null;
        var groupSignature = _groupedPaths.Count > 0 ? string.Join("|", _groupedPaths.Order(StringComparer.OrdinalIgnoreCase)) : null;
        var signature = profileSignature is null && groupSignature is null ? null : $"{profileSignature}#{groupSignature}";
        if (_appliedProfileSignature == signature && _profileFilterApplied) return;
        _appliedProfileSignature = signature;
        _profileFilterApplied = true;
        var view = CollectionViewSource.GetDefaultView(_pinnedApps);
        view.Filter = signature is null ? null : item => item is DockApp app && !IsHiddenByProfile(app) && !IsGrouped(app);
        if (_windowTracker is not null) UpdateRunningApps(VisibleSnapshot);
        ResizeDockToContent();
        _replacementSession?.RefreshPosition();
    }

    private void SaveProfiles(IReadOnlyList<DockProfile>? profiles, string? active)
    {
        var error = _preferences.Save(_preferences.Current with { Profiles = profiles is { Count: > 0 } ? profiles : null, ActiveProfile = active });
        if (error is not null) ShowStatus(error);
    }

    private void PopulateProfilesMenu(MenuItem menu)
    {
        menu.Items.Clear();
        var profiles = _preferences.Current.Profiles ?? [];
        var active = ActiveProfile;
        var all = new MenuItem { Header = UiText.Get("ProfileAll"), IsCheckable = true, IsChecked = active is null };
        all.Click += (_, _) => SaveProfiles(profiles, null);
        menu.Items.Add(all);
        foreach (var profile in profiles)
        {
            var item = new MenuItem { Header = profile.Name.Replace("_", "__"), IsCheckable = true, IsChecked = ReferenceEquals(profile, active) };
            item.Click += (_, _) => SaveProfiles(profiles, profile.Name);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var create = new MenuItem { Header = UiText.Get("ProfileNew"), Icon = FindResource("IconPlus") };
        create.Click += (_, _) => CreateProfile();
        menu.Items.Add(create);
        if (active is null) return;
        if (active.HiddenApps.Count > 0)
        {
            var reveal = new MenuItem { Header = UiText.Get("ProfileShowAll") };
            reveal.Click += (_, _) => SaveProfiles(
                profiles.Select(profile => ReferenceEquals(profile, active) ? profile with { HiddenApps = [] } : profile).ToList(), active.Name);
            menu.Items.Add(reveal);
        }

        var delete = new MenuItem { Header = UiText.Format("ProfileDelete", active.Name.Replace("_", "__")), Icon = FindResource("IconClose") };
        delete.SetResourceReference(StyleProperty, "DestructiveMenuItem");
        delete.Click += (_, _) => SaveProfiles(profiles.Where(profile => !ReferenceEquals(profile, active)).ToList(), null);
        menu.Items.Add(delete);
    }

    private void CreateProfile()
    {
        var name = TextPrompt.Ask(UiText.Get("ProfileNew"), UiText.Get("ProfileNamePrompt"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var profiles = (_preferences.Current.Profiles ?? []).ToList();
        if (profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            ShowStatus(UiText.Get("ErrorProfileExists"));
            return;
        }

        profiles.Add(new DockProfile(name, []));
        SaveProfiles(profiles, name);
    }

    private void HideInProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: DockApp app } || ActiveProfile is not { } active) return;
        var profiles = (_preferences.Current.Profiles ?? []).Select(profile => ReferenceEquals(profile, active)
            ? profile with { HiddenApps = profile.HiddenApps.Append(app.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }
            : profile).ToList();
        SaveProfiles(profiles, active.Name);
    }

    /// <summary>Pinning an app the active profile hides only shows it again; the Windows pin already exists.</summary>
    private bool RevealHiddenPin(DockApp app)
    {
        if (ActiveProfile is not { } active || !_pinnedApps.Any(pinned =>
                string.Equals(pinned.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var profiles = (_preferences.Current.Profiles ?? []).Select(profile => ReferenceEquals(profile, active)
            ? profile with { HiddenApps = profile.HiddenApps.Where(path => !string.Equals(path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToList() }
            : profile).ToList();
        SaveProfiles(profiles, active.Name);
        return true;
    }

    // ---- Extra launch commands in the app menu (#3) ----

    private void NewInstance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { CommandParameter: DockApp app }) LaunchApp(app, elevated: false);
    }

    private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { CommandParameter: DockApp app }) LaunchApp(app, elevated: true);
    }

    // ---- Update checks (#15) ----

    private bool _isUpdating;

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        _updateCheck?.Cancel();
        var check = new CancellationTokenSource();
        _updateCheck = check;
        var (release, succeeded) = await UpdateChecker.FindNewerAsync(check.Token);
        if (!ReferenceEquals(_updateCheck, check) || !IsLoaded) return;
        if (!succeeded)
        {
            // Background checks stay quiet when offline; a manual check informs via toast.
            if (userInitiated) NotificationService.ShowInfoToast(UiText.Get("ErrorUpdateCheck"));
            return;
        }

        _availableUpdate = release;
        if (release is not null)
        {
            NotificationService.ShowUpdateToast(release.Tag, release.Url);
        }
        else if (userInitiated)
        {
            NotificationService.ShowInfoToast(UiText.Format("UpdateCurrent", UpdateChecker.CurrentVersion.ToString(3)));
        }
    }

    private async Task StartUpdateAsync(UpdateChecker.Release release)
    {
        if (_isUpdating)
        {
            NotificationService.ShowInfoToast(UiText.Get("UpdateInProgress"));
            return;
        }

        if (string.IsNullOrEmpty(release.DownloadUrl))
        {
            ReportResult(ShellShortcuts.OpenUri(release.Url));
            return;
        }

        _isUpdating = true;
        NotificationService.ShowInfoToast(UiText.Format("UpdateDownloading", release.Tag));

        try
        {
            var (success, error) = await Task.Run(() => UpdateChecker.DownloadAndApplyAsync(release));
            if (!success)
            {
                _isUpdating = false;
                NotificationService.ShowInfoToast(error ?? UiText.Get("UpdateFailed"));
            }
        }
        catch (Exception ex)
        {
            _isUpdating = false;
            AppLog.Error($"Update failed: {ex.Message}");
            NotificationService.ShowInfoToast(UiText.Get("UpdateFailed"));
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is { } release)
        {
            await StartUpdateAsync(release);
            return;
        }

        await CheckForUpdatesAsync(userInitiated: true);
    }

    // ---- Virtual desktop filter (#7) ----

    /// <summary>The tracked windows, limited to the current virtual desktop when that option is on.</summary>
    private IReadOnlyList<TrackedWindow> VisibleSnapshot => FilterWindows(_windowTracker?.Snapshot ?? []);

    private IReadOnlyList<TrackedWindow> FilterWindows(IReadOnlyList<TrackedWindow> windows)
    {
        if (!_preferences.Current.CurrentDesktopOnly) return windows;
        if (ReferenceEquals(windows, _filteredSource) && _filteredDesktop == _currentDesktop) return _filteredWindows;
        _filteredSource = windows;
        _filteredDesktop = _currentDesktop;
        _filteredWindows = windows.Where(window => VirtualDesktops.IsOnCurrentDesktop(window.Handle)).ToList();
        return _filteredWindows;
    }

    private void RefreshDesktopFilter()
    {
        var (current, _) = VirtualDesktops.Read();
        if (current == _currentDesktop || _windowTracker is null) return;
        _currentDesktop = current;
        WindowTracker_WindowsChanged(this, _windowTracker.Snapshot);
    }

    // ---- Docks on other displays (#10) ----

    private void ApplySecondaryDocks()
    {
        var wanted = _preferences.Current.ShowOnAllMonitors ? SecondaryDockWindow.SecondaryMonitors() : [];
        foreach (var dock in _secondaryDocks.Where(dock => !wanted.Contains(dock.Monitor)).ToList())
        {
            dock.Close();
            _secondaryDocks.Remove(dock);
        }

        foreach (var monitor in wanted.Where(monitor => _secondaryDocks.All(dock => dock.Monitor != monitor)))
        {
            var dock = new SecondaryDockWindow(monitor, _runningAppIcons);
            dock.Show();
            _secondaryDocks.Add(dock);
        }

        foreach (var dock in _secondaryDocks) dock.PositionOnMonitor();
        if (_secondaryDocks.Count > 0)
        {
            _secondaryTimer ??= CreateSecondaryTimer();
            _secondaryTimer.Start();
        }
        else
        {
            _secondaryTimer?.Stop();
        }

        UpdateSecondaryDocks();
    }

    private DispatcherTimer CreateSecondaryTimer()
    {
        // Keeps the secondary clocks current; window changes update the docks immediately.
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => UpdateSecondaryDocks();
        return timer;
    }

    private void UpdateSecondaryDocks()
    {
        if (_secondaryDocks.Count == 0) return;
        var windows = VisibleSnapshot;
        foreach (var dock in _secondaryDocks) dock.Update(windows, _foregroundWindow);
    }

    private void DisposeFeatures()
    {
        _secondaryTimer?.Stop();
        foreach (var dock in _secondaryDocks) dock.Close();
        _secondaryDocks.Clear();
        UnregisterHotkeys();
        _updateTimer?.Stop();
        _desktopTimer?.Stop();
        _updateCheck?.Cancel();
        _launcher?.Close();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
