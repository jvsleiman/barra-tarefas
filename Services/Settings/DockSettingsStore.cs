using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTaskbar.Models;

namespace OpenTaskbar.Services;

public sealed class DockSettingsStore
{
    private const string RemovedMemoryWidgetId = "Memory";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _path;
    private readonly AppLanguage _defaultLanguage;

    public DockSettingsStore(string? path = null, CultureInfo? defaultCulture = null)
    {
        _defaultLanguage = (defaultCulture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "tr" => AppLanguage.Turkish,
            "pt" => AppLanguage.Portuguese,
            _ => AppLanguage.English
        };
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenTaskbar",
            "settings.json");
    }

    public (DockSettings Settings, string? Error) Load()
    {
        if (!File.Exists(_path))
        {
            return (CreateDefaultSettings(), null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var settings = document.RootElement.Deserialize<DockSettings>(JsonOptions);
            // Older files without a language follow the same system-language rule as a first launch.
            if (settings is not null && !document.RootElement.TryGetProperty(nameof(DockSettings.Language), out _))
                settings = settings with { Language = _defaultLanguage };
            // Normalize the legacy enabled-only format before enforcing the installed subset invariant.
            if (settings is { InstalledWidgets: null })
                settings = settings with { InstalledWidgets = settings.Widgets ?? Array.Empty<string>() };
            // The standalone memory widget was removed in favor of System Meters; drop stale identifiers.
            if (settings is not null)
                settings = settings with
                {
                    Widgets = settings.Widgets?.Where(id => id != RemovedMemoryWidgetId).ToArray(),
                    InstalledWidgets = settings.InstalledWidgets?.Where(id => id != RemovedMemoryWidgetId).ToArray()
                };
            if (settings is not null && !document.RootElement.TryGetProperty(nameof(DockSettings.StyleVersion), out _))
                settings = MigrateToTileStyle(settings);
            if (settings is null || !IsValid(settings))
            {
                return (CreateDefaultSettings(), UiText.Get("ErrorDockSettingsInvalid"));
            }

            return (settings, null);
        }
        catch (JsonException)
        {
            return (CreateDefaultSettings(), UiText.Get("ErrorDockSettingsRead"));
        }
        catch (IOException)
        {
            return (CreateDefaultSettings(), UiText.Get("ErrorDockSettingsDisk"));
        }
        catch (UnauthorizedAccessException)
        {
            return (CreateDefaultSettings(), UiText.Get("ErrorDockSettingsAccess"));
        }
    }

    private DockSettings CreateDefaultSettings() => new(
        Language: _defaultLanguage,
        Widgets: DockSettings.DefaultWidgets,
        InstalledWidgets: DockSettings.DefaultWidgets);

    /// <summary>
    /// One-time move of pre-tile settings to the tile look: full-width bar, medium tiles, no magnification and the
    /// calendar/tray/clock cards. Pins, profiles, stacks and every other choice are kept.
    /// </summary>
    internal static DockSettings MigrateToTileStyle(DockSettings settings)
    {
        static IReadOnlyList<string> WithDefaults(IReadOnlyList<string>? ids) =>
            (ids ?? []).Concat(DockSettings.DefaultWidgets).Distinct(StringComparer.Ordinal).ToArray();
        return settings with
        {
            Layout = DockLayout.FullWidth,
            Size = DockSize.Medium,
            MagnificationEnabled = false,
            IconTiles = true,
            Widgets = WithDefaults(settings.Widgets),
            InstalledWidgets = WithDefaults(settings.InstalledWidgets),
            StyleVersion = DockSettings.CurrentStyleVersion
        };
    }

    public string? Save(DockSettings settings)
    {
        if (!IsValid(settings))
            return UiText.Get("ErrorSettingsValuesInvalid");
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is null)
            {
                return UiText.Get("ErrorDockSettingsPath");
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            return null;
        }
        catch (IOException)
        {
            return UiText.Get("ErrorDockSettingsSave");
        }
        catch (UnauthorizedAccessException)
        {
            return UiText.Get("ErrorDockSettingsWriteAccess");
        }
    }

    private static bool IsValid(DockSettings settings) => Enum.IsDefined(settings.Position)
        && Enum.IsDefined(settings.Theme) && Enum.IsDefined(settings.Language)
        && Enum.IsDefined(settings.AutoHide) && Enum.IsDefined(settings.Layout) && Enum.IsDefined(settings.Size)
        // Keep these ranges identical to the Widgets tab sliders.
        && settings.FocusMinutes is >= 1 and <= 90
        && settings.FocusBreakMinutes is >= 1 and <= 30
        && settings.FocusSessions is >= 1 and <= 8
        && double.IsFinite(settings.SurfaceOpacity) && settings.SurfaceOpacity is >= 0.6 and <= 1
        && double.IsFinite(settings.DockScale) && settings.DockScale is >= 0.75 and <= 1.5
        && settings.PinOrder?.Any(string.IsNullOrWhiteSpace) != true
        && settings.PinOrder?.Distinct(StringComparer.OrdinalIgnoreCase).Count() == settings.PinOrder?.Count
        && settings.Widgets?.Any(widget => !DockWidgetService.SupportedIds.Contains(widget)) != true
        && settings.Widgets?.Distinct(StringComparer.Ordinal).Count() == settings.Widgets?.Count
        && settings.InstalledWidgets?.Any(widget => !DockWidgetService.SupportedIds.Contains(widget)) != true
        && settings.InstalledWidgets?.Distinct(StringComparer.Ordinal).Count() == settings.InstalledWidgets?.Count
        && settings.Widgets?.Any(widget => settings.InstalledWidgets?.Contains(widget, StringComparer.Ordinal) != true) != true
        && Enum.IsDefined(settings.LauncherHotkey) && Enum.IsDefined(settings.DockHotkey)
        && Enum.IsDefined(settings.StartAction) && Enum.IsDefined(settings.StartPlacement) && Enum.IsDefined(settings.Edge)
        && Enum.IsDefined(settings.DateStyle)
        && (settings.StatsMeters & ~SystemMeters.All) == 0
        && settings.WeatherCity is { Length: <= 80 }
        && settings.StackFolders?.Any(string.IsNullOrWhiteSpace) != true
        && settings.WidgetOrder?.Any(string.IsNullOrWhiteSpace) != true
        && settings.WidgetOrder?.Distinct(StringComparer.OrdinalIgnoreCase).Count() == settings.WidgetOrder?.Count
        && settings.Profiles?.Any(profile => string.IsNullOrWhiteSpace(profile.Name) || profile.HiddenApps is null) != true
        && settings.Profiles?.Select(profile => profile.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).Count() == settings.Profiles?.Count
        && settings.AppGroups?.Any(group => string.IsNullOrWhiteSpace(group.Name) || group.Apps is null
            || group.Apps.Any(string.IsNullOrWhiteSpace) || !IsColorOrEmpty(group.Color)) != true
        && settings.QuickSettingsItems?.Any(item => !DockSettings.AllQuickSettingsItems.Contains(item)) != true
        && settings.QuickSettingsItems?.Distinct(StringComparer.Ordinal).Count() == settings.QuickSettingsItems?.Count
        && settings.AiUsageProviders?.Any(provider => !AiUsage.Providers.Contains(provider)) != true
        && Enum.IsDefined(settings.IndicatorStyle) && Enum.IsDefined(settings.StartIcon)
        && settings.StartIconPath is { Length: <= 400 }
        && double.IsFinite(settings.StartIconScale) && settings.StartIconScale is >= 0.6 and <= 1.6
        && double.IsFinite(settings.TileOpacity) && settings.TileOpacity is >= 0 and <= 0.5
        && double.IsFinite(settings.CornerRadius) && settings.CornerRadius is >= 0 and <= 24
        && new[] { settings.AccentColor, settings.BarColor, settings.TileColor, settings.WeekdayColor, settings.RunningDotColor }.All(IsColorOrEmpty);

    /// <summary>Empty (theme default) or "#RRGGBB".</summary>
    public static bool IsColorOrEmpty(string? color) => string.IsNullOrEmpty(color)
        || (color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit));

    public static bool Validate(DockSettings settings) => IsValid(settings);

    public static JsonSerializerOptions SerializerOptions => JsonOptions;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
