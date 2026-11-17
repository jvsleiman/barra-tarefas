using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>
/// Search-first launcher opened by the global hotkey, the dock menu, or the Start button. It takes focus while open and
/// hides as soon as it loses it.
/// </summary>
public sealed class LauncherWindow : Window
{
    private const int ResultLimit = 9;
    private static readonly LauncherCatalog Catalog = new();
    private readonly Dictionary<string, BitmapSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _reportError;
    private readonly TextBox _query;
    private readonly ListBox _results;
    private readonly TextBlock _hint;

    public LauncherWindow(Action<string> reportError)
    {
        _reportError = reportError;
        Title = UiText.Get("LauncherTitle");
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SetResourceReference(FontFamilyProperty, "InterfaceFont");

        _query = new TextBox
        {
            FontSize = 20,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _query.SetResourceReference(ForegroundProperty, "TextPrimary");
        _query.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimary");
        _query.TextChanged += (_, _) => UpdateResults();
        _query.PreviewKeyDown += Query_PreviewKeyDown;
        System.Windows.Automation.AutomationProperties.SetName(_query, UiText.Get("LauncherPlaceholder"));

        _hint = new TextBlock { FontSize = 20, IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

        var searchIcon = new ContentControl { Content = FindResource("IconSearch"), Width = 20, Height = 20 };
        searchIcon.SetResourceReference(StyleProperty, "WidgetIcon");
        searchIcon.SetResourceReference(ForegroundProperty, "TextSecondary");

        var queryRow = new Grid { Height = 44 };
        queryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        queryRow.ColumnDefinitions.Add(new ColumnDefinition());
        queryRow.Children.Add(searchIcon);
        Grid.SetColumn(_hint, 1);
        Grid.SetColumn(_query, 1);
        queryRow.Children.Add(_hint);
        queryRow.Children.Add(_query);

        _results = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 8, 0, 0),
            Focusable = false
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Disabled);
        _results.ItemContainerStyle = CreateItemStyle();
        _results.MouseUp += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Left && _results.SelectedItem is not null) Run(_results.SelectedItem);
        };

        var body = new StackPanel();
        body.Children.Add(queryRow);
        body.Children.Add(_results);
        var frame = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(24),
            Child = body,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.4 }
        };
        frame.SetResourceReference(Border.BackgroundProperty, "MenuMaterial");
        frame.SetResourceReference(Border.BorderBrushProperty, "MenuStroke");
        frame.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        Content = frame;
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            Hide();
        };
        UiText.Current.PropertyChanged += (_, _) => _hint.Text = UiText.Get("LauncherPlaceholder");
        _hint.Text = UiText.Get("LauncherPlaceholder");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // A tool window stays out of Alt+Tab and out of the dock's running-app list.
        var handle = new WindowInteropHelper(this).Handle;
        MainWindow.ExcludeFromTaskSwitcher(handle);
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new nint((style | 0x80) & ~0x40000));
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.18;
        _query.Text = string.Empty;
        UpdateResults();
        Show();
        Activate();
        _query.Focus();
        Keyboard.Focus(_query);
        _ = RefreshCatalogAsync();
    }

    private async Task RefreshCatalogAsync()
    {
        await Catalog.RefreshAsync();
        if (IsVisible) UpdateResults();
    }

    private void UpdateResults()
    {
        _hint.Visibility = _query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Portuguese uses the English Windows-settings names (the catalog has Turkish and English only).
        var english = UiText.Current.Language != AppLanguage.Turkish;
        var results = Catalog.Search(_query.Text, english, ResultLimit);
        _results.ItemsSource = results.Select(entry => new ListBoxItem { Content = BuildRow(entry), Tag = entry }).ToList();
        _results.SelectedIndex = results.Count > 0 ? 0 : -1;
        _results.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildRow(LauncherEntry entry)
    {
        var icon = new Image { Width = 28, Height = 28, Margin = new Thickness(0, 0, 12, 0) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var glyph = entry.Kind switch
        {
            LauncherKind.Setting => "IconSettings",
            LauncherKind.Calculator => "IconZap",
            LauncherKind.FileSearch => "IconFolder",
            LauncherKind.WebSearch => "IconSearch",
            _ => null
        };
        UIElement iconElement = icon;
        if (glyph is not null)
        {
            var vector = new ContentControl { Content = FindResource(glyph), Width = 20, Height = 20, Margin = new Thickness(4, 0, 16, 0) };
            vector.SetResourceReference(StyleProperty, "WidgetIcon");
            vector.SetResourceReference(ForegroundProperty, "TextPrimary");
            iconElement = vector;
        }
        else if (_icons.TryGetValue(entry.Target, out var cached))
        {
            icon.Source = cached;
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                var image = ApplicationIconProvider.GetForShellItem(entry.Target, 64, iconOnly: entry.Kind == LauncherKind.App);
                _icons[entry.Target] = image;
                icon.Source = image;
            });
        }

        var title = entry.Kind switch
        {
            LauncherKind.FileSearch => UiText.Format("LauncherSearchFiles", entry.Name),
            LauncherKind.WebSearch => UiText.Format("LauncherSearchWeb", entry.Name),
            _ => entry.Name
        };
        var detail = entry.Kind switch
        {
            LauncherKind.App => UiText.Get("LauncherApp"),
            LauncherKind.Setting => UiText.Get("LauncherSetting"),
            LauncherKind.Recent => entry.Detail ?? UiText.Get("LauncherRecent"),
            LauncherKind.Calculator => UiText.Format("LauncherCopyResult", entry.Detail ?? string.Empty),
            _ => null
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail is not null)
        {
            var caption = new TextBlock { Text = detail, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            text.Children.Add(caption);
        }

        var row = new DockPanel();
        row.Children.Add(iconElement);
        row.Children.Add(text);
        return row;
    }

    private static Style CreateItemStyle()
    {
        var template = new ControlTemplate(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "Surface" };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 7, 10, 7));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceSelectedStrong"), "Surface"));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceHover"), "Surface"));
        template.Triggers.Add(hover);
        template.Triggers.Add(selected);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(TemplateProperty, template));
        style.Setters.Add(new Setter(FocusableProperty, false));
        return style;
    }

    private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var count = _results.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                _results.SelectedIndex = (_results.SelectedIndex + 1) % count;
                _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                _results.SelectedIndex = (_results.SelectedIndex - 1 + count) % count;
                _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter when _results.SelectedItem is ListBoxItem { Tag: LauncherEntry entry }:
                e.Handled = true;
                Run(entry);
                break;
        }
    }

    private void Run(object selected)
    {
        var entry = selected as LauncherEntry ?? (selected as ListBoxItem)?.Tag as LauncherEntry;
        if (entry is null) return;
        Hide();
        if (LauncherCatalog.Launch(entry) is { } error) _reportError(error);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
