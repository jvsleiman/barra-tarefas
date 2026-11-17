using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private int _crashHandled;
    public ApplicationPreferences Preferences { get; private set; } = new(new DockSettingsStore());
    public bool IsPreview { get; private set; }
    public bool IsInspection { get; private set; }
    public bool OpenSettingsOnStartup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.Initialize();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        IsPreview = e.Args.Contains("--preview", StringComparer.Ordinal);
        IsInspection = e.Args.Contains("--inspect", StringComparer.Ordinal);
        OpenSettingsOnStartup = e.Args.Contains("--settings", StringComparer.Ordinal);
        if (IsPreview)
            Preferences = new ApplicationPreferences(new DockSettingsStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenTaskbar", "preview-settings.json")));
        UiText.Current.SetLanguage(Preferences.Current.Language);
        ThemeManager.ApplySettings(Preferences.Current);
        _instanceMutex = new Mutex(initiallyOwned: true,
            name: IsPreview ? "Local\\OpenTaskbar.Preview" : "Local\\OpenTaskbar.Singleton", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(UiText.Get("AlreadyRunning"), "OpenTaskbar", MessageBoxButton.OK, MessageBoxImage.Information);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var error = (MainWindow as OpenTaskbar.MainWindow)?.RestoreWindowsTaskbar();
        if (error is not null)
        {
            AppLog.Error(error);
            e.ApplicationExitCode = 1;
        }
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        AppLog.Info("OpenTaskbar exited.");
        AppLog.Shutdown();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        var error = (MainWindow as OpenTaskbar.MainWindow)?.RestoreWindowsTaskbar();
        if (error is not null) AppLog.Error(error);
        base.OnSessionEnding(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleFatalError(e.Exception, "UI thread");
        Shutdown(1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // The process is terminating; restore from this thread because the UI thread may be gone.
        HandleFatalError(e.ExceptionObject as Exception, "background thread");
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Warning($"Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }

    private void HandleFatalError(Exception? exception, string origin)
    {
        if (Interlocked.Exchange(ref _crashHandled, 1) != 0) return;
        AppLog.Error($"Unhandled exception on {origin}: {exception}");
        if (!TaskbarReplacementSession.HasActiveSession)
        {
            Trace.Flush();
            return;
        }

        var error = TaskbarReplacementSession.EmergencyRestore();
        if (error is not null) AppLog.Error($"Emergency taskbar restore failed: {error}");
        else AppLog.Info("Windows taskbar restored after an unhandled exception.");
        Trace.Flush();
    }
}
