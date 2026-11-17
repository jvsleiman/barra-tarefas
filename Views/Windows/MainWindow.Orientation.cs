using System.Windows;
using System.Windows.Controls;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>Bottom, left, or right screen edge: flips the dock's axis, insets, magnification and hide direction.</summary>
public partial class MainWindow
{
    private DockEdge? _appliedEdge;

    private DockEdge Edge => _preferences.Current.Edge;

    private bool IsVertical => Edge != DockEdge.Bottom;

    /// <summary>Icons grow away from the screen edge.</summary>
    private Point IconScaleOrigin => Edge switch
    {
        DockEdge.Left => new Point(0.05, 0.5),
        DockEdge.Right => new Point(0.95, 0.5),
        _ => new Point(0.5, 0.95)
    };

    /// <summary>Launch bounce runs away from the edge: up for the bottom dock, sideways for side docks.</summary>
    private double BounceDirection => Edge == DockEdge.Left ? -1 : 1;

    /// <summary>Section separators: thin lines in the classic look, collapsed in the tile look (tiles already separate items).</summary>
    private void ApplyDividerStyle()
    {
        var vertical = Edge != DockEdge.Bottom;
        var tiles = _preferences.Current.IconTiles;
        foreach (var divider in new[] { StartDivider, RunningAppsDivider, ExtrasDivider, WidgetDivider })
        {
            divider.Width = tiles ? 0 : vertical ? 22 : 1;
            divider.Height = tiles ? 0 : vertical ? 1 : 22;
            divider.Margin = tiles ? new Thickness(0) : vertical ? new Thickness(0, 4, 0, 4) : new Thickness(4, 0, 4, 0);
        }
    }

    private void ApplyEdge()
    {
        var edge = Edge;
        if (_appliedEdge == edge) return;
        _appliedEdge = edge;
        var vertical = edge != DockEdge.Bottom;
        DockFlyout.Edge = edge;
        if (_autoHideController is not null)
        {
            _autoHideController.Edge = edge;
            DockSurfaceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            DockSurfaceTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            DockSurfaceTranslate.X = DockSurfaceTranslate.Y = 0;
        }

        // Window grid: headroom row above the shelf, or a headroom column beside it.
        DockHost.RowDefinitions.Clear();
        DockHost.ColumnDefinitions.Clear();
        var headroom = new GridLength(DockMetrics.Headroom);
        var shelf = new GridLength(1, GridUnitType.Star);
        var shelfIndex = 1;
        switch (edge)
        {
            case DockEdge.Left:
                DockHost.ColumnDefinitions.Add(new ColumnDefinition { Width = shelf });
                DockHost.ColumnDefinitions.Add(new ColumnDefinition { Width = headroom });
                shelfIndex = 0;
                break;
            case DockEdge.Right:
                DockHost.ColumnDefinitions.Add(new ColumnDefinition { Width = headroom });
                DockHost.ColumnDefinitions.Add(new ColumnDefinition { Width = shelf });
                break;
            default:
                DockHost.RowDefinitions.Add(new RowDefinition { Height = headroom });
                DockHost.RowDefinitions.Add(new RowDefinition { Height = shelf });
                break;
        }

        foreach (UIElement child in DockHost.Children)
        {
            Grid.SetRow(child, vertical ? 0 : shelfIndex);
            Grid.SetColumn(child, vertical ? shelfIndex : 0);
        }

        DockEdgeTrigger.HorizontalAlignment = edge switch
        {
            DockEdge.Left => HorizontalAlignment.Left,
            DockEdge.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch
        };
        DockEdgeTrigger.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
        DockEdgeTrigger.Width = vertical ? DockMetrics.EdgeTriggerHeight : double.NaN;
        DockEdgeTrigger.Height = vertical ? double.NaN : DockMetrics.EdgeTriggerHeight;

        var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        DockContents.Orientation = orientation;
        DockContents.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        DockContents.VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Center;
        var panel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        panel.VisualTree.SetValue(StackPanel.OrientationProperty, orientation);
        PinnedApps.ItemsPanel = panel;
        RunningApps.ItemsPanel = panel;
        StackItems.ItemsPanel = panel;
        DockWidgets.ItemsPanel = panel;
        AppGroupItems.ItemsPanel = panel;
        ApplyDividerStyle();

        // Widgets are designed for a horizontal shelf; side docks keep only the apps.
        DockScrollViewer.HoverHeadroom = vertical ? 0 : DockMetrics.Headroom;
        WidgetIsland.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        DockSheen.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        LeftResizeThumb.Visibility = RightResizeThumb.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        StatusText.MaxWidth = vertical ? 0 : 280;
        foreach (var button in GetAllDockButtons())
        {
            if (GetButtonIcon(button) is { } icon) icon.RenderTransformOrigin = IconScaleOrigin;
        }

        ApplySizeProfile();
        if (_replacementSession is not null) _replacementSession.Edge = edge;
        ApplyLayoutMode();
    }

    /// <summary>Side docks: cells shrink in height when many apps are open; the surface hugs its content.</summary>
    private void ResizeSideDockToContent()
    {
        var available = _dockArea.Height > 0 ? _dockArea.Height : SystemParameters.PrimaryScreenHeight;
        var count = Math.Max(1, (StartButtonShown ? 1 : 0) + VisiblePinnedCount + _runningApps.Count + ExtraButtonCount);
        var frame = DockFrame.Padding.Top + DockFrame.Padding.Bottom + DockSurface.Margin.Top + DockSurface.Margin.Bottom + 24;
        Application.Current.Resources["DockCellSize"] = Profile.Cell;
        Application.Current.Resources["DockCellHeight"] = Math.Min(Profile.Cell,
            Math.Max(DockMetrics.MinCellSize, (available - frame) / count));
        // Status messages show as a tooltip on the Start button instead of a wide text line.
        StartButton.ToolTip = string.IsNullOrEmpty(StatusText.Text) ? UiText.Get("Start") : StatusText.Text;
        // The surface sizes itself to its content; cells above already shrink so the content fits the screen.
        DockSurface.Width = double.NaN;
        DockSurface.Height = double.NaN;
    }
}
