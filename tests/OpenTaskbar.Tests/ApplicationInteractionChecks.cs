using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTaskbar.Models;
using OpenTaskbar.Services;
using OpenTaskbar.Widgets;

internal static class ApplicationInteractionChecks
{
    public static int Run(Application application)
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }

        var first = new DockApp("Foreground test", "foreground-test.exe") { WindowCount = 1, IsForeground = true };
        var second = new DockApp("Background test", "background-test.exe") { WindowCount = 1 };
        var firstButton = new Button { Style = (Style)application.FindResource("DockButton"), DataContext = first, Content = "A" };
        var secondButton = new Button { Style = (Style)application.FindResource("DockButton"), DataContext = second, Content = "B" };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { firstButton, secondButton } };
        var strip = new StackPanel();
        using var coordinator = new WidgetCoordinator(strip, Dispatcher.CurrentDispatcher, _ => { });
        var tray = strip.Children.OfType<TrayWidget>().Single();
        string? closePath = null;
        string? terminatePath = null;
        coordinator.CloseApplicationRequested += path => closePath = path;
        coordinator.TerminateApplicationRequested += path => terminatePath = path;
        var trayApp = new TrayApp("Background app test", "tray-test.exe", null);
        var content = (StackPanel)tray.BuildContent([trayApp]);
        var row = content.Children.OfType<DockPanel>().Single();
        var terminate = row.Children.OfType<Button>().First();
        var open = row.Children.OfType<Button>().Last();
        var menu = open.ContextMenu ?? throw new InvalidOperationException("Tray app actions must be available.");
        Check(AutomationProperties.GetName(terminate).Contains(trayApp.Name)
            && AutomationProperties.GetName(terminate).Contains(UiText.Get("Terminate")),
            "The visible end-process action must name its application and operation.");
        Check(terminate.Content is ContentControl icon && icon.Style == application.FindResource("WidgetIcon"),
            "The end-process geometry must use the existing icon template.");
        Check(menu.Items.OfType<MenuItem>().Select(item => item.Header).SequenceEqual(
            new[] { UiText.Get("CloseApplication"), UiText.Get("Terminate") }),
            "Tray apps must expose normal close and confirmed end-process actions.");
        terminate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(terminatePath == trayApp.ExecutablePath && closePath is null,
            "The visible tray action must route only the selected path to process termination.");
        terminatePath = null;
        menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(closePath == trayApp.ExecutablePath && terminatePath is null,
            "Normal close must stay separate from forced termination.");
        menu.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(terminatePath == trayApp.ExecutablePath,
            "The context menu must route to the same termination flow as the visible action.");
        var empty = (StackPanel)tray.BuildContent([]);
        Check(!empty.Children.OfType<DockPanel>().Any()
            && empty.Children.OfType<TextBlock>().Any(text => text.Text == UiText.Get("TrayEmpty")),
            "An empty tray must show its empty state without application actions.");

        var panel = new StackPanel { Children = { buttons, content } };
        panel.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
        panel.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "InterfaceFont");
        var window = new Window
        {
            Title = "OpenTaskbar interaction checks",
            Content = panel,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        try
        {
            window.Show();
            window.Activate();
            Check(secondButton.Focus(), "The regression check must give the background button actual keyboard focus.");
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeManager.Apply(theme);
                panel.Background = (Brush)application.FindResource("SurfacePopup");
                Flush();
                var firstSurface = (Border)firstButton.Template.FindName("Surface", firstButton);
                var secondSurface = (Border)secondButton.Template.FindName("Surface", secondButton);
                Check(firstSurface.Background == application.FindResource("SurfaceSelectedStrong"),
                    "The foreground application must have a background without keyboard focus.");
                Check(secondButton.IsKeyboardFocused && secondSurface.Background == application.FindResource("DockTile"),
                    "Keyboard focus on a background app must keep the plain tile, not the active background.");
                Check(secondSurface.BorderBrush == application.FindResource("StrokeFocus"),
                    "Keyboard navigation must retain a visible focus outline.");
                first.IsForeground = false;
                second.IsForeground = true;
                Flush();
                Check(firstSurface.Background == application.FindResource("DockTile")
                    && secondSurface.Background == application.FindResource("SurfaceSelectedStrong"),
                    "Switching applications must move the foreground background immediately.");
                second.IsForeground = false;
                Flush();
                Check(secondButton.IsKeyboardFocused && secondSurface.Background == application.FindResource("DockTile"),
                    "Minimizing the selected app must clear its background even while its button retains focus.");
                first.IsForeground = true;
                Flush();
                var directory = Path.Combine(Path.GetTempPath(), "OpenTaskbarInteractionChecks");
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, $"{theme}.png"));
                encoder.Save(output);
            }

            panel.Children.Remove(content);
            var flyout = new OpenTaskbar.DockFlyout();
            try
            {
                flyout.Show(firstButton, content, animate: false);
                Flush();
                menu.PlacementTarget = open;
                menu.IsOpen = true;
                Flush();
                Check(menu.IsOpen && menu.IsVisible,
                    "The tray context menu must remain available inside the real dock flyout.");
            }
            finally
            {
                menu.IsOpen = false;
                flyout.Close();
            }

            // Toggle regression: a press on the anchor light-dismisses the flyout (IsOpen=false) and its click arrives
            // ~60 ms later while the close animation is still running; that click must not reopen it.
            var toggleFlyout = new OpenTaskbar.DockFlyout();
            var opened = 0;
            toggleFlyout.Toggle(secondButton, () => { opened++; return new TextBlock { Text = "x" }; }, animate: true);
            Flush();
            Check(toggleFlyout.IsOpen && opened == 1, "The first click on a dock tile must open its flyout.");
            toggleFlyout.Close(); // what light dismiss does on mouse down
            toggleFlyout.Toggle(secondButton, () => { opened++; return new TextBlock { Text = "x" }; }, animate: true);
            Flush();
            Check(!toggleFlyout.IsOpen && opened == 1, "The click that dismissed a flyout must not reopen it mid-fade.");
            toggleFlyout.Toggle(secondButton, () => { opened++; return new TextBlock { Text = "x" }; }, animate: true);
            Flush();
            Check(toggleFlyout.IsOpen && opened == 2, "The next click on the tile must open the flyout again.");
            toggleFlyout.Toggle(secondButton, () => { opened++; return new TextBlock { Text = "x" }; }, animate: true);
            Flush();
            Check(!toggleFlyout.IsOpen, "Clicking the tile of an open flyout must close it.");

            // Start / shell panel toggle: open now, or seen open within the grace period, counts as open at press.
            var shell = new ShellSurfaceToggle();
            var t0 = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
            Check(!shell.WasOpen(t0, openNow: false) && shell.WasOpen(t0, openNow: true), "A closed shell panel must read as closed.");
            shell.RecordOpen(t0);
            Check(shell.WasOpen(t0.AddMilliseconds(300), openNow: false), "Start that dismissed itself on the same press must count as open.");
            Check(!shell.WasOpen(t0 + ShellSurfaceToggle.Grace + TimeSpan.FromMilliseconds(1), openNow: false),
                "After the grace period a closed Start must open again on click.");
            shell.Reset();
            Check(!shell.WasOpen(t0.AddMilliseconds(10), openNow: false), "Closing Start must reset the toggle.");
        }
        finally
        {
            window.Close();
            ThemeManager.Apply(AppTheme.Dark);
        }

        return checks;

        void Flush()
        {
            application.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            panel.UpdateLayout();
        }
    }

    private static bool IsTransparent(Brush brush) => brush is SolidColorBrush solid && solid.Color.A == 0;
}
