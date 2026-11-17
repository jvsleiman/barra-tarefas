using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

public partial class ClockWidget : DockWidget
{
    private readonly DockFlyout _calendar = new();
    private string _lastRenderKey = string.Empty;
    private DateOnly _shownMonth;

    public ClockWidget() => InitializeComponent();

    private CultureInfo Culture => UiText.CultureFor(Settings.Language);

    protected override void OnActivated()
    {
        _lastRenderKey = string.Empty;
        OnTick(DateTime.Now);
    }

    protected override void OnSettingsChanged()
    {
        _lastRenderKey = string.Empty;
        if (IsActive) OnTick(DateTime.Now);
    }

protected override void OnDeactivated()
    {
        _calendar.Close();
        _weatherLoad?.Cancel();
    }

    // ---- Weather under the time (shares Open-Meteo with the Weather widget; refreshed every 30 min) ----

    private CancellationTokenSource? _weatherLoad;
    private DateTime _nextWeatherLoad;
    private string _weatherKey = string.Empty;

    private void UpdateWeather(DateTime now)
    {
        if (!Settings.ClockShowWeather)
        {
            WeatherRow.Visibility = Visibility.Collapsed;
            return;
        }

        var key = $"{Settings.WeatherCity.Trim()}|{Settings.Language}";
        if (key != _weatherKey)
        {
            _weatherKey = key;
            _nextWeatherLoad = DateTime.MinValue;
        }

        if (now < _nextWeatherLoad) return;
        _nextWeatherLoad = now.AddMinutes(2); // retry delay until a load succeeds
        _ = LoadWeatherAsync(key);
    }

    private async Task LoadWeatherAsync(string key)
    {
        _weatherLoad?.Cancel();
        var load = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _weatherLoad = load;
        try
        {
            var (report, _) = await WeatherService.LoadAsync(Settings.WeatherCity, Settings.Language == AppLanguage.English, load.Token);
            if (!IsActive || load.IsCancellationRequested || key != _weatherKey || report is null) return;
            var (icon, _) = WeatherService.Describe(report.Code);
            WeatherIcon.Content = TryFindResource(icon);
            WeatherText.Text = $"{Math.Round(report.Temperature).ToString(Culture)}°";
            WeatherRow.Visibility = Settings.ClockShowWeather ? Visibility.Visible : Visibility.Collapsed;
            _nextWeatherLoad = DateTime.Now.AddMinutes(30);
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override void OnTick(DateTime now)
    {
        UpdateWeather(now);
        var culture = Culture;
        var timeFormat = Settings.Clock24Hour
            ? (Settings.ClockShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (Settings.ClockShowSeconds ? "hh:mm:ss tt" : "hh:mm tt");
        var timeString = now.ToString(timeFormat, culture);
        var renderKey = $"{timeString}|{Settings.DateStyle}|{Settings.Language}";
        if (renderKey != _lastRenderKey)
        {
            _lastRenderKey = renderKey;
            TimeText.Text = timeString;
            switch (Settings.DateStyle)
            {
                case ClockDateStyle.DayAndShortDate:
                    DateColumn.Visibility = Visibility.Visible;
                    DayOfWeekText.Visibility = Visibility.Visible;
                    DayOfWeekText.Text = now.ToString("dddd", culture).ToUpper(culture);
                    DateText.Text = now.ToString(Settings.Language == AppLanguage.English ? "MMM d" : "d MMM", culture).Replace(".", string.Empty);
                    break;
                case ClockDateStyle.ShortDateOnly:
                    DateColumn.Visibility = Visibility.Visible;
                    DayOfWeekText.Visibility = Visibility.Collapsed;
                    DateText.Text = now.ToString(Settings.Language == AppLanguage.English ? "MMM d, yyyy" : "d MMM yyyy", culture);
                    break;
                case ClockDateStyle.NumericDate:
                    DateColumn.Visibility = Visibility.Visible;
                    DayOfWeekText.Visibility = Visibility.Collapsed;
                    DateText.Text = now.ToString("d", culture);
                    break;
                case ClockDateStyle.Hidden:
                    DateColumn.Visibility = Visibility.Collapsed;
                    break;
            }
            ClockButton.ToolTip = now.ToString("D", culture);
        }
    }

    private void ClockButton_Click(object sender, RoutedEventArgs e)
    {
        _shownMonth = DateOnly.FromDateTime(DateTime.Today);
        _shownMonth = new DateOnly(_shownMonth.Year, _shownMonth.Month, 1);
        var root = new ContentControl();
        root.Content = BuildCalendar(root);
        _calendar.Toggle(ClockButton, () => root);
    }

    /// <summary>Month view with the user's first day of week; arrows rebuild it in place.</summary>
    private UIElement BuildCalendar(ContentControl host)
    {
        var culture = Culture;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var root = new StackPanel { Width = 280 };
        root.Children.Add(new TextBlock
        {
            Text = DateTime.Today.ToString("D", culture), FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(4, 0, 4, 10)
        });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var previous = NavButton("IconChevronLeft", -1, host);
        var next = NavButton("IconChevronNext", 1, host);
        DockPanel.SetDock(previous, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        header.Children.Add(previous);
        header.Children.Add(next);
        var title = _shownMonth.ToString("MMMM yyyy", culture);
        header.Children.Add(new TextBlock
        {
            Text = culture.TextInfo.ToTitleCase(title), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(header);

        var grid = new UniformGrid { Columns = 7 };
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        for (var index = 0; index < 7; index++)
        {
            var day = (DayOfWeek)(((int)firstDay + index) % 7);
            var name = DockFlyout.Caption(culture.DateTimeFormat.GetShortestDayName(day));
            name.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(name);
        }

        var offset = ((int)_shownMonth.DayOfWeek - (int)firstDay + 7) % 7;
        var start = _shownMonth.AddDays(-offset);
        for (var cell = 0; cell < 42; cell++)
        {
            var date = start.AddDays(cell);
            var label = new TextBlock
            {
                Text = date.Day.ToString(culture), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var border = new Border { Width = 32, Height = 30, CornerRadius = new CornerRadius(15), Child = label, Margin = new Thickness(1) };
            if (date == today)
            {
                border.SetResourceReference(Border.BackgroundProperty, "WidgetAccent");
                label.SetResourceReference(TextBlock.ForegroundProperty, "WidgetAccentText");
                label.FontWeight = FontWeights.SemiBold;
            }
            else if (date.Month != _shownMonth.Month)
            {
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                label.Opacity = 0.6;
            }

            grid.Children.Add(border);
        }

        root.Children.Add(grid);
        var notifications = DockFlyout.FlatButton(new TextBlock { Text = UiText.Get("OpenNotifications"), FontWeight = FontWeights.SemiBold }, (_, _) =>
        {
            _calendar.Close();
            Report(ShellShortcuts.OpenNotificationCenter());
        });
        notifications.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(notifications);
        return root;
    }

    private Button NavButton(string icon, int months, ContentControl host)
    {
        var glyph = new ContentControl { Content = FindResource(icon), Width = 14, Height = 14 };
        glyph.SetResourceReference(StyleProperty, "WidgetIcon");
        glyph.SetResourceReference(ForegroundProperty, "TextPrimary");
        var button = new Button { Content = glyph };
        button.SetResourceReference(StyleProperty, "WidgetIconButton");
        var key = months < 0 ? "PreviousMonth" : "NextMonth";
        button.ToolTip = UiText.Get(key);
        System.Windows.Automation.AutomationProperties.SetName(button, UiText.Get(key));
        button.Click += (_, _) =>
        {
            _shownMonth = _shownMonth.AddMonths(months);
            host.Content = BuildCalendar(host);
        };
        return button;
    }
}
