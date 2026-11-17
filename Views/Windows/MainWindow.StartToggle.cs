using System.Windows.Input;
using System.Windows.Threading;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>Start tile as a real toggle: a second click closes the Windows Start menu instead of reopening it.</summary>
public partial class MainWindow
{
    private const ushort VirtualKeyEscape = 0x1B;
    private readonly ShellSurfaceToggle _startToggle = new();
    private DispatcherTimer? _startWatch;
    private bool? _startOpenAtPress;

    private void StartButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _startOpenAtPress = _startToggle.WasOpen(DateTime.UtcNow, StartMenuState.IsStartMenuForeground());

    /// <summary>Returns true when this click should close Start (it was open when the button was pressed).</summary>
    private bool CloseStartMenuIfOpen()
    {
        var wasOpen = _startOpenAtPress ?? StartMenuState.IsStartMenuForeground();
        _startOpenAtPress = null;
        if (!wasOpen) return false;
        _startToggle.Reset();
        // Start keeps the foreground (the dock never activates), so Escape goes to Start and dismisses it.
        if (StartMenuState.IsStartMenuForeground()) ShowStatusIfError(ShellShortcuts.SendChord(VirtualKeyEscape));
        return true;
    }

    /// <summary>After opening Start, remember while it stays open so a later press can tell it was open.</summary>
    private void WatchStartMenu()
    {
        _startToggle.RecordOpen(DateTime.UtcNow);
        _startWatch ??= CreateStartWatch();
        _startWatch.Start();
    }

    private DispatcherTimer CreateStartWatch()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
        var missed = 0;
        timer.Tick += (_, _) =>
        {
            if (StartMenuState.IsStartMenuForeground())
            {
                missed = 0;
                _startToggle.RecordOpen(DateTime.UtcNow);
            }
            else if (++missed > 15)
            {
                missed = 0;
                timer.Stop();
            }
        };
        return timer;
    }

    private void ShowStatusIfError(string? error)
    {
        if (error is not null) ShowStatus(error);
    }
}
