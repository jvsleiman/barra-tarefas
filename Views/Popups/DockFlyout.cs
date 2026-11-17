using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>
/// A glass popup anchored to a dock element (stacks, calendar, tray, desktops). Opens away from the docked screen edge
/// and closes when the pointer clicks elsewhere.
/// </summary>
public sealed class DockFlyout
{
    private static int _openCount;
    private UIElement? _closedTarget;
    private nint _previousForeground;
    private bool _restoreFocus;
    private long _closedAt;
    private bool _closedWithPointerOnTarget;
    private static long _lastOpenedAt;
    private readonly Popup _popup;
    private readonly Border _frame;
    private readonly Border _innerBevel;

    public DockFlyout()
    {
        _innerBevel = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(12),
            Background = System.Windows.Media.Brushes.Transparent
        };
        _innerBevel.SetResourceReference(Border.BorderBrushProperty, "DockInnerBevelMaterial");

        _frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 8),
            Child = _innerBevel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24, ShadowDepth = 5, Direction = 270, Opacity = 0.45, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            }
        };
        _frame.SetResourceReference(Border.BackgroundProperty, "MenuMaterial");
        _frame.SetResourceReference(Border.BorderBrushProperty, "MenuStroke");
        _frame.SetResourceReference(TextElement.ForegroundProperty, "TextPrimary");
        _frame.SetResourceReference(TextElement.FontFamilyProperty, "InterfaceFont");
        _popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Child = _frame,
            PopupAnimation = PopupAnimation.Fade,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = Place
        };
        _popup.Opened += (_, _) =>
        {
            _openCount++;
            OpenFlyouts.Add(this);
        };
        _popup.Closed += (_, _) =>
        {
            _openCount = Math.Max(0, _openCount - 1);
            OpenFlyouts.Remove(this);
            // Light dismiss and Esc hand the keyboard back to the app that had it before the flyout opened.
            if (_restoreFocus && _previousForeground != 0
                && _popup.PlacementTarget is { } target && Window.GetWindow(target) is { IsActive: true })
            {
                NativeWindowTracker.Activate(_previousForeground);
            }

            Closed?.Invoke(this, EventArgs.Empty);
        };
        // Closed is raised only after the fade-out finishes, but a press on the anchor light-dismisses the flyout on
        // mouse down and its click arrives ~60 ms later, mid-fade. Record the dismissal the moment IsOpen turns false,
        // otherwise that click sees "not open, not recently closed" and reopens the flyout it just closed.
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Popup.IsOpenProperty, typeof(Popup))
            .AddValueChanged(_popup, (_, _) =>
            {
                if (_popup.IsOpen) return;
                _closedTarget = _popup.PlacementTarget;
                _closedAt = Environment.TickCount64;
                // Remembered so a click held longer than the grace period still toggles closed.
                _closedWithPointerOnTarget = _popup.PlacementTarget is FrameworkElement anchor && IsPointerOver(anchor);
            });
        _popup.KeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            args.Handled = true;
            _popup.IsOpen = false;
        };
    }

    private static readonly List<DockFlyout> OpenFlyouts = [];

    /// <summary>Closes every flyout, e.g. when the dock loses activation or another flyout opens.</summary>
    public static void CloseAll()
    {
        foreach (var flyout in OpenFlyouts.ToArray()) flyout.Close();
    }

    /// <summary>The dock edge the flyouts open away from.</summary>
    public static DockEdge Edge { get; set; } = DockEdge.Bottom;

    public static bool IsAnyOpen => _openCount > 0;

    public event EventHandler? Closed;

    public bool IsOpen => _popup.IsOpen;

    public void Show(UIElement target, UIElement content, bool animate = true)
    {
        _frame.Margin = Edge switch
        {
            DockEdge.Left => new Thickness(8, 0, 0, 0),
            DockEdge.Right => new Thickness(0, 0, 8, 0),
            _ => new Thickness(0, 0, 0, 8)
        };
        _innerBevel.Child = content;
        _popup.PopupAnimation = animate && SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None;
        foreach (var other in OpenFlyouts.Where(flyout => !ReferenceEquals(flyout, this)).ToArray()) other.Close();
        _popup.PlacementTarget = target;
        // The dock never activates on click; give it focus so the flyout gets keyboard input and closes with the dock.
        var owner = Window.GetWindow(target);
        _previousForeground = NativeWindowTracker.GetForegroundApplicationWindow();
        _restoreFocus = true;
        owner?.Activate();
        _popup.IsOpen = true;
        _lastOpenedAt = Environment.TickCount64;
        _frame.Focusable = true;
        _frame.Focus();
    }

    public void Toggle(UIElement target, Func<UIElement> content, bool animate = true)
    {
        if (_popup.IsOpen && ReferenceEquals(_popup.PlacementTarget, target))
        {
            Close();
            return;
        }

        // Clicking the anchor of an open flyout first light-dismisses it on mouse down; don't reopen on the click.
        if (RecentlyClosed(target)) return;
        Show(target, content(), animate);
    }

    public bool RecentlyClosed(UIElement target)
    {
        if (!ReferenceEquals(_closedTarget, target)) return false;
        var elapsed = Environment.TickCount64 - _closedAt;
        if (elapsed >= 350 && !(_closedWithPointerOnTarget && elapsed < 2000)) return false;
        _closedTarget = null; // consume it: the next click on the anchor opens again
        return true;
    }

    /// <summary>
    /// True right after a flyout opened. Opening activates the dock, and Windows sometimes bounces activation for a
    /// moment (seen with the hidden Alt+Tab host); that deactivation must not close the flyout that was just opened.
    /// </summary>
    public static bool OpenedRecently => Environment.TickCount64 - _lastOpenedAt < 800;

    internal static bool IsPointerOver(FrameworkElement element)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null || !GetCursorPos(out var cursor)) return false;
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return cursor.X >= topLeft.X && cursor.X < bottomRight.X && cursor.Y >= topLeft.Y && cursor.Y < bottomRight.Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>Closes without restoring focus: used when a flyout action opens something that should get it.</summary>
    public void Close()
    {
        _restoreFocus = false;
        _popup.IsOpen = false;
    }

    private static CustomPopupPlacement[] Place(Size popupSize, Size targetSize, Point offset)
    {
        var point = Edge switch
        {
            DockEdge.Left => new Point(targetSize.Width, (targetSize.Height - popupSize.Height) / 2),
            DockEdge.Right => new Point(-popupSize.Width, (targetSize.Height - popupSize.Height) / 2),
            _ => new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height)
        };
        return [new CustomPopupPlacement(point, PopupPrimaryAxis.None)];
    }

    /// <summary>Flat, borderless button used for flyout rows and tiles.</summary>
    public static Button FlatButton(object content, RoutedEventHandler click, string? toolTip = null)
    {
        var button = new Button { Content = content, ToolTip = toolTip, Cursor = System.Windows.Input.Cursors.Hand };
        button.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutButton");
        button.Click += click;
        if (toolTip is not null) System.Windows.Automation.AutomationProperties.SetName(button, toolTip);
        return button;
    }

    public static TextBlock Heading(string text)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(4, 0, 4, 8) };
        return block;
    }

    public static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 2) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        return block;
    }
}
