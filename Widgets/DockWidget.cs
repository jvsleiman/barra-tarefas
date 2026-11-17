using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTaskbar.Models;

namespace OpenTaskbar.Widgets;

/// <summary>
/// Base for dock widgets. The coordinator activates a widget while it is enabled in settings and ticks it once
/// per second; inactive widgets must not poll or hold system subscriptions.
/// </summary>
public class DockWidget : UserControl, IDisposable
{
    private bool _hasData = true;

    public DockWidget()
    {
        Visibility = Visibility.Collapsed;
        SetResourceReference(ForegroundProperty, "TextPrimary");
        Focusable = false;
        RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(), new TranslateTransform() }
        };
        RenderTransformOrigin = new Point(0.5, 0.5);
    }

    public static readonly DependencyProperty ShowLeadingDividerProperty = DependencyProperty.Register(
        nameof(ShowLeadingDivider), typeof(bool), typeof(DockWidget),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Draws a thin vertical divider in the gap before this card (set for every visible card but the first).</summary>
    public bool ShowLeadingDivider
    {
        get => (bool)GetValue(ShowLeadingDividerProperty);
        set => SetValue(ShowLeadingDividerProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (!ShowLeadingDivider || ActualHeight <= 0 || TryFindResource("DockDivider") is not Brush brush) return;
        var inset = ActualHeight * 0.22;
        drawingContext.DrawRectangle(brush, null, new Rect(0, inset, 1, ActualHeight - inset * 2));
    }

    public Action<string>? ReportError { get; set; }
    protected bool IsActive { get; private set; }
    protected DockSettings Settings { get; private set; } = new();

    /// <summary>False hides the widget while active, e.g. media controls with no playing session.</summary>
    protected bool HasData
    {
        get => _hasData;
        set
        {
            _hasData = value;
            UpdateVisibility();
        }
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        UpdateVisibility();
        OnActivated();
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        UpdateVisibility();
        OnDeactivated();
    }

    public void ApplySettings(DockSettings settings)
    {
        Settings = settings;
        OnSettingsChanged();
    }

    public void Tick(DateTime now)
    {
        if (IsActive) OnTick(now);
    }

    protected virtual void OnActivated() { }
    protected virtual void OnDeactivated() { }
    protected virtual void OnSettingsChanged() { }
    protected virtual void OnTick(DateTime now) { }

    protected void Report(string? error)
    {
        if (error is not null) ReportError?.Invoke(error);
    }

    private void UpdateVisibility() =>
        Visibility = IsActive && _hasData ? Visibility.Visible : Visibility.Collapsed;

    public void Dispose()
    {
        Deactivate();
        GC.SuppressFinalize(this);
    }
}
