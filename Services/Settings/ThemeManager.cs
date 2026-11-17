using System.Windows;
using System.Windows.Media;
using OpenTaskbar.Models;

namespace OpenTaskbar.Services;

/// <summary>
/// Loads the theme palette and layers the user's materials on top (a merged dictionary added after the palette, so
/// its keys win): surface opacity, icon tiles and the 0.4.0 personal colors. Empty colors keep the theme's own.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _palette;
    private static readonly ResourceDictionary Materials = new();
    private static DockSettings _personal = new();
    private static double _opacity = 0.9;

    public static void Apply(AppTheme theme, double opacity = 0.9)
    {
        var source = $"/OpenTaskbar;component/Design/Palettes/{theme}.xaml";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_palette?.Source?.OriginalString != source)
        {
            var next = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
            dictionaries.Remove(Materials);
            if (_palette is not null) dictionaries.Remove(_palette);
            dictionaries.Add(next);
            _palette = next;
        }
        if (!dictionaries.Contains(Materials)) dictionaries.Add(Materials);
        SetSurfaceOpacity(opacity);
        ApplyPersonalColors();
    }

    /// <summary>Applies all appearance settings at once (startup, save and live preview).</summary>
    public static void ApplySettings(DockSettings settings)
    {
        _personal = settings;
        Apply(settings.Theme, settings.SurfaceOpacity);
        SetIconTiles(settings.IconTiles);
    }

    /// <summary>Tiles off keeps the layout but makes the tile fill and stroke transparent (classic icon row).</summary>
    public static void SetIconTiles(bool enabled)
    {
        foreach (var key in new[] { "DockTileStroke", "StartTileBackground" })
        {
            if (enabled) Materials.Remove(key);
            else Materials[key] = Brushes.Transparent;
        }

        if (enabled) ApplyPersonalColors();
        else Materials["DockTile"] = Brushes.Transparent;
    }

    public static void SetSurfaceOpacity(double opacity)
    {
        if (!double.IsFinite(opacity) || opacity is < 0.6 or > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        if (_palette is null) throw new InvalidOperationException("The theme must be initialized before its materials.");
        _opacity = opacity;
        var alpha = (byte)Math.Round(opacity * 255);
        var settings = ((SolidColorBrush)_palette["SettingsBackground"]).Color;
        settings.A = alpha;
        var settingsBrush = new SolidColorBrush(settings);
        settingsBrush.Freeze();
        Materials["SettingsGlassMaterial"] = settingsBrush;
        var barColor = ParseColor(_personal.BarColor);
        foreach (var key in new[] { "DockMaterial", "MenuMaterial" })
        {
            var material = ((LinearGradientBrush)_palette[key]).Clone();
            foreach (var stop in material.GradientStops)
            {
                // A custom bar color replaces the dock's tint; menus keep the theme's material.
                var color = key == "DockMaterial" && barColor is { } custom ? custom : stop.Color;
                color.A = alpha;
                stop.Color = color;
            }
            material.Freeze();
            Materials[key] = material;
        }
    }

    private static void ApplyPersonalColors()
    {
        if (_palette is null) return;
        SetColor("WidgetAccent", _personal.AccentColor);
        SetColor("AccentBrush", _personal.AccentColor);
        SetColor("WeekdayAccent", _personal.WeekdayColor);
        SetColor("RunningDotBrush", _personal.RunningDotColor);
        var bar = _personal.IndicatorStyle == RunningIndicatorStyle.Bar;
        Materials["RunningIndicatorWidth"] = bar ? 14.0 : 4.0;
        Materials["RunningIndicatorHeight"] = bar ? 3.0 : 4.0;
        Materials["RunningIndicatorActiveWidth"] = bar ? 22.0 : 10.0;
        var defaultTile = ((SolidColorBrush)_palette["DockTile"]).Color;
        if (!_personal.IconTiles) return;
        var tile = TileColor(_personal.TileColor, _personal.TileOpacity, defaultTile);
        Materials["DockTile"] = Frozen(tile);
        Materials["DockTileHover"] = Frozen(tile with { A = (byte)Math.Min(255, tile.A * 2 + 6) });
    }

    /// <summary>The tile fill: chosen (or theme) RGB at the chosen opacity.</summary>
    public static Color TileColor(string chosen, double opacity, Color themeDefault)
    {
        var rgb = ParseColor(chosen) ?? themeDefault;
        return Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 0.5) * 255), rgb.R, rgb.G, rgb.B);
    }

    /// <summary>"#RRGGBB" → color; empty or invalid → null (use the theme's color).</summary>
    public static Color? ParseColor(string? hex)
    {
        if (!DockSettingsStore.IsColorOrEmpty(hex) || string.IsNullOrEmpty(hex)) return null;
        return Color.FromRgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    }

    private static void SetColor(string key, string hex)
    {
        if (ParseColor(hex) is { } color) Materials[key] = Frozen(color);
        else Materials.Remove(key);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
