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
            if (IsActive) Render(reports, DateTimeOffset.Now);
        }
        finally
        {
            _reading = false;
        }
    }

    private void Render(IReadOnlyList<AiUsageReport> reports, DateTimeOffset now)
    {
        _cards.Children.Clear();
        var culture = UiText.CultureFor(UiText.Current.Language);
        foreach (var report in reports)
        {
            if (AiUsage.IsLinkOnly(report.Provider))
            {
                // No local usage data for these: a logo shortcut that opens the app.
                var label = CardParts.Text(report.Provider, 11);
                label.Margin = new Thickness(5, 0, 0, 0);
                var link = CardParts.Card(CardParts.Row(CardParts.Icon("IconSparkles", 16, "TextSecondary"), label), (_, _) => Report(AiUsage.OpenApp(report.Provider)));
                link.Padding = new Thickness(8, 0, 10, 0);
                link.ToolTip = $"{report.Provider} · {UiText.Get("NoUsageData")}";
                _cards.Children.Add(link);
                continue;
            }

            var name = report.Provider == AiUsage.Codex ? "Codex" : "Claude";
            var row = CardParts.Row(CardParts.Icon("IconSparkles", 16, "UsageBarBrush"));
            row.Children.Add(Gauge("5h", report.Session, culture));
            row.Children.Add(Gauge("7d", report.Weekly, culture));
            var card = CardParts.Card(row);
            card.Padding = new Thickness(8, 0, 8, 0);
            card.ToolTip = Details(name, report, now, culture);
            _cards.Children.Add(card);
        }
    }

    /// <summary>A small ring: the percentage (Codex) or a compact token count (Claude), labelled 5h / 7d.</summary>
    private static StackPanel Gauge(string label, UsageWindow? usage, System.Globalization.CultureInfo culture)
    {
        var ring = new RingMeter
        {
            Width = 28, Height = 28, StrokeThickness = 3, FontSize = 9,
            Value = usage?.Percent is { } percent ? percent / 100 : 0,
            Text = usage?.Percent is { } p ? $"{Math.Round(p)}" : usage?.Tokens is { } tokens ? AiUsage.FormatTokens(tokens, culture) : "–"
        };
        ring.SetResourceReference(RingMeter.ValueBrushProperty, "UsageBarBrush");
        ring.SetResourceReference(RingMeter.TrackBrushProperty, "WidgetTrack");
        ring.SetResourceReference(RingMeter.ForegroundProperty, "TextPrimary");
        var caption = CardParts.Text(label, 9, FontWeights.Normal, "TextSecondary");
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, -1, 0, 0);
        return new StackPanel { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { ring, caption } };
    }

    /// <summary>Everything the rings leave out: exact numbers, reset times and where the data comes from.</summary>
    private static string Details(string name, AiUsageReport report, DateTimeOffset now, System.Globalization.CultureInfo culture)
    {
        if (!report.HasData) return $"{name} · {UiText.Get("NoData")}";
        string Line(string label, UsageWindow? usage)
        {
            if (usage is null) return $"{label}: —";
            var value = usage.Percent is { } percent ? $"{Math.Round(percent)}%" : usage.Tokens is { } tokens ? UiText.Format("UsageTokens", AiUsage.FormatTokens(tokens, culture)) : "—";
            var reset = usage.ResetsAt is { } at && AiUsage.FormatRemaining(at, now) is { Length: > 0 } left ? $" · {UiText.Format("ResetsIn", left)}" : string.Empty;
            return $"{label}: {value}{reset}";
        }

        var source = report.Provider == AiUsage.Codex ? UiText.Get("UsageCodexSource") : UiText.Get("UsageClaudeSource");
        return $"{name}\n{Line(UiText.Get("UsageSession"), report.Session)}\n{Line(UiText.Get("UsageWeekly"), report.Weekly)}\n{source}";
    }
}