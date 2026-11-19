using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>Represents an active media session from any application running on Windows.</summary>
public sealed class ActiveMediaSessionItem
{
    public string Aumid { get; init; } = "";
    public string AppName { get; init; } = "";
    public MediaPlayerBrandInfo? BrandInfo { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public bool IsPlaying { get; init; }
    public BitmapSource? Artwork { get; init; }
    public bool IsCurrent { get; init; }
}

/// <summary>Snapshot of the current media session. Mutated only on the UI thread.</summary>
internal sealed class MediaInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Source { get; set; } = "";
    public MediaPlayerBrandInfo BrandInfo { get; set; } = null!;
    public BitmapSource? Artwork { get; set; }
    public Color? Accent { get; set; }
    public Color? SecondaryAccent { get; set; }
    public int ActiveSessionCount { get; set; } = 1;

    public bool IsPlaying { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
    public bool CanPrevious { get; set; }
    public bool CanNext { get; set; }
    public bool CanSeek { get; set; }
    public bool CanShuffle { get; set; }
    public bool CanRepeat { get; set; }
    public bool? IsShuffleActive { get; set; }
    public Windows.Media.MediaPlaybackAutoRepeatMode? AutoRepeatMode { get; set; }
    public string AlbumTitle { get; set; } = "";
    public int TrackNumber { get; set; }
    public int AlbumTrackCount { get; set; }
    public string SourceAppUserModelId { get; set; } = "";

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
/// Supports cycling between multiple active media sessions when several apps are open.
/// All events are marshalled onto the UI dispatcher.
/// </summary>
internal sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _preferredSessionAumid;
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
            App.Log($"[Media] Session manager unavailable: {ex.Message}");
            return;
        }

        _manager.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(AttachCurrentSession);
        _manager.SessionsChanged += (_, _) => _ui.InvokeAsync(AttachCurrentSession);
        AttachCurrentSession();
    }

    // ── Commands ─────────────────────────────────────────────────────

    public void TogglePlayPause() => _ = TogglePlayPauseAsync();
    public void Next() => _ = NextAsync();
    public void Previous() => _ = PreviousAsync();
    public void Seek(TimeSpan position) => _ = SeekAsync(position);
    public void ToggleShuffle() => _ = ToggleShuffleAsync();
    public void CycleRepeatMode() => _ = CycleRepeatModeAsync();

    public async Task ToggleShuffleAsync()
    {
        try
        {
            if (_session is not null)
            {
                bool target = !(Current?.IsShuffleActive ?? false);
                await _session.TryChangeShuffleActiveAsync(target);
            }
        }
        catch (Exception ex) { App.Log($"[Media] Shuffle toggle failed: {ex.Message}"); }
    }

    public async Task CycleRepeatModeAsync()
    {
        try
        {
            if (_session is not null)
            {
                var cur = Current?.AutoRepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None;
                var target = cur switch
                {
                    Windows.Media.MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                    Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                    _ => Windows.Media.MediaPlaybackAutoRepeatMode.None
                };
                await _session.TryChangeAutoRepeatModeAsync(target);
            }
        }
        catch (Exception ex) { App.Log($"[Media] Repeat cycle failed: {ex.Message}"); }
    }

    public async Task TogglePlayPauseAsync()
    {
        try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); }
        catch (Exception ex) { App.Log($"[Media] Toggle failed: {ex.Message}"); }
    }

    public async Task NextAsync()
    {
        try { if (_session is not null) await _session.TrySkipNextAsync(); }
        catch (Exception ex) { App.Log($"[Media] Next failed: {ex.Message}"); }
    }

    public async Task PreviousAsync()
    {
        try { if (_session is not null) await _session.TrySkipPreviousAsync(); }
        catch (Exception ex) { App.Log($"[Media] Previous failed: {ex.Message}"); }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        var media = Current;
        if (_session is null || media is null) return;

        // Optimistic: move the scrubber immediately, the app will confirm via TimelinePropertiesChanged.
        media.Position = position;
        media.PositionUpdatedAt = DateTimeOffset.Now;
        Changed?.Invoke();

        try { await _session.TryChangePlaybackPositionAsync((media.StartTime + position).Ticks); }
        catch (Exception ex) { App.Log($"[Media] Seek failed: {ex.Message}"); }
    }

    public bool SwitchToSession(string aumid)
    {
        if (_manager is null || string.IsNullOrWhiteSpace(aumid)) return false;
        try
        {
            var sessions = _manager.GetSessions();
            if (sessions is null) return false;
            var target = sessions.FirstOrDefault(s => string.Equals(s.SourceAppUserModelId, aumid, StringComparison.OrdinalIgnoreCase));
            if (target is null) return false;

            _preferredSessionAumid = target.SourceAppUserModelId;
            AttachSessionCore(target, sessions.Count);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[Media] SwitchToSession failed: {ex.Message}");
            return false;
        }
    }

    public async Task<IReadOnlyList<ActiveMediaSessionItem>> GetActiveSessionsAsync()
    {
        var list = new List<ActiveMediaSessionItem>();
        if (_manager is null) return list;

        try
        {
            var sessions = _manager.GetSessions();
            if (sessions is null || sessions.Count == 0) return list;

            string currentAumid = _session?.SourceAppUserModelId ?? "";

            foreach (var sess in sessions)
            {
                try
                {
                    string aumid = sess.SourceAppUserModelId ?? "";
                    string name = FriendlyAppName(aumid);
                    bool isPlaying = false;
                    try
                    {
                        var pb = sess.GetPlaybackInfo();
                        isPlaying = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    }
                    catch { }

                    string title = "";
                    string artist = "";
                    BitmapSource? art = null;
                    try
                    {
                        var props = await sess.TryGetMediaPropertiesAsync();
                        if (props != null)
                        {
                            title = props.Title ?? "";
                            artist = string.IsNullOrWhiteSpace(props.Artist) ? (props.AlbumArtist ?? "") : props.Artist;
                            if (props.Thumbnail != null)
                            {
                                var (loadedArt, _, _) = await Task.Run(() => LoadThumbnailAndAccentAsync(props.Thumbnail));
                                art = loadedArt;
                            }
                        }
                    }
                    catch { }

                    var brand = MediaPlayerBrandService.Detect(aumid, title, artist);
                    list.Add(new ActiveMediaSessionItem
                    {
                        Aumid = aumid,
                        AppName = string.IsNullOrWhiteSpace(brand.DisplayName) ? (string.IsNullOrWhiteSpace(name) ? "Player" : name) : brand.DisplayName,
                        BrandInfo = brand,
                        Title = string.IsNullOrWhiteSpace(title) ? "Audio Playing" : title,
                        Artist = string.IsNullOrWhiteSpace(artist) ? name : artist,
                        IsPlaying = isPlaying,
                        Artwork = art,
                        IsCurrent = string.Equals(aumid, currentAumid, StringComparison.OrdinalIgnoreCase)
                    });
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Media] GetActiveSessions failed: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// Cycles to the next available media session (e.g., switching between Spotify and a browser tab).
    /// Returns the friendly name of the newly selected media source, or <see langword="null"/> if fewer than 2 sessions exist.
    /// </summary>
    public string? CycleNextSession()
    {
        if (_manager is null) return null;
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try
        {
            sessions = _manager.GetSessions();
        }
        catch
        {
            return null;
        }

        if (sessions is null || sessions.Count <= 1) return null;

        string? currentId = _session?.SourceAppUserModelId;
        int currentIndex = -1;
        for (int i = 0; i < sessions.Count; i++)
        {
            if (string.Equals(sessions[i].SourceAppUserModelId, currentId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = (currentIndex + 1) % sessions.Count;
        var nextSession = sessions[nextIndex];
        _preferredSessionAumid = nextSession.SourceAppUserModelId;
        AttachSessionCore(nextSession, sessions.Count);
        return Current?.Source;
    }

    public bool FocusActivePlayerWindow()
    {
        string? source = Current?.Source;
        string? aumid = _session?.SourceAppUserModelId;
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(aumid))
            return false;

        try
        {
            string searchKey = ResolveProcessSearchKey(source, aumid);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    string procName = p.ProcessName;
                    string title = p.MainWindowTitle ?? "";

                    bool matched = (!string.IsNullOrEmpty(searchKey) &&
                                    (procName.Contains(searchKey, StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains(searchKey, StringComparison.OrdinalIgnoreCase))) ||
                                   (!string.IsNullOrEmpty(source) &&
                                    (procName.Contains(source, StringComparison.OrdinalIgnoreCase) ||
                                     title.Contains(source, StringComparison.OrdinalIgnoreCase)));

                    if (matched)
                    {
                        IntPtr hwnd = p.MainWindowHandle;
                        if (IsIconic(hwnd))
                        {
                            ShowWindow(hwnd, SW_RESTORE);
                        }
                        SetForegroundWindow(hwnd);
                        return true;
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }
        return false;
    }

    internal static string ResolveProcessSearchKey(string? source, string? aumid)
    {
        string combined = $"{source} {aumid}".ToLowerInvariant();
        if (combined.Contains("spotify")) return "Spotify";
        if (combined.Contains("applemusic") || combined.Contains("itunes")) return "AppleMusic";
        if (combined.Contains("youtubemusic") || combined.Contains("youtube music") || combined.Contains("ytmdesktop")) return "YouTube Music";
        if (combined.Contains("msedge") || combined.Contains("edge")) return "msedge";
        if (combined.Contains("chrome")) return "chrome";
        if (combined.Contains("firefox") || combined.Contains("308046b0af4a39cb")) return "firefox";
        if (combined.Contains("brave")) return "brave";
        if (combined.Contains("opera")) return "opera";
        if (combined.Contains("vlc")) return "vlc";
        if (combined.Contains("tidal")) return "TIDAL";
        if (combined.Contains("amazon")) return "Amazon Music";
        if (combined.Contains("foobar")) return "foobar2000";
        return source ?? "";
    }

    // ── Session tracking ─────────────────────────────────────────────

    private void AttachCurrentSession()
    {
        if (_manager is null) return;

        GlobalSystemMediaTransportControlsSession? session = null;
        int sessionCount = 1;
        try
        {
            var allSessions = _manager.GetSessions();
            sessionCount = Math.Max(1, allSessions?.Count ?? 1);
            if (!string.IsNullOrEmpty(_preferredSessionAumid) && allSessions != null)
            {
                session = allSessions.FirstOrDefault(s =>
                    string.Equals(s.SourceAppUserModelId, _preferredSessionAumid, StringComparison.OrdinalIgnoreCase));
            }
            session ??= _manager.GetCurrentSession();
        }
        catch
        {
            session = null;
        }

        string? newId = session?.SourceAppUserModelId;
        if (_session is not null && session is not null && newId == _session.SourceAppUserModelId)
        {
            if (Current is not null && Current.ActiveSessionCount != sessionCount)
            {
                Current.ActiveSessionCount = sessionCount;
                Changed?.Invoke();
            }
            return; // same app, keep existing subscriptions
        }

        AttachSessionCore(session, sessionCount);
    }

    private void AttachSessionCore(GlobalSystemMediaTransportControlsSession? session, int sessionCount)
    {
        DetachSession();
        _session = session;

        if (session is null)
        {
            Current = null;
            Changed?.Invoke();
            return;
        }

        string? newId = session.SourceAppUserModelId;
        var initialBrand = MediaPlayerBrandService.Detect(newId);
        Current = new MediaInfo
        {
            Source = initialBrand.DisplayName,
            BrandInfo = initialBrand,
            SourceAppUserModelId = newId ?? "",
            ActiveSessionCount = sessionCount
        };
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
        media.CanShuffle = controls.IsShuffleEnabled;
        media.CanRepeat = controls.IsRepeatEnabled;
        media.IsShuffleActive = info.IsShuffleActive;
        media.AutoRepeatMode = info.AutoRepeatMode;
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
        var dur = timeline.EndTime - timeline.StartTime;
        media.Duration = dur > TimeSpan.Zero ? dur : TimeSpan.Zero;
        var pos = timeline.Position - timeline.StartTime;
        media.Position = pos > TimeSpan.Zero ? pos : TimeSpan.Zero;
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
        string album = properties.AlbumTitle ?? "";
        bool trackChanged = title != media.Title || artist != media.Artist;

        media.Title = title;
        media.Artist = artist;
        media.AlbumTitle = album;
        media.TrackNumber = properties.TrackNumber;
        media.AlbumTrackCount = properties.AlbumTrackCount;

        // Refine brand detection using full track metadata (e.g. YouTube Music / Apple Music tab in browsers)
        var refinedBrand = MediaPlayerBrandService.Detect(media.SourceAppUserModelId, title, artist, album);
        if (refinedBrand.Brand != MediaPlayerBrand.Unknown)
        {
            media.BrandInfo = refinedBrand;
            media.Source = refinedBrand.DisplayName;
        }

        if (trackChanged && properties.Thumbnail is null)
        {
            media.Artwork = null;
            media.Accent = null;
            media.SecondaryAccent = null;
        }
        Changed?.Invoke();
        if (trackChanged && title.Length > 0) TrackChanged?.Invoke();

        if (properties.Thumbnail is { } thumbnail)
        {
            var (artwork, accent, secondary) = await Task.Run(() => LoadThumbnailAndAccentAsync(thumbnail));
            if (version != _propertiesVersion || !ReferenceEquals(media, Current)) return;
            media.Artwork = artwork;
            media.Accent = accent;
            media.SecondaryAccent = secondary;
            Changed?.Invoke();
        }
    }

    private static async Task<(BitmapSource? Artwork, Color? Accent, Color? Secondary)> LoadThumbnailAndAccentAsync(IRandomAccessStreamReference reference)
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
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 160;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();

            var (accent, secondary) = ColorExtractor.ExtractPalette(image);
            return (image, accent, secondary);
        }
        catch (Exception ex)
        {
            App.Log($"[Media] Thumbnail load failed: {ex.Message}");
            return (null, null, null);
        }
    }

    internal static string FriendlyAppName(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        var brand = MediaPlayerBrandService.Detect(aumid);
        if (brand.Brand != MediaPlayerBrand.Unknown && brand.Brand != MediaPlayerBrand.Browser)
            return brand.DisplayName;

        string id = aumid.ToLowerInvariant();
        if (id.Contains("msedge")) return "Edge";
        if (id.Contains("chrome")) return "Chrome";
        if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "Firefox";
        if (id.Contains("brave")) return "Brave";
        if (id.Contains("opera")) return "Opera";
        if (id.Contains("zunevideo")) return "Movies & TV";

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
