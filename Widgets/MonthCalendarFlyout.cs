using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

/// <summary>
/// Month calendar flyout shared by the clock and calendar cards. It is a DockFlyout, so a second click on the
/// card closes it (unlike the Windows Win+N panel, whose state the dock cannot observe reliably).
/// </summary>
public sealed class MonthCalendarFlyout(Func<CultureInfo> culture, Action<string?> report)
{
    private readonly DockFlyout _flyout = new();
    private DateOnly _shownMonth;

    public bool IsOpen => _flyout.IsOpen;

    public void Toggle(UIElement anchor)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _shownMonth = new DateOnly(today.Year, today.Month, 1);
        _flyout.Toggle(anchor, () =>
        {
            var host = new ContentControl();
            host.Content = Build(host);
            return host;
        });
    }

    public void Close() => _flyout.Close();

    /// <summary>Month view with the user's first day of week; arrows rebuild it in place.</summary>
    private UIElement Build(ContentControl host)
    {
        var culture1 = culture();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var root = new StackPanel { Width = 280 };
        root.Children.Add(new TextBlock
        {
            Text = DateTime.Today.ToString("D", culture1), FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(4, 0, 4, 10)
        });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var previous = NavButton("IconChevronLeft", -1, host);
        var next = NavButton("IconChevronNext", 1, host);
        DockPanel.SetDock(previous, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        header.Children.Add(previous);
        header.Children.Add(next);
        var title = _shownMonth.ToString("MMMM yyyy", culture1);
        header.Children.Add(new TextBlock
        {
            Text = culture1.TextInfo.ToTitleCase(title), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(header);

        var grid = new UniformGrid { Columns = 7 };
        var firstDay = culture1.DateTimeFormat.FirstDayOfWeek;
        for (var index = 0; index < 7; index++)
        {
            var day = (DayOfWeek)(((int)firstDay + index) % 7);
            var name = DockFlyout.Caption(culture1.DateTimeFormat.GetShortestDayName(day));
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
                Text = date.Day.ToString(culture1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
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
            _flyout.Close();
            report(ShellShortcuts.OpenNotificationCenter());
        });
        notifications.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(notifications);
        return root;
    }

    private Button NavButton(string icon, int months, ContentControl host)
    {
        var glyph = new ContentControl { Content = Application.Current.FindResource(icon), Width = 14, Height = 14 };
        glyph.SetResourceReference(FrameworkElement.StyleProperty, "WidgetIcon");
        glyph.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        var button = new Button { Content = glyph };
        button.SetResourceReference(FrameworkElement.StyleProperty, "WidgetIconButton");
        var key = months < 0 ? "PreviousMonth" : "NextMonth";
        button.ToolTip = UiText.Get(key);
        System.Windows.Automation.AutomationProperties.SetName(button, UiText.Get(key));
        button.Click += (_, _) =>
        {
            _shownMonth = _shownMonth.AddMonths(months);
            host.Content = Build(host);
        };
        return button;
    }
}
