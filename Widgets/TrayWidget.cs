using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

/// <summary>Stands in for the hidden notification area: lists running background apps that own a tray icon.</summary>
public sealed class TrayWidget : DockWidget
{
    private readonly Button _button;
    private readonly DockFlyout _flyout = new();

    public event Action<string>? CloseApplicationRequested;
    public event Action<string>? TerminateApplicationRequested;

    public TrayWidget()
    {
        var glyph = new ContentControl { Content = FindResource("IconChevronUp"), Width = 16, Height = 16 };
        glyph.SetResourceReference(StyleProperty, "WidgetIcon");
        glyph.SetResourceReference(ForegroundProperty, "TextPrimary");
        // A square tile like the app icons; it opens the background-apps flyout.
        _button = new Button { Content = glyph, Padding = new Thickness(0), Width = 44 };
        _button.SetResourceReference(StyleProperty, "WidgetCardButton");
        _button.Click += Button_Click;
        void Localize()
        {
            _button.ToolTip = UiText.Get("TrayApps");
            System.Windows.Automation.AutomationProperties.SetName(_button, UiText.Get("TrayApps"));
        }

        Localize();
        UiText.Current.PropertyChanged += (_, _) => Localize();
        Content = _button;
    }

    protected override void OnDeactivated() => _flyout.Close();

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        if (_flyout.IsOpen || _flyout.RecentlyClosed(_button))
        {
            _flyout.Close();
            return;
        }

        var apps = await Task.Run(TrayIcons.RunningApps);
        if (!IsActive) return;
        _flyout.Show(_button, BuildContent(apps));
    }

    internal UIElement BuildContent(IReadOnlyList<TrayApp> apps)
    {
        var root = new StackPanel { Width = 280 };
        root.Children.Add(DockFlyout.Heading(UiText.Get("TrayApps")));
        if (apps.Count == 0) root.Children.Add(DockFlyout.Caption(UiText.Get("TrayEmpty")));
        foreach (var app in apps)
        {
            var image = new Image { Source = app.Icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var row = new DockPanel();
            row.Children.Add(image);
            row.Children.Add(new TextBlock { Text = app.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var open = DockFlyout.FlatButton(row, (_, _) =>
            {
                _flyout.Close();
                Report(TrayIcons.Open(app));
            }, app.Name);
            var menu = new ContextMenu();
            var closeItem = new MenuItem { Header = UiText.Get("CloseApplication"), Icon = FindResource("IconClose") };
            closeItem.Click += (_, _) =>
            {
                _flyout.Close();
                CloseApplicationRequested?.Invoke(app.ExecutablePath);
            };
            var terminateItem = new MenuItem { Header = UiText.Get("Terminate"), Icon = FindResource("IconTerminate") };
            terminateItem.SetResourceReference(StyleProperty, "DestructiveMenuItem");
            terminateItem.Click += (_, _) =>
            {
                _flyout.Close();
                TerminateApplicationRequested?.Invoke(app.ExecutablePath);
            };
            menu.Items.Add(closeItem);
            menu.Items.Add(terminateItem);
            open.ContextMenu = menu;

            // Tray apps may keep running after WM_CLOSE; force quit uses the dock's existing confirmation flow.
            var quitIcon = new ContentControl
            {
                Content = FindResource("IconTerminate"), Width = 16, Height = 16
            };
            quitIcon.SetResourceReference(StyleProperty, "WidgetIcon");
            quitIcon.SetResourceReference(ForegroundProperty, "TextPrimary");
            var quit = DockFlyout.FlatButton(quitIcon, (_, _) =>
            {
                _flyout.Close();
                TerminateApplicationRequested?.Invoke(app.ExecutablePath);
            }, $"{app.Name}: {UiText.Get("Terminate")}");
            var appRow = new DockPanel();
            DockPanel.SetDock(quit, Dock.Right);
            appRow.Children.Add(quit);
            appRow.Children.Add(open);
            root.Children.Add(appRow);
        }

        var settings = DockFlyout.FlatButton(new TextBlock { Text = UiText.Get("TraySettings"), FontWeight = FontWeights.SemiBold }, (_, _) =>
        {
            _flyout.Close();
            Report(ShellShortcuts.OpenUri("ms-settings:taskbar"));
        });
        settings.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(settings);
        root.Children.Add(DockFlyout.Caption(UiText.Get("TrayNote")));
        return root;
    }
}
