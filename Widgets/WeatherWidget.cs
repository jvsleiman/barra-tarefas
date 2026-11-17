using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

/// <summary>Current temperature and conditions; the flyout shows a short forecast.</summary>
public sealed class WeatherWidget : DockWidget
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(2);
    private readonly ContentControl _icon;
    private readonly TextBlock _temperature;
    private readonly Button _card;
    private readonly DockFlyout _flyout = new();
    private WeatherReport? _report;
    private string? _error;
    private DateTime _loadedAt = DateTime.MinValue;
    private string? _loadedFor;
    private CancellationTokenSource? _loading;

    public WeatherWidget()
    {
        _icon = new ContentControl { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) };
        _icon.SetResourceReference(StyleProperty, "WidgetIcon");
        _icon.SetResourceReference(ForegroundProperty, "TextPrimary");
        _temperature = new TextBlock { FontSize = 15 };
        _temperature.SetResourceReference(StyleProperty, "WidgetText");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(_icon);
        content.Children.Add(_temperature);
        _card = new Button { Content = content, Padding = new Thickness(10, 0, 12, 0) };
        _card.SetResourceReference(StyleProperty, "WidgetCardButton");
        _card.Click += (_, _) => _flyout.Toggle(_card, BuildFlyout);
        Content = _card;
        HasData = false;
        UiText.Current.PropertyChanged += (_, _) => Render();
    }

    private string LocationKey => $"{Settings.WeatherCity.Trim()}|{Settings.Language}";

    protected override void OnActivated() => _ = LoadAsync();

    protected override void OnDeactivated()
    {
        _loading?.Cancel();
        _flyout.Close();
    }

    protected override void OnSettingsChanged()
    {
        if (IsActive && _loadedFor is not null && _loadedFor != LocationKey) _ = LoadAsync();
    }

    protected override void OnTick(DateTime now)
    {
        if (now - _loadedAt > (_report is null ? RetryInterval : RefreshInterval) && _loading is null) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;
        _loadedAt = DateTime.Now;
        var key = LocationKey;
        var (report, error) = await WeatherService.LoadAsync(Settings.WeatherCity, Settings.Language != AppLanguage.Turkish, loading.Token);
        if (!ReferenceEquals(_loading, loading)) return;
        _loading = null;
        if (!IsActive) return;
        // Keep the last good report through brief network failures.
        if (report is not null || _loadedFor != key) _report = report;
        // Without a city, missing location access just hides the widget instead of raising an error.
        if (error is not null && _loadedFor != key && !string.IsNullOrWhiteSpace(Settings.WeatherCity)) Report(error);
        _error = _report is null ? error : null;
        _loadedFor = key;
        Render();
    }

    private void Render()
    {
        // A failed load stays visible with its cause instead of silently hiding the widget.
        HasData = _report is not null || _error is not null;
        if (_report is not { } report)
        {
            _icon.Content = null;
            _temperature.Text = UiText.Get("WeatherUnavailable");
            _card.ToolTip = _error;
            System.Windows.Automation.AutomationProperties.SetName(_card, $"{UiText.Get("Weather")}: {_error}");
            return;
        }
        var (icon, text) = WeatherService.Describe(report.Code);
        _icon.Content = FindResource(icon);
        _temperature.Text = FormatDegrees(report.Temperature);
        _card.ToolTip = string.IsNullOrEmpty(report.Place) ? UiText.Get(text) : $"{report.Place} · {UiText.Get(text)}";
        System.Windows.Automation.AutomationProperties.SetName(_card, $"{UiText.Get("Weather")}: {_temperature.Text}, {_card.ToolTip}");
    }

    private static string FormatDegrees(double value) => $"{Math.Round(value).ToString(CultureInfo.CurrentCulture)}°";

    private UIElement BuildFlyout()
    {
        var container = new Border
        {
            Width = 320,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12)
        };

        if (_report is not { } report)
        {
            var emptyRoot = new StackPanel();
            emptyRoot.Children.Add(DockFlyout.Heading(UiText.Get("Weather")));
            emptyRoot.Children.Add(DockFlyout.Caption(_error ?? UiText.Get("WeatherUnavailable")));
            container.Child = emptyRoot;
            return container;
        }

        container.Background = WeatherService.GetConditionBackground(report.Code);

        var root = new StackPanel();
        var (icon, text) = WeatherService.Describe(report.Code);

        // Header: Place name & condition description
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var conditionText = new TextBlock
        {
            Text = UiText.Get(text),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        conditionText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        DockPanel.SetDock(conditionText, Dock.Right);
        header.Children.Add(conditionText);

        var placeHeading = new TextBlock
        {
            Text = string.IsNullOrEmpty(report.Place) ? UiText.Get("Weather") : report.Place,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        placeHeading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        header.Children.Add(placeHeading);
        root.Children.Add(header);

        // Current weather row
        var now = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 2, 10) };
        var bigIcon = new ContentControl
        {
            Content = FindResource(icon),
            Width = 44,
            Height = 44,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        bigIcon.SetResourceReference(StyleProperty, "WidgetIcon");
        bigIcon.SetResourceReference(ForegroundProperty, "TextPrimary");

        var summary = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var tempText = new TextBlock
        {
            Text = FormatDegrees(report.Temperature),
            FontSize = 32,
            FontWeight = FontWeights.Light,
            Margin = new Thickness(0, 0, 0, 2)
        };
        tempText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        summary.Children.Add(tempText);

        string todayRange = string.Empty;
        if (report.Days.Count > 0)
        {
            todayRange = $" · {FormatDegrees(report.Days[0].Max)} / {FormatDegrees(report.Days[0].Min)}";
        }
        var feelsText = new TextBlock
        {
            Text = $"{UiText.Format("WeatherFeelsLike", FormatDegrees(report.ApparentTemperature))}{todayRange}",
            FontSize = 12
        };
        feelsText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        summary.Children.Add(feelsText);

        now.Children.Add(bigIcon);
        now.Children.Add(summary);
        root.Children.Add(now);

        // Micro metrics cards: Humidity, Wind
        var metricsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };

        var humidityCard = new Border
        {
            Background = (Brush)FindResource("SurfaceSelected"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 4, 0)
        };
        var humidityPanel = new StackPanel();
        var humLabel = new TextBlock { Text = UiText.Get("WeatherHumidity"), FontSize = 11 };
        humLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var humVal = new TextBlock { Text = $"%{Math.Round(report.Humidity)}", FontSize = 13, FontWeight = FontWeights.SemiBold };
        humVal.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        humidityPanel.Children.Add(humLabel);
        humidityPanel.Children.Add(humVal);
        humidityCard.Child = humidityPanel;
        metricsGrid.Children.Add(humidityCard);

        var windCard = new Border
        {
            Background = (Brush)FindResource("SurfaceSelected"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(4, 0, 0, 0)
        };
        var windPanel = new StackPanel();
        var windLabel = new TextBlock { Text = UiText.Get("WeatherWind"), FontSize = 11 };
        windLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var windVal = new TextBlock { Text = $"{Math.Round(report.WindSpeed)} km/s", FontSize = 13, FontWeight = FontWeights.SemiBold };
        windVal.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        windPanel.Children.Add(windLabel);
        windPanel.Children.Add(windVal);
        windCard.Child = windPanel;
        metricsGrid.Children.Add(windCard);

        root.Children.Add(metricsGrid);

        // Divider
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 2, 0, 10)
        };
        divider.SetResourceReference(Border.BackgroundProperty, "MenuDivider");
        root.Children.Add(divider);

        // Next 3 days forecast heading
        var forecastHeading = new TextBlock
        {
            Text = UiText.Get("WeatherForecast3Days"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        forecastHeading.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        root.Children.Add(forecastHeading);

        // Next 3 days forecast cards
        var culture = UiText.CultureFor(Settings.Language);
        var forecastDays = report.Days.Skip(1).Take(3).ToList();
        var daysGrid = new UniformGrid { Columns = Math.Max(1, forecastDays.Count), Margin = new Thickness(0, 0, 0, 6) };

        foreach (var day in forecastDays)
        {
            var dayCard = new Border
            {
                Background = (Brush)FindResource("SurfaceSelected"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 8, 6, 8),
                Margin = new Thickness(3, 0, 3, 0)
            };

            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var dayTitle = new TextBlock
            {
                Text = day.Date.ToString("ddd", culture).ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            dayTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            column.Children.Add(dayTitle);

            var (dayIconKey, dayTextKey) = WeatherService.Describe(day.Code);
            var dayIcon = new ContentControl
            {
                Content = FindResource(dayIconKey),
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 6, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            dayIcon.SetResourceReference(StyleProperty, "WidgetIcon");
            dayIcon.SetResourceReference(ForegroundProperty, "TextPrimary");
            column.Children.Add(dayIcon);

            var dayDesc = new TextBlock
            {
                Text = UiText.Get(dayTextKey),
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            dayDesc.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            column.Children.Add(dayDesc);

            var tempPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var maxText = new TextBlock
            {
                Text = FormatDegrees(day.Max),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 4, 0)
            };
            maxText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var minText = new TextBlock
            {
                Text = FormatDegrees(day.Min),
                FontSize = 12
            };
            minText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            tempPanel.Children.Add(maxText);
            tempPanel.Children.Add(minText);
            column.Children.Add(tempPanel);

            dayCard.Child = column;
            daysGrid.Children.Add(dayCard);
        }

        root.Children.Add(daysGrid);

        var footer = new TextBlock
        {
            Text = "Open-Meteo",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        root.Children.Add(footer);

        container.Child = root;
        return container;
    }
}
