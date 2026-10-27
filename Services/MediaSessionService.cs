using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

/// <summary>Snapshot of the current media session. Mutated only on the UI thread.</summary>
internal sealed class MediaInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Source { get; set; } = "";
    public BitmapSource? Artwork { get; set; }
    public Color? Accent { get; set; }

    public bool IsPlaying { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
    public bool CanPrevious { get; set; }
    public bool CanNext { get; set; }
    public bool CanSeek { get; set; }

    public TimeSpan StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public TimeSpan Position { get; set; }
    public DateTimeOffset PositionUpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Apps only push timeline updates every few seconds (or on seek), so extrapolate
    /// from the last reported position while playing.
    /// </summary>
    public TimeSpan GetPosition(DateTimeOffset now)
    {
        var position = Position;
        if (IsPlaying)
            position += TimeSpan.FromTicks((long)((now - PositionUpdatedAt).Ticks * PlaybackRate));
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && position > Duration) position = Duration;
        return position;
    }
}

/// <summary>
/// Wraps the WinRT GlobalSystemMediaTransportControlsSessionManager: the same API that powers the
/// Windows volume-flyout media card. Works with Spotify, browsers, Media Player, VLC, etc.
/// All events are marshalled onto the UI dispatcher.
/// </summary>
internal sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _propertiesVersion;

    public MediaInfo? Current { get; private set; }

    /// <summary>Anything about the session changed (metadata, playback state, timeline, artwork).</summary>
    public event Action? Changed;

    /// <summary>A different track started.</summary>
    public event Action? TrackChanged;

    public MediaSessionService(Dispatcher ui) => _ui = ui;

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Session manager unavailable: {ex.Message}");
            return;
        }

        _manager.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(AttachCurrentSession);
        _manager.SessionsChanged += (_, _) => _ui.InvokeAsync(AttachCurrentSession);
        AttachCurrentSession();
    }

    // ── Commands ─────────────────────────────────────────────────────

    public async void TogglePlayPause()
    {
        try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Media] Toggle failed: {ex.Message}"); }
    }

    public async void Next()
    {
        try { if (_session is not null) await _session.TrySkipNextAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Media] Next failed: {ex.Message}"); }
    }

    public async void Previous()
    {
        try { if (_session is not null) await _session.TrySkipPreviousAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Media] Previous failed: {ex.Message}"); }
    }

    public async void Seek(TimeSpan position)
    {
        var media = Current;
        if (_session is null || media is null) return;

        // Optimistic: move the scrubber immediately, the app will confirm via TimelinePropertiesChanged.
        media.Position = position;
        media.PositionUpdatedAt = DateTimeOffset.Now;
        Changed?.Invoke();

        try { await _session.TryChangePlaybackPositionAsync((media.StartTime + position).Ticks); }
        catch (Exception ex) { Debug.WriteLine($"[Media] Seek failed: {ex.Message}"); }
    }

    // ── Session tracking ─────────────────────────────────────────────

    private void AttachCurrentSession()
    {
        if (_manager is null) return;

        GlobalSystemMediaTransportControlsSession? session;
        try { session = _manager.GetCurrentSession(); }
        catch { session = null; }

        string? newId = session?.SourceAppUserModelId;
        if (_session is not null && session is not null && newId == _session.SourceAppUserModelId)
            return; // same app, keep existing subscriptions

        DetachSession();
        _session = session;

        if (session is null)
        {
            Current = null;
            Changed?.Invoke();
            return;
        }

        Current = new MediaInfo { Source = FriendlyAppName(newId) };
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        RefreshTimeline();
        RefreshPlayback();
        _ = RefreshPropertiesAsync();
    }

    private void DetachSession()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        _session = null;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ui.InvokeAsync(() => _ = RefreshPropertiesAsync());

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _ui.InvokeAsync(RefreshPlayback);

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _ui.InvokeAsync(RefreshTimeline);

    private void RefreshPlayback()
    {
        var session = _session;
        var media = Current;
        if (session is null || media is null) return;

        GlobalSystemMediaTransportControlsSessionPlaybackInfo info;
        try { info = session.GetPlaybackInfo(); }
        catch { return; }

        bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (playing != media.IsPlaying)
        {
            // Re-base the extrapolated position at the moment playback state flips.
            var now = DateTimeOffset.Now;
            media.Position = media.GetPosition(now);
            media.PositionUpdatedAt = now;
            media.IsPlaying = playing;
        }

        media.PlaybackRate = info.PlaybackRate is > 0 ? info.PlaybackRate.Value : 1.0;
        var controls = info.Controls;
        media.CanPrevious = controls.IsPreviousEnabled;
        media.CanNext = controls.IsNextEnabled;
        media.CanSeek = controls.IsPlaybackPositionEnabled;
        Changed?.Invoke();
    }

    private void RefreshTimeline()
    {
        var session = _session;
        var media = Current;
        if (session is null || media is null) return;

        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline;
        try { timeline = session.GetTimelineProperties(); }
        catch { return; }

        media.StartTime = timeline.StartTime;
        media.Duration = timeline.EndTime - timeline.StartTime;
        media.Position = timeline.Position - timeline.StartTime;
        media.PositionUpdatedAt = timeline.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : timeline.LastUpdatedTime;
        Changed?.Invoke();
    }

    private async Task RefreshPropertiesAsync()
    {
        var session = _session;
        var media = Current;
        if (session is null || media is null) return;

        int version = ++_propertiesVersion;
        GlobalSystemMediaTransportControlsSessionMediaProperties properties;
        try { properties = await session.TryGetMediaPropertiesAsync(); }
        catch { return; }
        if (version != _propertiesVersion || !ReferenceEquals(media, Current)) return;

        string title = properties.Title ?? "";
        string artist = string.IsNullOrWhiteSpace(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist;
        bool trackChanged = title != media.Title || artist != media.Artist;

        media.Title = title;
        media.Artist = artist;
        if (trackChanged && properties.Thumbnail is null)
        {
            media.Artwork = null;
            media.Accent = null;
        }
        Changed?.Invoke();
        if (trackChanged && title.Length > 0) TrackChanged?.Invoke();

        if (properties.Thumbnail is { } thumbnail)
        {
            var (artwork, accent) = await Task.Run(() => LoadThumbnailAndAccentAsync(thumbnail));
            if (version != _propertiesVersion || !ReferenceEquals(media, Current)) return;
            media.Artwork = artwork;
            media.Accent = accent;
            Changed?.Invoke();
        }
    }

    private static async Task<(BitmapSource? Artwork, Color? Accent)> LoadThumbnailAndAccentAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var winrtStream = await reference.OpenReadAsync().AsTask().ConfigureAwait(false);
            using var source = winrtStream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer).ConfigureAwait(false);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();

            var accent = ColorExtractor.Vibrant(image);
            return (image, accent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] Thumbnail load failed: {ex.Message}");
            return (null, null);
        }
    }

    private static string FriendlyAppName(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        string id = aumid.ToLowerInvariant();
        if (id.Contains("spotify")) return "Spotify";
        if (id.Contains("msedge")) return "Edge";
        if (id.Contains("chrome")) return "Chrome";
        if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "Firefox";
        if (id.Contains("brave")) return "Brave";
        if (id.Contains("opera")) return "Opera";
        if (id.Contains("zunemusic")) return "Media Player";
        if (id.Contains("zunevideo")) return "Movies & TV";
        if (id.Contains("vlc")) return "VLC";

        string name = aumid;
        int bang = name.LastIndexOf('!');
        if (bang >= 0) name = name[(bang + 1)..];
        int underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        int dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        return name;
    }

    public void Dispose() => DetachSession();
}
