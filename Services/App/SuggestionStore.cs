using System.Globalization;
using System.IO;
using System.Text;

namespace OpenTaskbar.Services;

/// <summary>One suggestion: a Markdown task-list item ("- [ ] 2026-11-17 02:10 · Ideia · v0.3.2 — texto").</summary>
public sealed record Suggestion(int Index, bool Done, string Header, string Text)
{
    public string Display => $"{Header} — {Text}";
}

/// <summary>
/// João's suggestion list, kept as a plain Markdown file in %LOCALAPPDATA%\OpenTaskbar\sugestoes.md (outside the
/// app folder, so updates never touch it). Each item is a task-list line; extra lines of a multi-line text are
/// indented by two spaces, which Markdown renders as part of the same item.
/// </summary>
public sealed class SuggestionStore(string? path = null)
{
    public static readonly string[] Categories = ["Ideia", "Bug", "Visual"];
    private const string Title = "# Sugestões";
    private const string Separator = " — ";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: true);

    public string FilePath { get; } = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTaskbar", "sugestoes.md");

    /// <summary>Appends a new open item; returns null on success or the error message.</summary>
    public string? Add(string text, string category, string version, DateTime now)
    {
        var body = Normalize(text);
        if (body.Length == 0) return UiText.Get("SuggestionEmpty");
        var header = $"{now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {Clean(category)} · v{version}";
        var lines = body.Split('\n');
        var item = new StringBuilder($"- [ ] {header}{Separator}{lines[0]}");
        foreach (var line in lines.Skip(1)) item.Append('\n').Append("  ").Append(line);
        return Write(existing =>
        {
            var content = existing.Length == 0 ? Title + "\n\n" : existing.EndsWith('\n') ? existing : existing + "\n";
            return content + item + "\n";
        });
    }

    public IReadOnlyList<Suggestion> Load()
    {
        var items = new List<Suggestion>();
        var lines = ReadLines();
        foreach (var (start, count) in ItemRanges(lines))
        {
            var first = lines[start];
            var done = first.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase);
            var rest = first[6..];
            var split = rest.IndexOf(Separator, StringComparison.Ordinal);
            var header = split >= 0 ? rest[..split] : string.Empty;
            var text = split >= 0 ? rest[(split + Separator.Length)..] : rest;
            var continuation = lines.Skip(start + 1).Take(count - 1).Select(line => line.StartsWith("  ", StringComparison.Ordinal) ? line[2..] : line);
            items.Add(new Suggestion(items.Count, done, header, string.Join("\n", continuation.Prepend(text))));
        }

        return items;
    }

    public string? Toggle(int index) => Edit(index, (lines, start, _) =>
    {
        var first = lines[start];
        lines[start] = (first.StartsWith("- [ ] ", StringComparison.Ordinal) ? "- [x] " : "- [ ] ") + first[6..];
    });

    public string? Delete(int index) => Edit(index, (lines, start, count) => lines.RemoveRange(start, count));

    public string ReadAll() => File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : string.Empty;

    private string? Edit(int index, Action<List<string>, int, int> change) => Write(existing =>
    {
        var lines = Split(existing);
        var ranges = ItemRanges(lines).ToList();
        if (index < 0 || index >= ranges.Count) return existing;
        change(lines, ranges[index].Start, ranges[index].Count);
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    });

    /// <summary>Each item: its "- [ ]"/"- [x]" line plus the indented continuation lines that follow it.</summary>
    private static IEnumerable<(int Start, int Count)> ItemRanges(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!IsItem(lines[index])) continue;
            var end = index + 1;
            while (end < lines.Count && lines[end].StartsWith("  ", StringComparison.Ordinal) && !IsItem(lines[end])) end++;
            yield return (index, end - index);
            index = end - 1;
        }
    }

    private static bool IsItem(string line) =>
        line.StartsWith("- [ ] ", StringComparison.Ordinal) || line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase);

    private List<string> ReadLines() => Split(ReadAll());

    private static List<string> Split(string content) => content.Replace("\r\n", "\n").Split('\n').ToList();

    // Trailing spaces, Windows line breaks and blank lines inside the text would break the list item.
    private static string Normalize(string text) => string.Join("\n", text.Replace("\r\n", "\n").Replace('\r', '\n')
        .Split('\n').Select(line => line.TrimEnd()).SkipWhile(line => line.Length == 0).Reverse().SkipWhile(line => line.Length == 0).Reverse()
        .Select(line => line.Length == 0 ? "." : line));

    private static string Clean(string category) => Categories.Contains(category) ? category : Categories[0];

    private string? Write(Func<string, string> update)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var next = update(ReadAll().Replace("\r\n", "\n")).Replace("\n", "\r\n");
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, next, Utf8);
            File.Move(temporary, FilePath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"Suggestions file: {ex.Message}");
            return UiText.Get("SuggestionSaveError");
        }
    }
}
