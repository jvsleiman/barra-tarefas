using System.Windows;
using System.Windows.Controls;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

/// <summary>
/// AI usage card in the reference style: a provider name, then "Sessão" and "Semanal" rows with a green bar, the
/// percentage (Codex) or tokens (Claude Code) and the time to reset. Local files only; refreshed every minute.
/// </summary>
public sealed class AiUsageWidget : DockWidget
{
    private readonly StackPanel _cards = CardParts.Row();
    private readonly DockFlyout _panel = new();
    private IReadOnlyList<AiUsageReport> _reports = [];
    private DateTimeOffset _updatedAt;
    private DateTime _nextRead;
    private bool _reading;

    public AiUsageWidget()
    {
        Content = _cards;
        UiText.Current.PropertyChanged += (_, _) => _nextRead = DateTime.MinValue;
    }

    protected override void OnActivated() => _nextRead = DateTime.MinValue;

    protected override void OnSettingsChanged() => _nextRead = DateTime.MinValue;

    protected override async void OnTick(DateTime now)
    {
        if (_reading || now < _nextRead) return;
        _reading = true;
        _nextRead = now.AddMinutes(1);
        try
        {
            var providers = Settings.AiUsageProviders ?? AiUsage.DefaultProviders;
            var reports = await Task.Run(() => providers.Select(provider => AiUsage.IsLinkOnly(provider) ? new AiUsageReport(provider, null, null) : provider == AiUsage.Codex
                ? AiUsage.ReadCodex(DateTimeOffset.Now)
                : AiUsage.ReadClaude(DateTimeOffset.Now)).ToList());
            if (IsActive)
            {
                _reports = reports;
                _updatedAt = DateTimeOffset.Now;
                Render(reports, _updatedAt);
            }
        }
        finally
        {
            _reading = false;
        }
    }

    // Each AI's own app icon (from the apps installed on this PC), so the logo is the real one and nothing is bundled.
    private static readonly Dictionary<string, string> AppIds = new()
    {
        [AiUsage.Codex] = "OpenAI.Codex_2p2nqsd0c76g0!App",
        [AiUsage.ChatGpt] = "OpenAI.Codex_2p2nqsd0c76g0!App",
        [AiUsage.Claude] = "Claude_pzs8sxrjxfjjc!Claude",
        [AiUsage.Grok] = "electron.app.Grok Bot"
    };

    private static readonly Dictionary<string, System.Windows.Media.Imaging.BitmapSource?> Logos = new();

    private static UIElement Logo(string provider)
    {
        if (!Logos.TryGetValue(provider, out var image))
        {
            image = AppIds.TryGetValue(provider, out var appId) ? ApplicationIconProvider.GetForShellItem($"shell:AppsFolder\\{appId}", 32, iconOnly: true) : null;
            Logos[provider] = image;
        }

        if (image is null) return CardParts.Icon("IconSparkles", 16, "TextSecondary");
        var picture = new Image { Source = image, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(picture, System.Windows.Media.BitmapScalingMode.HighQuality);
        return picture;
    }

    private void Render(IReadOnlyList<AiUsageReport> reports, DateTimeOffset now)
    {
        _cards.Children.Clear();
        var culture = UiText.CultureFor(UiText.Current.Language);
        var row = CardParts.Row();
        foreach (var report in reports)
        {
            var name = report.Provider switch { AiUsage.Codex => "Codex", AiUsage.Claude => "Claude", _ => report.Provider };
            // Logo + one thin ring (5-hour usage, falling back to the week); numbers live in the tooltip.
            var item = CardParts.Row(Logo(report.Provider));
            item.Margin = new Thickness(4, 0, 6, 0);
            item.Background = System.Windows.Media.Brushes.Transparent;
            if (!AiUsage.IsLinkOnly(report.Provider)) item.Children.Add(Ring((report.Session ?? report.Weekly)?.Percent));
            item.ToolTip = AiUsage.IsLinkOnly(report.Provider) ? $"{report.Provider} · {UiText.Get("NoUsageData")}" : Details(name, report, now, culture);
            if (AiUsage.IsLinkOnly(report.Provider))
            {
                item.Cursor = System.Windows.Input.Cursors.Hand;
                item.MouseLeftButtonUp += (_, _) => Report(AiUsage.OpenApp(report.Provider));
            }

            row.Children.Add(item);
        }

        Button? card = null;
        card = CardParts.Card(row, (_, _) => _panel.Toggle(card!, BuildPanel));
        card.Padding = new Thickness(6, 0, 4, 0);
        _cards.Children.Add(card);
    }

    /// <summary>Claude-style usage ring: ~20 px, 2.5 px stroke, dark track, blue arc clockwise from the top, no text.</summary>
    private static RingMeter Ring(double? percent)
    {
        var ring = new RingMeter
        {
            Width = 20, Height = 20, StrokeThickness = 2.5, Text = string.Empty, Margin = new Thickness(6, 0, 0, 0),
            Value = percent is { } p ? p / 100 : 0,
            TrackBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2D, 0x33)),
            ValueBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2F, 0x7B, 0xFF))
        };
        return ring;
    }
    protected override void OnDeactivated() => _panel.Close();

    // ---- Expanded panel (click on the card) ----

    private UIElement BuildPanel()
    {
        var culture = UiText.CultureFor(UiText.Current.Language);
        var now = DateTimeOffset.Now;
        var root = new StackPanel { Width = 300 };
        root.Children.Add(DockFlyout.Heading(UiText.Get("WidgetAiUsage")));
        foreach (var report in _reports)
        {
            var name = report.Provider switch { AiUsage.Codex => "Codex (ChatGPT)", AiUsage.Claude => "Claude Code", _ => report.Provider };
            var title = CardParts.Row(Logo(report.Provider), CardParts.Text(name, 13));
            ((TextBlock)title.Children[1]).Margin = new Thickness(8, 0, 0, 0);
            var section = new StackPanel { Margin = new Thickness(4, 6, 4, 12) };
            section.Children.Add(title);
            if (AiUsage.IsLinkOnly(report.Provider) || !report.HasData)
            {
                section.Children.Add(DockFlyout.Caption(UiText.Get(AiUsage.IsLinkOnly(report.Provider) ? "NoUsageData" : "NoData")));
                if (AiUsage.IsLinkOnly(report.Provider))
                {
                    var open = DockFlyout.FlatButton(new TextBlock { Text = UiText.Get("OpenApp"), FontWeight = FontWeights.SemiBold }, (_, _) =>
                    {
                        _panel.Close();
                        Report(AiUsage.OpenApp(report.Provider));
                    });
                    section.Children.Add(open);
                }
            }
            else
            {
                var rings = CardParts.Row(BigRing(UiText.Get("UsageSession5h"), report.Session, now), BigRing(UiText.Get("UsageWeekly7d"), report.Weekly, now));
                rings.Margin = new Thickness(0, 10, 0, 0);
                section.Children.Add(rings);
                if (report.Session?.Tokens is { } sessionTokens || report.Weekly?.Tokens is not null)
                    section.Children.Add(DockFlyout.Caption(UiText.Format("UsageTokenSummary",
                        report.Session?.Tokens is { } s ? AiUsage.FormatTokens(s, culture) : "—",
                        report.Weekly?.Tokens is { } w ? AiUsage.FormatTokens(w, culture) : "—")));
                if (report.Session?.Estimated == true) section.Children.Add(DockFlyout.Caption(UiText.Get("UsageEstimatedNote")));
                if (report.Daily is { Count: > 0 } daily) section.Children.Add(DailyChart(daily, now, culture));
            }

            root.Children.Add(section);
        }

        root.Children.Add(DockFlyout.Caption(UiText.Format("UpdatedAt", _updatedAt.ToString("t", culture))));
        return root;
    }

    /// <summary>A larger ring with the % inside, its label and the reset countdown.</summary>
    private static StackPanel BigRing(string label, UsageWindow? usage, DateTimeOffset now)
    {
        var ring = Ring(usage?.Percent);
        ring.Width = ring.Height = 52;
        ring.StrokeThickness = 4;
        ring.FontSize = 12;
        ring.Margin = new Thickness(0);
        ring.Text = usage?.Percent is { } percent ? $"{Math.Round(percent)}%" : "—";
        ring.SetResourceReference(RingMeter.ForegroundProperty, "TextPrimary");
        var panel = new StackPanel { Width = 130 };
        panel.Children.Add(ring);
        var caption = CardParts.Text(label, 11, FontWeights.SemiBold);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(caption);
        if (usage?.ResetsAt is { } reset && AiUsage.FormatRemaining(reset, now) is { Length: > 0 } left)
        {
            var renew = CardParts.Text(UiText.Format("ResetsIn", left), 11, FontWeights.Normal, "TextSecondary");
            renew.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(renew);
        }

        return panel;
    }

    /// <summary>Seven thin bars, one per day (oldest → today), scaled to the busiest day.</summary>
    private static UIElement DailyChart(IReadOnlyList<long> daily, DateTimeOffset now, System.Globalization.CultureInfo culture)
    {
        var max = Math.Max(1, daily.Max());
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        for (var index = 0; index < daily.Count; index++)
        {
            var day = now.ToLocalTime().Date.AddDays(index - daily.Count + 1);
            var bar = new Border
            {
                Width = 10, Height = Math.Max(2, 36 * daily[index] / (double)max), CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2F, 0x7B, 0xFF)) { Opacity = index == daily.Count - 1 ? 1 : 0.6 },
                ToolTip = $"{day.ToString("ddd d", culture)} · {UiText.Format("UsageTokens", AiUsage.FormatTokens(daily[index], culture))}"
            };
            var initial = CardParts.Text(day.ToString("ddd", culture)[..1].ToUpper(culture), 9, FontWeights.Normal, "TextSecondary");
            initial.HorizontalAlignment = HorizontalAlignment.Center;
            bars.Children.Add(new StackPanel { Width = 22, Children = { new Grid { Height = 36, Children = { bar } }, initial } });
        }

        return bars;
    }

    /// <summary>Everything the rings leave out: exact numbers, reset times and where the data comes from.</summary>
    private static string Details(string name, AiUsageReport report, DateTimeOffset now, System.Globalization.CultureInfo culture)
    {
        if (!report.HasData) return $"{name} · {UiText.Get("NoData")}";
        string Line(string label, UsageWindow? usage)
        {
            if (usage is null) return $"{label}: —";
            var value = usage.Percent is { } percent ? (usage.Estimated && usage.Tokens is { } t ? $"{AiUsage.FormatTokens(t, culture)} · {Math.Round(percent)}% {UiText.Get("OfBiggestBlock")}" : $"{Math.Round(percent)}%") : usage.Tokens is { } tokens ? UiText.Format("UsageTokens", AiUsage.FormatTokens(tokens, culture)) : "—";
            var reset = usage.ResetsAt is { } at && AiUsage.FormatRemaining(at, now) is { Length: > 0 } left ? $" · {UiText.Format("ResetsIn", left)}" : string.Empty;
            return $"{label}: {value}{reset}";
        }

        var source = report.Provider == AiUsage.Codex ? UiText.Get("UsageCodexSource") : UiText.Get("UsageClaudeSource");
        return $"{name}\n{Line(UiText.Get("UsageSession"), report.Session)}\n{Line(UiText.Get("UsageWeekly"), report.Weekly)}\n{source}";
    }
}