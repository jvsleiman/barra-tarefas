using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

/// <summary>
/// Calendar card: red weekday abbreviation over a big day number, then an orange accent bar with the next event.
/// No calendar source is wired yet, so the event part shows "no events"; <see cref="SetNextEvent"/> is the hook
/// for a future provider (Outlook, Google, ICS…).
/// </summary>
public sealed class CalendarWidget : DockWidget
{
    private readonly Button _button;
    private readonly TextBlock _weekday;
    private readonly TextBlock _day;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly ShellSurfaceToggle _panelToggle = new();
    private bool? _panelOpenAtPress;
    private (string Title, DateTime Start)? _nextEvent;
    private string _renderKey = string.Empty;

    public CalendarWidget()
    {
        _weekday = Text(9.5, FontWeights.Bold, "WeekdayAccent");
        _day = Text(20, FontWeights.Bold, "TextPrimary");
        _day.Margin = new Thickness(0, -3, 0, -2);
        var date = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 26 };
        date.Children.Add(_weekday);
        date.Children.Add(_day);

        var accent = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(10, 7, 8, 7) };
        accent.SetResourceReference(Border.BackgroundProperty, "EventAccent");

        _title = Text(12, FontWeights.SemiBold, "TextPrimary");
        _title.MaxWidth = 170;
        _subtitle = Text(11, FontWeights.Normal, "TextSecondary");
        _subtitle.MaxWidth = 170;
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(_title);
        details.Children.Add(_subtitle);

        var layout = new StackPanel { Orientation = Orientation.Horizontal };
        layout.Children.Add(date);
        layout.Children.Add(accent);
        layout.Children.Add(details);

        _button = new Button { Content = layout, Padding = new Thickness(10, 0, 12, 0) };
        _button.SetResourceReference(StyleProperty, "WidgetCardButton");
        // Win+N panel as a toggle: the dock never takes focus, so a second click would otherwise reopen it.
        _button.PreviewMouseLeftButtonDown += (_, _) =>
            _panelOpenAtPress = _panelToggle.WasOpen(DateTime.UtcNow, StartMenuState.IsNotificationCenterForeground());
        _button.Click += (_, _) =>
        {
            var wasOpen = _panelOpenAtPress ?? StartMenuState.IsNotificationCenterForeground();
            _panelOpenAtPress = null;
            if (wasOpen)
            {
                _panelToggle.Reset();
                if (StartMenuState.IsNotificationCenterForeground()) Report(ShellShortcuts.SendChord(0x1B));
                return;
            }

            Report(ShellShortcuts.OpenNotificationCenter());
            _panelToggle.RecordOpen(DateTime.UtcNow);
        };
        Content = _button;
        UiText.Current.PropertyChanged += (_, _) =>
        {
            _renderKey = string.Empty;
            if (IsActive) OnTick(DateTime.Now);
        };
    }

    /// <summary>Shows the next event (title + "01:45 · in 1h 14m"); null returns to the empty state.</summary>
    public void SetNextEvent(string? title, DateTime start)
    {
        _nextEvent = string.IsNullOrWhiteSpace(title) ? null : (title, start);
        _renderKey = string.Empty;
        if (IsActive) OnTick(DateTime.Now);
    }

    protected override void OnActivated() => OnTick(DateTime.Now);

    protected override void OnSettingsChanged()
    {
        _renderKey = string.Empty;
        if (IsActive) OnTick(DateTime.Now);
    }

    protected override void OnTick(DateTime now)
    {
        var key = $"{now:yyyyMMddHHmm}|{Settings.Language}|{_nextEvent}";
        if (key == _renderKey) return;
        _renderKey = key;
        var culture = UiText.CultureFor(Settings.Language);
        _weekday.Text = culture.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek).TrimEnd('.').ToUpper(culture);
        _day.Text = now.Day.ToString(culture);
        _button.ToolTip = now.ToString("D", culture);

        if (_nextEvent is { } next && next.Start > now)
        {
            _title.Text = next.Title;
            var remaining = next.Start - now;
            var until = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                : $"{Math.Max(1, remaining.Minutes)}m";
            _subtitle.Text = $"{next.Start.ToString("t", culture)} · {UiText.Format("EventIn", until)}";
            _subtitle.Visibility = Visibility.Visible;
        }
        else
        {
            _title.Text = UiText.Get("NoEvents");
            _subtitle.Text = UiText.Get("NoEventsHint");
            _subtitle.Visibility = Visibility.Visible;
        }
    }

    private static TextBlock Text(double size, FontWeight weight, string brush)
    {
        var text = new TextBlock
        {
            FontSize = size, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        text.SetResourceReference(TextBlock.FontFamilyProperty, "InterfaceFont");
        return text;
    }
}
