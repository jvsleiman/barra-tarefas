using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTaskbar.Services;
using Windows.Foundation;
using Windows.Media.Control;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using MediaSessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace OpenTaskbar.Widgets;

/// <summary>
/// Shows and controls the system's current media session (Spotify, browsers, players) through the
/// Global System Media Transport Controls, including track progress and audio output volume.
/// </summary>
public partial class MediaWidget : DockWidget
{
    private MediaSessionManager? _manager;
    private MediaSession? _session;
    private AudioEndpointMonitor? _audio;
    private int _refreshVersion;
    private string? _artKey;
    private TimeSpan _startTime;
    private TimeSpan _endTime;
    private TimeSpan _position;
    private DateTimeOffset _positionUpdatedAt;
    private bool _playing;
    private bool _canSeek;
    private bool _updatingProgress;
    private bool _draggingProgress;
    private bool _updatingVolume;

    public MediaWidget()
    {
        InitializeComponent();
        HasData = false;
        UiText.Current.PropertyChanged += (_, _) => RenderVolume();
    }

    protected override async void OnActivated()
    {
        try
        {
            _audio = new AudioEndpointMonitor();
            _audio.VolumeChanged += Audio_VolumeChanged;
            _audio.DefaultDeviceChanged += Audio_DefaultDeviceChanged;
        }
        catch (COMException ex)
        {
            AppLog.Warning($"Media widget audio unavailable: {ex.Message}");
            _audio = null;
        }

        RenderVolume();

        try
        {
            var manager = await MediaSessionManager.RequestAsync();
            if (!IsActive) return;
            _manager = manager;
            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
            BindSession(_manager.GetCurrentSession());
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            AppLog.Warning($"Media session manager unavailable: {ex.Message}");
            HasData = false;
        }
    }

    protected override void OnDeactivated()
    {
        BindSession(null);
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
            _manager = null;
        }

        if (_audio is not null)
        {
            _audio.VolumeChanged -= Audio_VolumeChanged;
            _audio.DefaultDeviceChanged -= Audio_DefaultDeviceChanged;
            _audio.Dispose();
            _audio = null;
        }
    }

    protected override void OnTick(DateTime now)
    {
        if (_session is not null && !_draggingProgress)
        {
            UpdateProgressUI();
        }
    }

    private void Audio_VolumeChanged() => Dispatcher.BeginInvoke(DispatcherPriority.Background, RenderVolume);

    private void Audio_DefaultDeviceChanged() => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        _audio?.BindDefaultDevice();
        RenderVolume();
    });

    private void RenderVolume()
    {
        var hasDevice = _audio?.HasDevice == true;
        MuteButton.IsEnabled = hasDevice;
        VolumeSlider.IsEnabled = hasDevice;
        if (!hasDevice)
        {
            VolumeIcon.Content = FindResource("IconVolumeMute");
            return;
        }

        var level = (int)Math.Round(_audio!.Volume * 100);
        var muted = _audio.Muted;
        VolumeIcon.Content = FindResource(muted || level == 0 ? "IconVolumeMute" : level < 50 ? "IconVolumeLow" : "IconVolume");
        VolumeSlider.ToolTip = muted ? UiText.Get("MuteToggle") : $"{_audio.DeviceName ?? UiText.Get("Volume")}: {UiText.Format("Percent", level)}";
        _updatingVolume = true;
        VolumeSlider.Value = level;
        _updatingVolume = false;
    }

    private void Manager_CurrentSessionChanged(MediaSessionManager sender, CurrentSessionChangedEventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsActive && _manager is not null) BindSession(_manager.GetCurrentSession());
        });

    private void BindSession(MediaSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            _session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged;
        }

        _session = session;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            _session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
            _session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
        }

        _artKey = null;
        Refresh(includeProperties: true);
    }

    private void Session_MediaPropertiesChanged(MediaSession sender, MediaPropertiesChangedEventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Refresh(includeProperties: true));

    private void Session_PlaybackInfoChanged(MediaSession sender, PlaybackInfoChangedEventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Refresh(includeProperties: false));

    private void Session_TimelinePropertiesChanged(MediaSession sender, TimelinePropertiesChangedEventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshTimeline);

    private void RefreshTimeline()
    {
        if (_session is null) return;
        try
        {
            var timeline = _session.GetTimelineProperties();
            if (timeline is null) return;
            _startTime = timeline.StartTime;
            _endTime = timeline.EndTime;
            _position = timeline.Position;
            _positionUpdatedAt = timeline.LastUpdatedTime;
            if (!_draggingProgress) UpdateProgressUI();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NullReferenceException)
        {
            AppLog.Warning($"Media timeline refresh failed: {ex.Message}");
        }
    }

    private void UpdateProgressUI()
    {
        var duration = _endTime - _startTime;
        if (duration.TotalSeconds <= 0.5)
        {
            _updatingProgress = true;
            ProgressSlider.Maximum = 100;
            ProgressSlider.Value = 0;
            ProgressSlider.IsEnabled = false;
            ProgressSlider.ToolTip = null;
            _updatingProgress = false;
            return;
        }

        var elapsed = (_position - _startTime).TotalSeconds;
        if (_playing && _positionUpdatedAt != default)
        {
            var delta = (DateTimeOffset.UtcNow - _positionUpdatedAt).TotalSeconds;
            if (delta > 0 && delta < 3600) elapsed += delta;
        }

        elapsed = Math.Clamp(elapsed, 0, duration.TotalSeconds);
        _updatingProgress = true;
        ProgressSlider.Maximum = duration.TotalSeconds;
        ProgressSlider.Value = elapsed;
        ProgressSlider.IsEnabled = _canSeek;
        ProgressSlider.ToolTip = $"{FormatTime(TimeSpan.FromSeconds(elapsed))} / {FormatTime(duration)}";
        _updatingProgress = false;
    }

    private static string FormatTime(TimeSpan span) =>
        span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");

    private async void Refresh(bool includeProperties)
    {
        // Only property fetches are versioned; a playback-only update must not cancel an in-flight title fetch.
        var version = includeProperties ? ++_refreshVersion : _refreshVersion;
        var session = _session;
        if (session is null)
        {
            HasData = false;
            return;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            // Some players publish a session before (or after) their playback info/controls exist.
            var controls = playback?.Controls;
            _playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _canSeek = controls?.IsPlaybackPositionEnabled == true;
            PlayPauseGlyph.Content = FindResource(_playing ? "IconPauseFill" : "IconPlayFill");
            PlayPauseButton.IsEnabled = controls is not null && (controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled);
            PreviousButton.IsEnabled = controls?.IsPreviousEnabled == true;
            NextButton.IsEnabled = controls?.IsNextEnabled == true;
            RefreshTimeline();
            if (!includeProperties) return;

            var properties = await session.TryGetMediaPropertiesAsync();
            if (version != _refreshVersion || !IsActive) return;
            var title = properties?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                HasData = false;
                return;
            }

            TitleText.Text = title;
            ArtistText.Text = string.IsNullOrWhiteSpace(properties!.Artist) ? properties.AlbumTitle : properties.Artist;
            Card.ToolTip = string.IsNullOrWhiteSpace(ArtistText.Text) ? title : $"{title}\n{ArtistText.Text}";
            HasData = true;
            await LoadArtAsync(properties, version);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NullReferenceException)
        {
            // Sessions vanish while their owning app exits; the next CurrentSessionChanged rebinds.
            AppLog.Warning($"Media session refresh failed: {ex.Message}");
            if (version == _refreshVersion) HasData = false;
        }
    }

    private async Task LoadArtAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties, int version)
    {
        var key = $"{properties.Title}|{properties.Artist}|{properties.AlbumTitle}";
        if (key == _artKey) return;
        // Players often publish artwork in a later event with the same title, so the key is recorded only on success.
        if (properties.Thumbnail is null)
        {
            ArtImage.Source = null;
            ArtPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            using var stream = await properties.Thumbnail.OpenReadAsync();
            if (version != _refreshVersion) return;
            using var managed = stream.AsStreamForRead();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.StreamSource = managed;
            image.EndInit();
            image.Freeze();
            ArtImage.Source = image;
            ArtPlaceholder.Visibility = Visibility.Collapsed;
            _artKey = key;
        }
        catch (Exception ex) when (ex is COMException or IOException or NotSupportedException or FileFormatException or NullReferenceException)
        {
            AppLog.Warning($"Media artwork unavailable: {ex.Message}");
        }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) => await RunAsync(s => s.TrySkipPreviousAsync());
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await RunAsync(s => s.TryTogglePlayPauseAsync());
    private async void Next_Click(object sender, RoutedEventArgs e) => await RunAsync(s => s.TrySkipNextAsync());

    private void ProgressSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingProgress = true;
    }

    private async void ProgressSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingProgress) return;
        _draggingProgress = false;
        await SeekToSliderValueAsync();
    }

    private async void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingProgress || _draggingProgress || !ProgressSlider.IsMouseOver || Mouse.LeftButton != MouseButtonState.Pressed) return;
        await SeekToSliderValueAsync();
    }

    private async Task SeekToSliderValueAsync()
    {
        if (_session is null || !_canSeek) return;
        var target = _startTime + TimeSpan.FromSeconds(ProgressSlider.Value);
        _position = target;
        _positionUpdatedAt = DateTimeOffset.UtcNow;
        await RunAsync(s => s.TryChangePlaybackPositionAsync(target.Ticks));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume || _audio is null) return;
        if (!_audio.SetVolume((float)(e.NewValue / 100))) Report(UiText.Get("ErrorVolumeChange"));
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audio is not null && !_audio.ToggleMute()) Report(UiText.Get("ErrorVolumeChange"));
    }

    private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_audio?.HasDevice != true) return;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + Math.Sign(e.Delta) * 2, 0, 100);
        e.Handled = true;
    }

    private async Task RunAsync(Func<MediaSession, IAsyncOperation<bool>> command)
    {
        if (_session is null) return;
        try
        {
            if (!await command(_session)) Report("Medya oturumu kontrol edilemedi.");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NullReferenceException)
        {
            AppLog.Warning($"Media command failed: {ex.Message}");
            Report("Medya oturumu kontrol edilemedi.");
        }
    }
}
