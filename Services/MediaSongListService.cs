using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Represents a track item fetched from the active album, artist catalog, or playlist.
/// </summary>
public sealed class SongTrackItem
{
    public int TrackNumber { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public string? PreviewUrl { get; init; }
    public string? ArtworkUrl { get; init; }

    public string DisplayTrackNumber => TrackNumber > 0 ? TrackNumber.ToString() : "•";
    public string FormattedDuration =>
        Duration > TimeSpan.Zero
            ? (Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss") : Duration.ToString(@"m\:ss"))
            : "--:--";
}

/// <summary>
/// Result container for fetched album tracklists.
/// </summary>
public sealed class AlbumTracklistResult
{
    public string AlbumName { get; init; } = "";
    public string ArtistName { get; init; } = "";
    public string? ArtworkUrl { get; init; }
    public IReadOnlyList<SongTrackItem> Tracks { get; init; } = Array.Empty<SongTrackItem>();
    public bool IsArtistTopTracks { get; init; }
}

/// <summary>
/// Historical record of a track played during this application session.
/// </summary>
public sealed class PlayedSongRecord
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string SourceApp { get; init; } = "";
    public BitmapSource? Artwork { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime PlayedAt { get; init; } = DateTime.Now;

    public string FormattedDuration =>
        Duration > TimeSpan.Zero
            ? (Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss") : Duration.ToString(@"m\:ss"))
            : "--:--";

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.Now - PlayedAt;
            if (diff.TotalSeconds < 45) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return PlayedAt.ToString("HH:mm");
        }
    }
}

/// <summary>
/// Comprehensive music intelligence service for the detached player:
/// 1. Fetches album tracklists and artist top tracks for whatever is currently playing (via iTunes Search API with zero auth).
/// 2. Records playback history of songs during the session.
/// 3. Coordinates with <see cref="MediaSessionService"/> to list and switch between all active Windows media sessions.
/// </summary>
internal sealed class MediaSongListService : IDisposable
{
    private static readonly HttpClient Http = SharedHttpClient.CreateClient(
        TimeSpan.FromSeconds(6.0),
        $"DynamicIsland/{InstallerService.AppVersion} (https://github.com/jainis-Patel/DynamicIsland)");

    private static readonly Regex VideoTagsRegex = new(
        @"\s*[\(\[](?:official\s*(?:music\s*video|video|audio|lyric\s*video|visualizer|4k|hd|remastered)?|lyrics?|lyric\s*video|audio|visualizer|remastered(?:\s*\d{4})?|deluxe(?:\s*edition)?|bonus\s*track|explicit|clean\s*version|live(?:\s*at.*?)?|acoustic(?:\s*version)?|4k|1080p|hd|mv|m/v|feat\.?|ft\.?|featuring|prod\.?|with).*?[\)\]]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AsianBracketsRegex = new(
        @"[【「『\[](?:MV|Official|Lyrics?|Audio|HD|4K|Video).*?[】」』\]]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TitleSuffixRegex = new(
        @"\s*-\s*(?:remastered|radio\s*edit|single\s*version|live|bonus\s*track).*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FeatRegex = new(
        @"\s*(?:feat\.|ft\.|featuring|with|prod\.?|prod\s+by).*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> GenericAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube", "brave", "chrome", "google chrome", "msedge", "microsoft edge", "edge",
        "firefox", "mozilla firefox", "opera", "opera gx", "vivaldi", "safari",
        "soundcloud", "spotify", "system audio", "unknown artist", "unknown",
        "media", "media player", "vlc", "windows media player", "groove music"
    };

    private const int MaxHistoryItems = 50;
    private readonly Dispatcher _ui;
    private readonly MediaSessionService _mediaService;
    private readonly string _historyFilePath;
    private readonly DebouncedJsonStore<List<PersistedSongHistoryItem>> _historyStore;
    private readonly ConcurrentDictionary<string, AlbumTracklistResult> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PlayedSongRecord> _history = new();
    private readonly object _historyLock = new();

    private sealed record PersistedSongHistoryItem(
        string Title,
        string Artist,
        string Album,
        string SourceApp,
        long DurationTicks,
        DateTime PlayedAt,
        string? ArtworkJpegBase64 = null);

    public IReadOnlyList<PlayedSongRecord> History
    {
        get
        {
            lock (_historyLock) return _history.ToList();
        }
    }

    public event Action? HistoryChanged;

    public MediaSongListService(Dispatcher ui, MediaSessionService mediaService, string? historyFilePath = null)
    {
        _ui = ui;
        _mediaService = mediaService;
        _historyFilePath = string.IsNullOrWhiteSpace(historyFilePath)
            ? Path.Combine(AppPaths.DataDirectory, "playback_history.json")
            : historyFilePath;
        _historyStore = new DebouncedJsonStore<List<PersistedSongHistoryItem>>(
            _historyFilePath,
            BuildHistorySnapshot,
            delayMs: 500);

        LoadHistoryFromDisk();
        _mediaService.TrackChanged += OnTrackChanged;
        RecordCurrentTrack();
    }

    private void OnTrackChanged() => _ui.InvokeAsync(RecordCurrentTrack);

    private void RecordCurrentTrack()
    {
        var current = _mediaService.Current;
        if (current == null || string.IsNullOrWhiteSpace(current.Title)) return;

        RecordTrackCore(
            current.Title,
            current.Artist ?? "",
            current.AlbumTitle ?? "",
            current.Source,
            current.Duration,
            current.Artwork);
    }

    internal void RecordTrackCore(
        string title,
        string artist,
        string album,
        string sourceApp,
        TimeSpan duration,
        BitmapSource? artwork,
        DateTime? playedAt = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        string cleanTitle = title.Trim();
        string cleanArtist = artist?.Trim() ?? "";
        string cleanAlbum = album?.Trim() ?? "";
        string normalizedArtist = string.IsNullOrWhiteSpace(cleanArtist) ? "Unknown Artist" : cleanArtist;

        lock (_historyLock)
        {
            if (_history.Count > 0)
            {
                var last = _history[0];
                if (string.Equals(last.Title, cleanTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(last.Artist, normalizedArtist, StringComparison.OrdinalIgnoreCase))
                {
                    return; // Avoid duplicating immediate repeats
                }
            }

            _history.Insert(0, new PlayedSongRecord
            {
                Title = cleanTitle,
                Artist = normalizedArtist,
                Album = cleanAlbum,
                SourceApp = sourceApp,
                Artwork = artwork,
                Duration = duration,
                PlayedAt = playedAt ?? DateTime.Now
            });

            if (_history.Count > MaxHistoryItems)
            {
                _history.RemoveRange(MaxHistoryItems, _history.Count - MaxHistoryItems);
            }
        }

        _historyStore.ScheduleSave();
        HistoryChanged?.Invoke();
    }

    public void ClearHistory()
    {
        lock (_historyLock)
        {
            if (_history.Count == 0) return;
            _history.Clear();
        }
        _historyStore.ScheduleSave();
        HistoryChanged?.Invoke();
    }

    private List<PersistedSongHistoryItem> BuildHistorySnapshot()
    {
        List<PlayedSongRecord> copy;
        lock (_historyLock)
        {
            copy = _history.Take(MaxHistoryItems).ToList();
        }

        var snapshot = new List<PersistedSongHistoryItem>(copy.Count);
        foreach (var rec in copy)
        {
            snapshot.Add(new PersistedSongHistoryItem(
                rec.Title,
                rec.Artist,
                rec.Album,
                rec.SourceApp,
                rec.Duration.Ticks,
                rec.PlayedAt,
                EncodeArtworkThumbnail(rec.Artwork)));
        }
        return snapshot;
    }

    private void LoadHistoryFromDisk()
    {
        try
        {
            var items = AtomicJsonStore.Load(_historyFilePath, () => new List<PersistedSongHistoryItem>());
            if (items.Count == 0) return;

            lock (_historyLock)
            {
                _history.Clear();
                foreach (var item in items.Take(MaxHistoryItems))
                {
                    if (string.IsNullOrWhiteSpace(item.Title)) continue;
                    _history.Add(new PlayedSongRecord
                    {
                        Title = item.Title,
                        Artist = string.IsNullOrWhiteSpace(item.Artist) ? "Unknown Artist" : item.Artist,
                        Album = item.Album ?? "",
                        SourceApp = item.SourceApp ?? "",
                        Artwork = DecodeArtworkThumbnail(item.ArtworkJpegBase64),
                        Duration = item.DurationTicks > 0 ? TimeSpan.FromTicks(item.DurationTicks) : TimeSpan.Zero,
                        PlayedAt = item.PlayedAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[MediaSongList] LoadHistoryFromDisk failed: {ex.Message}");
        }
    }

    private static string? EncodeArtworkThumbnail(BitmapSource? src)
    {
        if (src == null || src.PixelWidth <= 0 || src.PixelHeight <= 0) return null;
        try
        {
            double maxDim = Math.Max(src.PixelWidth, src.PixelHeight);
            BitmapSource target = src;
            if (maxDim > 40.0)
            {
                double scale = 40.0 / maxDim;
                target = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            }
            var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? DecodeArtworkThumbnail(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches the track listing for the album or artist of the currently playing track.
    /// </summary>
    public async Task<AlbumTracklistResult?> FetchSongListAsync(string rawTitle, string rawArtist, string rawAlbum = "", CancellationToken ct = default)
    {
        string cleanTitle = CleanMetadata(rawTitle);
        string cleanArtist = CleanArtist(rawArtist, rawTitle);

        if (string.IsNullOrWhiteSpace(cleanTitle) && string.IsNullOrWhiteSpace(cleanArtist))
            return null;

        string cacheKey = $"{cleanArtist}::{cleanTitle}::{rawAlbum}".ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        // 1. Search by Title + Artist or Album
        AlbumTracklistResult? result = null;
        try
        {
            long collectionId = 0;
            string collectionName = "";
            string artistName = "";
            string artworkUrl = "";

            string searchTerm = !string.IsNullOrWhiteSpace(rawAlbum) && !GenericAppNames.Contains(rawAlbum)
                ? $"{rawAlbum} {cleanArtist}"
                : $"{cleanTitle} {cleanArtist}";

            string searchUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(searchTerm)}&entity=song&limit=1";
            using (var resp = await Http.GetAsync(searchUrl, ct).ConfigureAwait(false))
            {
                if (resp.IsSuccessStatusCode)
                {
                    using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        if (first.TryGetProperty("collectionId", out var colIdProp))
                            collectionId = colIdProp.GetInt64();
                        if (first.TryGetProperty("collectionName", out var colNameProp))
                            collectionName = colNameProp.GetString() ?? "";
                        if (first.TryGetProperty("artistName", out var artNameProp))
                            artistName = artNameProp.GetString() ?? "";
                        if (first.TryGetProperty("artworkUrl100", out var artProp))
                            artworkUrl = artProp.GetString() ?? "";
                    }
                }
            }

            // 2. Lookup Album Tracks by collectionId
            if (collectionId > 0)
            {
                string lookupUrl = $"https://itunes.apple.com/lookup?id={collectionId}&entity=song";
                using var lookupResp = await Http.GetAsync(lookupUrl, ct).ConfigureAwait(false);
                if (lookupResp.IsSuccessStatusCode)
                {
                    using var lookupStream = await lookupResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var lookupDoc = await JsonDocument.ParseAsync(lookupStream, cancellationToken: ct).ConfigureAwait(false);
                    var lRoot = lookupDoc.RootElement;
                    if (lRoot.TryGetProperty("results", out var items))
                    {
                        var trackList = new List<SongTrackItem>();
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("wrapperType", out var wrapper) && wrapper.GetString() == "track")
                            {
                                int num = item.TryGetProperty("trackNumber", out var numProp) ? numProp.GetInt32() : 0;
                                string tName = item.TryGetProperty("trackName", out var tProp) ? tProp.GetString() ?? "" : "";
                                string aName = item.TryGetProperty("artistName", out var aProp) ? aProp.GetString() ?? "" : artistName;
                                string cName = item.TryGetProperty("collectionName", out var cProp) ? cProp.GetString() ?? "" : collectionName;
                                int timeMs = item.TryGetProperty("trackTimeMillis", out var msProp) ? msProp.GetInt32() : 0;
                                string? prevUrl = item.TryGetProperty("previewUrl", out var pProp) ? pProp.GetString() : null;
                                string? art100 = item.TryGetProperty("artworkUrl100", out var art100Prop) ? art100Prop.GetString() : artworkUrl;

                                if (!string.IsNullOrWhiteSpace(tName))
                                {
                                    trackList.Add(new SongTrackItem
                                    {
                                        TrackNumber = num,
                                        Title = tName,
                                        Artist = aName,
                                        Album = cName,
                                        Duration = TimeSpan.FromMilliseconds(timeMs),
                                        PreviewUrl = prevUrl,
                                        ArtworkUrl = art100
                                    });
                                }
                            }
                        }

                        if (trackList.Count > 0)
                        {
                            result = new AlbumTracklistResult
                            {
                                AlbumName = string.IsNullOrWhiteSpace(collectionName) ? "Album Tracks" : collectionName,
                                ArtistName = artistName,
                                ArtworkUrl = artworkUrl,
                                Tracks = trackList.OrderBy(t => t.TrackNumber).ToList(),
                                IsArtistTopTracks = false
                            };
                        }
                    }
                }
            }

            // 3. Fallback: Search Artist Popular Songs if album lookup was empty
            if (result == null && !string.IsNullOrWhiteSpace(cleanArtist) && !GenericAppNames.Contains(cleanArtist))
            {
                string artistSearchUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(cleanArtist)}&entity=song&limit=15";
                using var artResp = await Http.GetAsync(artistSearchUrl, ct).ConfigureAwait(false);
                if (artResp.IsSuccessStatusCode)
                {
                    using var artStream = await artResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var artDoc = await JsonDocument.ParseAsync(artStream, cancellationToken: ct).ConfigureAwait(false);
                    var aRoot = artDoc.RootElement;
                    if (aRoot.TryGetProperty("results", out var artItems))
                    {
                        var trackList = new List<SongTrackItem>();
                        int counter = 1;
                        foreach (var item in artItems.EnumerateArray())
                        {
                            string tName = item.TryGetProperty("trackName", out var tProp) ? tProp.GetString() ?? "" : "";
                            string aName = item.TryGetProperty("artistName", out var aProp) ? aProp.GetString() ?? "" : cleanArtist;
                            string cName = item.TryGetProperty("collectionName", out var cProp) ? cProp.GetString() ?? "" : "";
                            int timeMs = item.TryGetProperty("trackTimeMillis", out var msProp) ? msProp.GetInt32() : 0;
                            string? prevUrl = item.TryGetProperty("previewUrl", out var pProp) ? pProp.GetString() : null;
                            string? art100 = item.TryGetProperty("artworkUrl100", out var art100Prop) ? art100Prop.GetString() : null;

                            if (!string.IsNullOrWhiteSpace(tName))
                            {
                                trackList.Add(new SongTrackItem
                                {
                                    TrackNumber = counter++,
                                    Title = tName,
                                    Artist = aName,
                                    Album = cName,
                                    Duration = TimeSpan.FromMilliseconds(timeMs),
                                    PreviewUrl = prevUrl,
                                    ArtworkUrl = art100
                                });
                            }
                        }

                        if (trackList.Count > 0)
                        {
                            result = new AlbumTracklistResult
                            {
                                AlbumName = $"{cleanArtist} · Top Songs",
                                ArtistName = cleanArtist,
                                ArtworkUrl = trackList[0].ArtworkUrl,
                                Tracks = trackList,
                                IsArtistTopTracks = true
                            };
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[MediaSongList] Fetch failed: {ex.Message}");
        }

        if (result != null)
        {
            _cache[cacheKey] = result;
        }

        return result;
    }

    internal static string CleanMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim();

        s = AsianBracketsRegex.Replace(s, "");
        s = VideoTagsRegex.Replace(s, "");
        s = TitleSuffixRegex.Replace(s, "");
        s = FeatRegex.Replace(s, "");

        // Split "Artist - Title" if formatted as combined string
        int dashIdx = s.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 0 && dashIdx < s.Length - 3)
        {
            s = s[(dashIdx + 3)..];
        }

        return s.Trim(' ', '-', '·', '|', ':', '—');
    }

    internal static string CleanArtist(string? rawArtist, string? rawTitle)
    {
        if (!string.IsNullOrWhiteSpace(rawArtist) && !GenericAppNames.Contains(rawArtist.Trim()))
        {
            string clean = FeatRegex.Replace(rawArtist.Trim(), "");
            return clean.Trim(' ', '-', '·', ',');
        }

        // Try extracting artist from "Artist - Title" in rawTitle
        if (!string.IsNullOrWhiteSpace(rawTitle))
        {
            int dashIdx = rawTitle.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0)
            {
                string parsed = rawTitle[..dashIdx].Trim();
                if (!GenericAppNames.Contains(parsed))
                    return parsed;
            }
        }

        return rawArtist?.Trim() ?? "";
    }

    public void Dispose()
    {
        _mediaService.TrackChanged -= OnTrackChanged;
        _historyStore.Dispose();
    }
}