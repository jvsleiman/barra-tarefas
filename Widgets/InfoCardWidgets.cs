using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenTaskbar.Services;

namespace OpenTaskbar.Widgets;

/// <summary>Connected wireless devices as small icons with battery %, refreshed every minute.</summary>
public sealed class WirelessDevicesWidget : DockWidget
{
    private readonly StackPanel _row = CardParts.Row();
    private readonly Button _card;
    private DateTime _nextRead;
    private bool _reading;

    public WirelessDevicesWidget()
    {
        _card = CardParts.Card(_row, (_, _) => Report(ShellShortcuts.OpenUri("ms-settings:bluetooth")));
        Content = _card;
        HasData = false;
    }

    protected override void OnActivated() => _nextRead = DateTime.MinValue;

    protected override async void OnTick(DateTime now)
    {
        if (_reading || now < _nextRead) return;
        _reading = true;
        _nextRead = now.AddMinutes(1);
        try
        {
            var devices = await WirelessDevices.ReadAsync(CancellationToken.None);
            if (!IsActive) return;
            Render(devices);
        }
        finally
        {
            _reading = false;
        }
    }

    private void Render(IReadOnlyList<WirelessDevice> devices)
    {
        _row.Children.Clear();
        foreach (var device in devices.Take(6))
        {
            var icon = CardParts.Icon(IconFor(device.Kind), 15, device.Battery is <= 20 ? "WeekdayAccent" : "TextPrimary");
            var item = CardParts.Row(icon);
            item.Margin = new Thickness(4, 0, 4, 0);
            if (device.Battery is { } level) item.Children.Add(Spaced(CardParts.Text($"{level}%", 11)));
            item.ToolTip = device.Battery is { } battery ? $"{device.Name} · {battery}%" : device.Name;
            _row.Children.Add(item);
        }

        _card.ToolTip = devices.Count == 0 ? null : string.Join("\n", devices.Select(device =>
            device.Battery is { } level ? $"{device.Name} · {level}%" : device.Name));
        HasData = devices.Count > 0;
    }

    public static string IconFor(WirelessKind kind) => kind switch
    {
        WirelessKind.Headset => "IconHeadphones",
        WirelessKind.Mouse => "IconMouse",
        WirelessKind.Keyboard => "IconKeyboard",
        WirelessKind.Controller => "IconGamepad",
        WirelessKind.Phone => "IconPhone",
        _ => "IconBluetooth"
    };

    private static TextBlock Spaced(TextBlock text)
    {
        text.Margin = new Thickness(4, 0, 0, 0);
        return text;
    }
}

/// <summary>Live download/upload speed over all active network adapters.</summary>
public sealed class NetworkSpeedWidget : DockWidget
{
    private readonly TextBlock _down = CardParts.Text("—", 12);
    private readonly TextBlock _up = CardParts.Text("—", 11, FontWeights.Normal, "TextSecondary");
    private (long Received, long Sent, DateTime At)? _last;

    public NetworkSpeedWidget()
    {
        var down = CardParts.Row(CardParts.Icon("IconArrowDown", 12, "WidgetAccent"), _down);
        var up = CardParts.Row(CardParts.Icon("IconArrowUp", 12, "TextSecondary"), _up);
        _down.Margin = _up.Margin = new Thickness(4, 0, 0, 0);
        _down.MinWidth = _up.MinWidth = 62;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(down);
        stack.Children.Add(up);
        Content = CardParts.Card(stack, (_, _) => Report(ShellShortcuts.OpenUri("ms-settings:network-status")));
    }

    protected override void OnActivated() => _last = null;

    protected override void OnTick(DateTime now)
    {
        var (received, sent) = ReadTotals();
        if (_last is { } last && now > last.At)
        {
            var seconds = (now - last.At).TotalSeconds;
            _down.Text = FormatRate(Math.Max(0, received - last.Received) / seconds, Culture);
            _up.Text = FormatRate(Math.Max(0, sent - last.Sent) / seconds, Culture);
        }

        _last = (received, sent, now);
    }

    private CultureInfo Culture => UiText.CultureFor(Settings.Language);

    private static (long Received, long Sent) ReadTotals()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = adapter.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
        }

        return (received, sent);
    }

    /// <summary>Bytes/second → "850 KB/s", "12,4 MB/s" (decimal units, like Task Manager).</summary>
    public static string FormatRate(double bytesPerSecond, CultureInfo culture) => bytesPerSecond switch
    {
        >= 1_000_000_000 => (bytesPerSecond / 1_000_000_000).ToString("0.0", culture) + " GB/s",
        >= 1_000_000 => (bytesPerSecond / 1_000_000).ToString(bytesPerSecond >= 100_000_000 ? "0" : "0.0", culture) + " MB/s",
        >= 1_000 => (bytesPerSecond / 1_000).ToString("0", culture) + " KB/s",
        _ => Math.Round(bytesPerSecond).ToString("0", culture) + " B/s"
    };
}

/// <summary>System drive usage: a bar plus free space; click opens the drive.</summary>
public sealed class DiskUsageWidget : DockWidget
{
    private readonly TextBlock _title = CardParts.Text("C:", 12);
    private readonly TextBlock _free = CardParts.Text(string.Empty, 11, FontWeights.Normal, "TextSecondary");
    private readonly Grid _bar;
    private readonly Border _fill;
    private DateTime _nextRead;

    public DiskUsageWidget()
    {
        (_bar, _fill) = CardParts.Progress(92, "WidgetAccent");
        _bar.Margin = new Thickness(0, 3, 0, 3);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(CardParts.Row(_title, _free));
        _free.Margin = new Thickness(6, 0, 0, 0);
        stack.Children.Add(_bar);
        var icon = CardParts.Icon("IconHardDrive", 16);
        icon.Margin = new Thickness(0, 0, 8, 0);
        Content = CardParts.Card(CardParts.Row(icon, stack), (_, _) => Report(ShellShortcuts.OpenUri(SystemDrive)));
    }

    private static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

    protected override void OnActivated() => _nextRead = DateTime.MinValue;

    protected override void OnTick(DateTime now)
    {
        if (now < _nextRead) return;
        _nextRead = now.AddSeconds(30);
        try
        {
            var drive = new DriveInfo(SystemDrive);
            var used = 1 - (double)drive.AvailableFreeSpace / drive.TotalSize;
            _title.Text = $"{drive.Name.TrimEnd('\\')} {used.ToString("P0", UiText.CultureFor(Settings.Language))}";
            _free.Text = UiText.Format("DiskFree", (drive.AvailableFreeSpace / 1_000_000_000d).ToString("0", UiText.CultureFor(Settings.Language)));
            CardParts.SetProgress(_bar, _fill, used);
            _fill.SetResourceReference(Border.BackgroundProperty, used >= 0.9 ? "WeekdayAccent" : "WidgetAccent");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            HasData = false;
        }
    }
}

/// <summary>Quick notes: the card shows the first line; a click opens an editor saved to notas.txt as you type.</summary>
public sealed class QuickNotesWidget : DockWidget
{
    private readonly TextBlock _preview = CardParts.Text(string.Empty, 12);
    private readonly Button _card;
    private readonly DockFlyout _flyout = new();
    private readonly DispatcherTimer _save;
    private string _text = string.Empty;

    public static string NotesPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTaskbar", "notas.txt");

    public QuickNotesWidget()
    {
        _preview.MaxWidth = 150;
        var icon = CardParts.Icon("IconNote", 15, "EventAccent");
        icon.Margin = new Thickness(0, 0, 8, 0);
        _card = CardParts.Card(CardParts.Row(icon, _preview), (_, _) => _flyout.Toggle(_card!, BuildEditor));
        Content = _card;
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            Save();
        };
        UiText.Current.PropertyChanged += (_, _) => RenderPreview();
    }

    protected override void OnActivated()
    {
        try
        {
            _text = File.Exists(NotesPath) ? File.ReadAllText(NotesPath) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _text = string.Empty;
        }

        RenderPreview();
    }

    protected override void OnDeactivated()
    {
        if (_save.IsEnabled) Save();
        _flyout.Close();
    }

    /// <summary>The card's one-line preview: the first non-empty line, or the widget name when empty.</summary>
    public static string PreviewOf(string text, string empty) =>
        text.Replace("\r", string.Empty).Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? empty;

    private void RenderPreview()
    {
        _preview.Text = PreviewOf(_text, UiText.Get("WidgetNotes"));
        _card.ToolTip = string.IsNullOrWhiteSpace(_text) ? UiText.Get("WidgetNotesDescription") : _text.Trim();
    }

    private UIElement BuildEditor()
    {
        var box = new TextBox
        {
            Text = _text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, Width = 300, Height = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8), FontSize = 13, BorderThickness = new Thickness(1)
        };
        box.SetResourceReference(Control.BackgroundProperty, "SettingsControl");
        box.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        box.SetResourceReference(Control.BorderBrushProperty, "MenuStroke");
        box.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimary");
        box.TextChanged += (_, _) =>
        {
            _text = box.Text;
            RenderPreview();
            _save.Stop();
            _save.Start();
        };
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        };
        var root = new StackPanel();
        root.Children.Add(DockFlyout.Heading(UiText.Get("WidgetNotes")));
        root.Children.Add(box);
        root.Children.Add(DockFlyout.Caption(UiText.Get("NotesSavedHint")));
        return root;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
            File.WriteAllText(NotesPath, _text, new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(UiText.Get("NotesSaveError"));
        }
    }
}
