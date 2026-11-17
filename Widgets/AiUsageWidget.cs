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
            var providers = Settings.AiUsageProviders ?? AiUsage.Providers;
            var reports = await Task.Run(() => providers.Select(provider => provider == AiUsage.Codex
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
        foreach (var report in reports)
        {
            var title = CardParts.Row(CardParts.Icon("IconSparkles", 13, "UsageBarBrush"), CardParts.Text(report.Provider == AiUsage.Codex ? "ChatGPT" : "Claude", 12));
            ((TextBlock)title.Children[1]).Margin = new Thickness(5, 0, 0, 0);
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(title);
            if (!report.HasData)
            {
                body.Children.Add(CardParts.Text(UiText.Get("NoData"), 11, FontWeights.Normal, "TextSecondary"));
            }
            else
            {
                body.Children.Add(UsageRow(UiText.Get("UsageSession"), report.Session, now));
                body.Children.Add(UsageRow(UiText.Get("UsageWeekly"), report.Weekly, now));
            }

            var card = CardParts.Card(body);
            card.ToolTip = report.Provider == AiUsage.Codex ? UiText.Get("UsageCodexSource") : UiText.Get("UsageClaudeSource");
            _cards.Children.Add(card);
        }
    }

    private static StackPanel UsageRow(string label, UsageWindow? usage, DateTimeOffset now)
    {
        var name = CardParts.Text(label, 10, FontWeights.Normal, "TextSecondary");
        name.Width = 46;
        var (bar, fill) = CardParts.Progress(54);
        var value = CardParts.Text(string.Empty, 10, FontWeights.SemiBold);
        value.Margin = new Thickness(6, 0, 0, 0);
        var culture = UiText.CultureFor(UiText.Current.Language);
        if (usage?.Percent is { } percent)
        {
            CardParts.SetProgress(bar, fill, percent / 100);
            value.Text = $"{Math.Round(percent)}%";
        }
        else if (usage?.Tokens is { } tokens)
        {
            bar.Visibility = Visibility.Collapsed;
            value.Text = UiText.Format("UsageTokens", AiUsage.FormatTokens(tokens, culture));
            value.Margin = new Thickness(0);
        }
        else
        {
            bar.Visibility = Visibility.Collapsed;
            value.Text = "—";
        }

        var row = CardParts.Row(name, bar, value);
        if (usage?.ResetsAt is { } reset && AiUsage.FormatRemaining(reset, now) is { Length: > 0 } left)
        {
            var remaining = CardParts.Text(left, 10, FontWeights.Normal, "TextSecondary");
            remaining.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(remaining);
        }

        return row;
    }
}
