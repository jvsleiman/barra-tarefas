using System.Windows;
using System.Windows.Media;
using OpenTaskbar.Models;

namespace OpenTaskbar.Services;

public static class ThemeManager
{
    private static ResourceDictionary? _palette;
    private static readonly ResourceDictionary Materials = new();
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
    }

    /// <summary>Tiles off keeps the layout but makes the tile fill and stroke transparent (classic icon row).</summary>
    public static void SetIconTiles(bool enabled)
    {
        foreach (var key in new[] { "DockTile", "DockTileStroke", "StartTileBackground" })
        {
            if (enabled) Materials.Remove(key);
            else Materials[key] = Brushes.Transparent;
        }    }

    public static void SetSurfaceOpacity(double opacity)
    {
        if (!double.IsFinite(opacity) || opacity is < 0.6 or > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        if (_palette is null) throw new InvalidOperationException("The theme must be initialized before its materials.");
        var alpha = (byte)Math.Round(opacity * 255);
        var settings = ((SolidColorBrush)_palette["SettingsBackground"]).Color;
        settings.A = alpha;
        var settingsBrush = new SolidColorBrush(settings);
        settingsBrush.Freeze();
        Materials["SettingsGlassMaterial"] = settingsBrush;
        foreach (var key in new[] { "DockMaterial", "MenuMaterial" })
        {
            var material = ((LinearGradientBrush)_palette[key]).Clone();
            foreach (var stop in material.GradientStops)
            {
                var color = stop.Color;
                color.A = alpha;
                stop.Color = color;
            }
            material.Freeze();
            Materials[key] = material;
        }
    }
}
