using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>0.4.0 personalization applied to the main dock: Start icon, corner radius and app titles.</summary>
public partial class MainWindow
{
    private UIElement? _startCustomContent;
    private string? _appliedStartKey;

    private void ApplyPersonalization()
    {
        var settings = _preferences.Current;
        ApplyStartIcon(settings);
        var radius = settings.CornerRadius;
        DockFrame.CornerRadius = new CornerRadius(radius);
        DockInnerBevel.CornerRadius = new CornerRadius(Math.Max(0, radius - 1));
        // Icon + title shows each app's name next to its icon; the tile then grows to fit the text.
        Application.Current.Resources["AppTitleVisibility"] = settings.ShowAppTitles ? Visibility.Visible : Visibility.Collapsed;
        Application.Current.Resources["DockAppButtonWidth"] = settings.ShowAppTitles ? double.NaN : Profile.Cell;
    }

    /// <summary>Grey four-square logo (default), the blue Windows logo, a custom image or the word "Iniciar".</summary>
    private void ApplyStartIcon(DockSettings settings)
    {
        var key = $"{settings.StartIcon}|{settings.StartIconPath}|{settings.StartIconScale}|{settings.Language}";
        if (key == _appliedStartKey) return;
        _appliedStartKey = key;
        if (StartLogo.Parent is not Grid host) return;
        if (_startCustomContent is not null) host.Children.Remove(_startCustomContent);
        _startCustomContent = null;
        StartLogo.Visibility = Visibility.Visible;
        StartButton.ClearValue(WidthProperty);
        var size = Math.Round(18 * settings.StartIconScale);
        StartLogo.Width = StartLogo.Height = size;
        StartLogo.ClearValue(Shape.FillProperty);
        StartLogo.SetResourceReference(Shape.FillProperty, "WindowsLogoBrush");

        switch (settings.StartIcon)
        {
            case StartIconStyle.WindowsLogo:
                StartLogo.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
                break;
            case StartIconStyle.CustomImage when LoadStartImage(settings.StartIconPath) is { } image:
                StartLogo.Visibility = Visibility.Collapsed;
                var picture = new Image { Source = image, Width = size * 1.3, Height = size * 1.3, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
                _startCustomContent = picture;
                break;
            case StartIconStyle.Text:
                StartLogo.Visibility = Visibility.Collapsed;
                var text = new TextBlock
                {
                    Text = UiText.Get("Start"), FontWeight = FontWeights.SemiBold, FontSize = Math.Round(13 * settings.StartIconScale),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                _startCustomContent = text;
                // The tile grows to the word instead of staying square.
                StartButton.Width = double.NaN;
                host.ClearValue(WidthProperty);
                host.Margin = new Thickness(10, 0, 10, 0);
                break;
        }

        if (_startCustomContent is not null) host.Children.Add(_startCustomContent);
        if (settings.StartIcon != StartIconStyle.Text)
        {
            host.Margin = new Thickness(0);
            host.SetResourceReference(WidthProperty, "DockIconSize");
        }
    }

    /// <summary>PNG/ICO/JPG chosen by the user; a missing or broken file falls back to the default logo.</summary>
    internal static BitmapSource? LoadStartImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException
                                       or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warning($"Start icon unavailable: {ex.Message}");
            return null;
        }
    }

    private static nint DockMonitorFromWindow(nint window) => MonitorFromWindow(window, 2);

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
}
