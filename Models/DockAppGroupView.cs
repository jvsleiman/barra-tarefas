using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace OpenTaskbar.Models;

/// <summary>
/// A dock tile that holds several pinned apps. Shows up to four app icons in a 2x2 grid and reports running /
/// foreground state from its members so the shared DockButton template can draw the running dot.
/// </summary>
public sealed class DockAppGroupView : INotifyPropertyChanged, IDisposable
{
    public DockAppGroupView(string name, IReadOnlyList<DockApp> apps)
    {
        Name = name;
        Apps = apps;
        foreach (var app in Apps) app.PropertyChanged += App_PropertyChanged;
    }

    public string Name { get; }
    public IReadOnlyList<DockApp> Apps { get; }
    public IReadOnlyList<BitmapSource?> PreviewIcons => Apps.Take(4).Select(app => app.Icon).ToArray();
    public bool IsRunning => Apps.Any(app => app.IsRunning);
    public bool IsForeground => Apps.Any(app => app.IsForeground);
    public string ToolTip => $"{Name}: {string.Join(", ", Apps.Select(app => app.Name))}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockApp.IsRunning):
                Raise(nameof(IsRunning));
                break;
            case nameof(DockApp.IsForeground):
                Raise(nameof(IsForeground));
                break;
            case nameof(DockApp.Icon):
                Raise(nameof(PreviewIcons));
                break;
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        foreach (var app in Apps) app.PropertyChanged -= App_PropertyChanged;
    }
}
