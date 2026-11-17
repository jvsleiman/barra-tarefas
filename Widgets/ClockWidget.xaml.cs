using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

public partial class ClockWidget : DockWidget
{
    private readonly MonthCalendarFlyout _calendar;
    private string _lastRenderKey = string.Empty;

    public ClockWidget()
    {
        InitializeComponent();
        _calendar = new MonthCalendarFlyout(() => Culture, Report);
    }

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
            var (report, _) = await WeatherService.LoadAsync(Settings.WeatherCity, Settings.Language != AppLanguage.Turkish, load.Token);
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

    private void ClockButton_Click(object sender, RoutedEventArgs e) => _calendar.Toggle(ClockButton);
}
