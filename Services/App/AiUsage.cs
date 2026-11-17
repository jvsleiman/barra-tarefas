using System.IO;
using System.Text.Json;

namespace OpenTaskbar.Services;

/// <summary>One usage window: a real percentage (Codex) or a token count (Claude Code), plus when it resets.</summary>
public sealed record UsageWindow(double? Percent, long? Tokens, DateTimeOffset? ResetsAt);

public sealed record AiUsageReport(string Provider, UsageWindow? Session, UsageWindow? Weekly)
{
    public bool HasData => Session is not null || Weekly is not null;
}

/// <summary>
/// AI usage from local files only — no tokens, logins or web scraping:
/// • Codex CLI logs (%USERPROFILE%\.codex\sessions\**\*.jsonl) carry rate_limits events with real used_percent,
///   window length and reset time; the newest event wins.
/// • Claude Code logs (%USERPROFILE%\.claude\projects\**\*.jsonl) carry per-message token usage; like ccusage, the
///   current 5-hour block and the last 7 days are summed. No limit is stored there, so these show tokens, not %.
/// </summary>
public static class AiUsage
{
    public const string Codex = "Codex";
    public const string Claude = "Claude";
    public const string ChatGpt = "ChatGPT";
    public const string Grok = "Grok";
    public static readonly IReadOnlyList<string> Providers = [Codex, Claude, ChatGpt, Grok];
    public static readonly IReadOnlyList<string> DefaultProviders = [Codex, Claude];

    /// <summary>
    /// ChatGPT (desktop app) and Grok keep no readable usage data on the PC (checked: only app files, no limits), so they
    /// are shortcuts: the logo opens the app, or the website when the app isn't installed.
    /// </summary>
    public static bool IsLinkOnly(string provider) => provider is ChatGpt or Grok;

    public static string? OpenApp(string provider)
    {
        var (appId, site) = provider == Grok ? ("electron.app.Grok Bot", "https://grok.com") : ("OpenAI.Codex_2p2nqsd0c76g0!App", "https://chatgpt.com");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ShellShortcuts.OpenUri(site);
        }
    }

    public static AiUsageReport ReadCodex(DateTimeOffset now, string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
        foreach (var file in NewestFiles(root, 12))
        {
            var lines = ReadLinesShared(file);
            for (var index = lines.Count - 1; index >= 0; index--)
            {
                if (!lines[index].Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
                if (ParseCodexLine(lines[index], now) is { HasData: true } report) return report;
            }
        }

        return new AiUsageReport(Codex, null, null);
    }

    /// <summary>One Codex log line → session (≤ 6 h window) and weekly (≥ 1 day window) usage.</summary>
    public static AiUsageReport? ParseCodexLine(string line, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var limits = root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("rate_limits", out var nested) ? nested
                : root.TryGetProperty("rate_limits", out var direct) ? direct : default;
            if (limits.ValueKind != JsonValueKind.Object) return null;
            UsageWindow? session = null, weekly = null;
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) continue;
                if (!window.TryGetProperty("used_percent", out var used) || used.ValueKind != JsonValueKind.Number) continue;
                var minutes = window.TryGetProperty("window_minutes", out var length) && length.ValueKind == JsonValueKind.Number ? length.GetDouble() : 0;
                DateTimeOffset? resets = window.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(at.GetInt64()) : null;
                // After the reset the logged percentage is stale: the window starts again at 0.
                var percent = resets is { } reset && reset <= now ? 0 : used.GetDouble();
                var usage = new UsageWindow(percent, null, resets is { } r && r > now ? r : null);
                if (minutes is > 0 and <= 360) session ??= usage;
                else if (minutes >= 1440) weekly ??= usage;
            }

            return new AiUsageReport(Codex, session, weekly);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static AiUsageReport ReadClaude(DateTimeOffset now, string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        var since = now.AddDays(-7);
        var lines = NewestFiles(root, 400).Where(file => File.GetLastWriteTimeUtc(file) >= since.UtcDateTime).SelectMany(ReadLinesShared);
        return SumClaude(lines, now);
    }

    /// <summary>Claude Code usage lines → tokens in the active 5-hour block and in the last 7 days.</summary>
    public static AiUsageReport SumClaude(IEnumerable<string> lines, DateTimeOffset now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(DateTimeOffset At, long Tokens)>();
        foreach (var line in lines)
        {
            if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("timestamp", out var stamp) || !DateTimeOffset.TryParse(stamp.GetString(), out var at)) continue;
                if (!root.TryGetProperty("message", out var message) || !message.TryGetProperty("usage", out var usage)) continue;
                // Streaming writes the same message more than once; count each message/request pair once.
                var key = $"{(message.TryGetProperty("id", out var id) ? id.GetString() : null)}|{(root.TryGetProperty("requestId", out var request) ? request.GetString() : null)}";
                if (key != "|" && !seen.Add(key)) continue;
                long Count(string name) => usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
                var tokens = Count("input_tokens") + Count("output_tokens") + Count("cache_creation_input_tokens") + Count("cache_read_input_tokens");
                entries.Add((at, tokens));
            }
            catch (JsonException)
            {
            }
        }

        if (entries.Count == 0) return new AiUsageReport(Claude, null, null);
        entries.Sort((a, b) => a.At.CompareTo(b.At));

        // ccusage-style blocks: a block starts at the hour of its first message and lasts 5 hours.
        DateTimeOffset? blockStart = null;
        foreach (var (at, _) in entries)
        {
            if (blockStart is null || at >= blockStart.Value.AddHours(5))
                blockStart = new DateTimeOffset(at.Year, at.Month, at.Day, at.Hour, 0, 0, at.Offset);
        }

        UsageWindow? session = null;
        if (blockStart is { } start && now < start.AddHours(5))
            session = new UsageWindow(null, entries.Where(entry => entry.At >= start).Sum(entry => entry.Tokens), start.AddHours(5));
        var weekTokens = entries.Where(entry => entry.At >= now.AddDays(-7)).Sum(entry => entry.Tokens);
        var weekly = weekTokens > 0 ? new UsageWindow(null, weekTokens, null) : null;
        return new AiUsageReport(Claude, session, weekly);
    }

    /// <summary>"em 2h 10m" style countdown and "1,2M" style token counts for the card.</summary>
    public static string FormatRemaining(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var left = resetsAt - now;
        if (left <= TimeSpan.Zero) return string.Empty;
        return left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours}h" : left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes:00}m" : $"{Math.Max(1, left.Minutes)}m";
    }

    public static string FormatTokens(long tokens, System.Globalization.CultureInfo culture) => tokens switch
    {
        >= 1_000_000 => (tokens / 1_000_000d).ToString("0.#", culture) + "M",
        >= 1_000 => (tokens / 1_000d).ToString("0", culture) + "k",
        _ => tokens.ToString(culture)
    };

    private static IEnumerable<string> NewestFiles(string root, int count)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc).Take(count).Select(file => file.FullName).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // The CLIs keep writing these logs; open with sharing so the dock never blocks or fails on them.
    private static List<string> ReadLinesShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
