using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTaskbar.Widgets;

/// <summary>Building blocks for code-built cards so every widget shares the tile look.</summary>
internal static class CardParts
{
    public static Button Card(UIElement content, RoutedEventHandler? click = null)
    {
        var button = new Button { Content = content, Padding = new Thickness(10, 0, 12, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "WidgetCardButton");
        if (click is not null) button.Click += click;
        return button;
    }

    public static ContentControl Icon(string key, double size = 14, string brush = "TextPrimary")
    {
        var icon = new ContentControl { Content = Application.Current.TryFindResource(key), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(FrameworkElement.StyleProperty, "WidgetIcon");
        icon.SetResourceReference(Control.ForegroundProperty, brush);
        return icon;
    }

    public static TextBlock Text(string text = "", double size = 12, FontWeight? weight = null, string brush = "TextPrimary")
    {
        var block = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight ?? FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        block.SetResourceReference(TextBlock.FontFamilyProperty, "InterfaceFont");
        System.Windows.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    /// <summary>Thin rounded progress bar (green by default, like the reference usage card).</summary>
    public static (Grid Bar, Border Fill) Progress(double width, string brush = "UsageBarBrush")
    {
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "WidgetTrack");
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        var grid = new Grid { Width = width, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(track);
        grid.Children.Add(fill);
        return (grid, fill);
    }

    public static void SetProgress(Grid bar, Border fill, double fraction) =>
        fill.Width = Math.Round(bar.Width * Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1));

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    public static Brush? Brush(string key) => Application.Current.TryFindResource(key) as Brush;
}
