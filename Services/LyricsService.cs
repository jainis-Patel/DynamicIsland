using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Represents a single timestamped line in an LRC synchronized lyrics document.
/// </summary>
public sealed record LrcLine(TimeSpan Timestamp, string Text);

/// <summary>
/// Resolved lyrics data for a music track, including synchronized lines and instrumental flag.
/// </summary>
public sealed class TrackLyrics
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public bool IsInstrumental { get; init; }
    public string? PlainLyrics { get; init; }
    public IReadOnlyList<LrcLine> SyncedLines { get; init; } = Array.Empty<LrcLine>();
    public bool HasSyncedLyrics => SyncedLines.Count > 0;
}

/// <summary>
/// High-performance lyrics service integrating with the open-source LRCLIB database (https://lrclib.net).
/// Features concurrent multi-tier hedged requests, smart YouTube/Brave browser title-artist metadata parsing,
/// persistent disk + memory caching, and real-time synchronization with playback position.
/// </summary>
internal sealed class LyricsService : IDisposable
{
    private static readonly HttpClient Http = CreateLrcLibHttpClient();

    private static HttpClient CreateLrcLibHttpClient()
    {
        var client = SharedHttpClient.CreateClient(
            TimeSpan.FromSeconds(5.0),
            $"DynamicIsland/{InstallerService.AppVersion} (https://github.com/jainis-Patel/DynamicIsland)");
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "L-User-Agent",
            $"DynamicIsland/{InstallerService.AppVersion} (https://github.com/jainis-Patel/DynamicIsland)");
        return client;
    }

    private static readonly Regex LrcTimestampRegex = new(
        @"^\[(\d{1,3}):(\d{2})(?:\.(\d{1,3}))?\]",
        RegexOptions.Compiled);

    private static readonly HashSet<string> GenericArtists = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube", "brave", "chrome", "google chrome", "msedge", "microsoft edge", "edge",
        "firefox", "mozilla firefox", "opera", "opera gx", "vivaldi", "safari",
        "soundcloud", "spotify", "system audio", "unknown artist", "unknown",
        "media", "media player", "vlc", "windows media player", "groove music"
    };

    private static readonly Regex VideoTagsRegex = new(
        @"\s*[\(\[](?:official\s*(?:music\s*video|video|audio|lyric\s*video|visualizer|4k|hd|remastered)?|lyrics?|lyric\s*video|audio|visualizer|remastered(?:\s*\d{4})?|deluxe(?:\s*edition)?|bonus\s*track|explicit|clean\s*version|live(?:\s*at.*?)?|acoustic(?:\s*version)?|4k|1080p|hd|mv|m/v|feat\.?|ft\.?|featuring|prod\.?|with|sped\s*up|slowed(?:\s*\+\s*reverb)?|slowed\s*down|nightcore|reverb|tiktok(?:\s*version)?|edit|extended(?:\s*mix)?|original\s*mix|radio\s*edit|remix).*?[\)\]]",
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

    private const int MaxDiskCacheEntries = 120;
    private readonly Dispatcher _ui;
    private readonly string _diskCachePath;
    private readonly ConcurrentDictionary<string, TrackLyrics> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DebouncedJsonStore<List<CachedLyricsEntry>> _diskCacheStore;
    private CancellationTokenSource? _fetchCts;
    private string _currentTrackKey = "";
    private int _lastActiveIndex = -2;
    private volatile bool _disposed;

    public TrackLyrics? CurrentTrack { get; private set; }
    public bool IsLoading { get; private set; }
    public int CurrentIndex { get; private set; } = -1;
    public string PreviousLineText { get; private set; } = "";
    public string CurrentLineText { get; private set; } = "";
    public string NextLineText { get; private set; } = "";

    public bool HasLyrics => CurrentTrack?.HasSyncedLyrics ?? false;
    public bool IsInstrumental => CurrentTrack?.IsInstrumental ?? false;

    public event Action? LyricsLoaded;
    public event Action<string, string, string, int>? LineChanged;

    private sealed record CachedLyricsEntry(
        string Key,
        string Title,
        string Artist,
        bool IsInstrumental,
        List<CachedLineDto> Lines);

    private sealed record CachedLineDto(long Ticks, string Text);

    public LyricsService(Dispatcher ui)
    {
        _ui = ui;
        _diskCachePath = Path.Combine(AppPaths.DataDirectory, "lyrics_cache.json");
        _diskCacheStore = new DebouncedJsonStore<List<CachedLyricsEntry>>(_diskCachePath, BuildDiskCacheSnapshot, delayMs: 500);
        _ = Task.Run(LoadDiskCache);
    }

    /// <summary>
    /// Loads or fetches lyrics for the given track. If already cached, completes synchronously in 0ms.
    /// Avoids cancelling in-flight requests when GSMTC emits duplicate metadata/timeline updates for the same song.
    /// </summary>
    public void LoadLyricsForTrack(string title, string artist, TimeSpan duration, bool forceRetry = false)
    {
        if (string.IsNullOrWhiteSpace(title) || string.Equals(title, "Not Playing", StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        var (cleanTitle, cleanArtist) = ParseTrackMetadata(title, artist);
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            cleanTitle = title.Trim();
        }

        string cacheKey = $"{cleanArtist}|{cleanTitle}";

        if (forceRetry)
        {
            _cache.TryRemove(cacheKey, out _);
            _currentTrackKey = "";
        }
        else if (string.Equals(_currentTrackKey, cacheKey, StringComparison.OrdinalIgnoreCase) &&
                 (IsLoading || CurrentTrack != null))
        {
            return;
        }

        _currentTrackKey = cacheKey;
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = new CancellationTokenSource();
        var ct = _fetchCts.Token;

        if (!forceRetry && _cache.TryGetValue(cacheKey, out var cached))
        {
            IsLoading = false;
            TrackLyrics? effective = (cached.HasSyncedLyrics || cached.IsInstrumental || !string.IsNullOrEmpty(cached.PlainLyrics))
                ? cached
                : null;
            SetCurrentTrack(effective);
            return;
        }

        CurrentTrack = null;
        IsLoading = true;
        LyricsLoaded?.Invoke();

        _ = Task.Run(async () =>
        {
            TrackLyrics? result = null;
            bool gotServerResponse = false;
            try
            {
                (result, gotServerResponse) = await FetchFromLrcLibAsync(cleanTitle, cleanArtist, duration, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Log($"[Lyrics] Fetch error for '{cleanTitle}' by '{cleanArtist}': {ex.Message}");
            }

            if (!ct.IsCancellationRequested && !_disposed)
            {
                if (result != null || gotServerResponse)
                {
                    _cache[cacheKey] = result ?? new TrackLyrics
                    {
                        Title = cleanTitle,
                        Artist = cleanArtist
                    };
                    if (result is { HasSyncedLyrics: true })
                    {
                        _diskCacheStore.ScheduleSave();
                    }
                }

                _ = _ui.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested && !_disposed && string.Equals(_currentTrackKey, cacheKey, StringComparison.OrdinalIgnoreCase))
                    {
                        IsLoading = false;
                        SetCurrentTrack(result);
                    }
                });
            }
        }, ct);
    }

    /// <summary>
    /// Synchronizes playback position with the loaded LRC lines. Dispatches <see cref="LineChanged"/>
    /// when the current sung line advances. Displays the first upcoming line during song intros.
    /// </summary>
    public void UpdatePosition(TimeSpan position)
    {
        if (CurrentTrack == null || !CurrentTrack.HasSyncedLyrics)
        {
            if (CurrentIndex != -1)
            {
                CurrentIndex = -1;
                PreviousLineText = "";
                CurrentLineText = CurrentTrack?.IsInstrumental == true
                    ? "\u266A Instrumental Track"
                    : "\u266A Synced lyrics unavailable";
                NextLineText = "";
                LineChanged?.Invoke("", CurrentLineText, "", -1);
            }
            return;
        }

        var lines = CurrentTrack.SyncedLines;
        int index = FindCurrentIndex(lines, position);

        if (index != _lastActiveIndex)
        {
            _lastActiveIndex = index;
            CurrentIndex = index;

            if (index < 0 && lines.Count > 0)
            {
                // Song is in its intro before the first vocal timestamp: preview first upcoming line immediately
                PreviousLineText = "";
                CurrentLineText = $"\u266A {lines[0].Text}";
                NextLineText = lines.Count > 1 ? lines[1].Text : "";
            }
            else
            {
                PreviousLineText = index > 0 ? lines[index - 1].Text : "";
                CurrentLineText = index >= 0 && index < lines.Count ? lines[index].Text : "";
                NextLineText = index + 1 < lines.Count ? lines[index + 1].Text : "";
            }

            LineChanged?.Invoke(PreviousLineText, CurrentLineText, NextLineText, index);
        }
    }

    /// <summary>
    /// Locates the active line index for a given playback position with a 120ms singing lead-in via binary search.
    /// </summary>
    public static int FindCurrentIndex(IReadOnlyList<LrcLine>? lines, TimeSpan position)
    {
        if (lines == null || lines.Count == 0) return -1;
        var effectivePos = position + TimeSpan.FromMilliseconds(120);

        int lo = 0;
        int hi = lines.Count - 1;
        int answer = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (lines[mid].Timestamp <= effectivePos)
            {
                answer = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return answer;
    }

    public void Reset() => Clear();

    public void Clear()
    {
        _fetchCts?.Cancel();
        _currentTrackKey = "";
        IsLoading = false;
        SetCurrentTrack(null);
    }

    private void SetCurrentTrack(TrackLyrics? track)
    {
        CurrentTrack = track;
        _lastActiveIndex = -2;
        CurrentIndex = -1;

        if (track is { HasSyncedLyrics: true } && track.SyncedLines.Count > 0)
        {
            PreviousLineText = "";
            CurrentLineText = $"\u266A {track.SyncedLines[0].Text}";
            NextLineText = track.SyncedLines.Count > 1 ? track.SyncedLines[1].Text : "";
        }
        else
        {
            PreviousLineText = "";
            CurrentLineText = "";
            NextLineText = "";
        }

        LyricsLoaded?.Invoke();
        LineChanged?.Invoke(PreviousLineText, CurrentLineText, NextLineText, CurrentIndex);
    }

    /// <summary>
    /// Concurrent multi-tier fetching strategy querying LRCLIB in parallel:
    /// Fires Tier 1 (/api/get), Tier 2 (/api/search by fields), and Tier 3 (/api/search?q=...) simultaneously
    /// so browser/YouTube/Spotify tracks resolve in a single network round-trip (~250-450ms) instead of 3-5 sequential timeouts.
    /// </summary>
    private static async Task<(TrackLyrics? Track, bool ServerResponded)> FetchFromLrcLibAsync(string title, string artist, TimeSpan duration, CancellationToken ct)
    {
        bool hasArtist = !string.IsNullOrWhiteSpace(artist);
        using var tier1Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tierToken = tier1Cts.Token;

        string query = hasArtist ? $"{artist} {title}".Trim() : title.Trim();
        string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}";

        Task<(TrackLyrics? Track, bool ServerResponded)>? directTask = null;
        Task<(TrackLyrics? Track, bool ServerResponded)>? fieldTask = null;

        if (hasArtist)
        {
            string directUrl = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            string fieldSearchUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            directTask = QueryLrcLibSingleAsync(directUrl, title, artist, duration, tierToken);
            fieldTask = QueryLrcLibArrayAsync(fieldSearchUrl, title, artist, duration, tierToken);
        }

        Task<(TrackLyrics? Track, bool ServerResponded)> queryTask = QueryLrcLibArrayAsync(searchUrl, title, artist, duration, tierToken);

        TrackLyrics? direct = null;
        TrackLyrics? fieldMatch = null;
        bool anyServerResponse = false;

        if (directTask != null)
        {
            bool responded;
            (direct, responded) = await directTask;
            anyServerResponse |= responded;
            if (direct?.HasSyncedLyrics == true)
            {
                tier1Cts.Cancel();
                return (direct, true);
            }
        }

        if (fieldTask != null)
        {
            bool responded;
            (fieldMatch, responded) = await fieldTask;
            anyServerResponse |= responded;
            if (fieldMatch?.HasSyncedLyrics == true)
            {
                tier1Cts.Cancel();
                return (fieldMatch, true);
            }
        }

        var (queryMatch, queryResponded) = await queryTask;
        anyServerResponse |= queryResponded;
        if (queryMatch?.HasSyncedLyrics == true)
        {
            return (queryMatch, true);
        }

        // Secondary fallback wave: Collaboration artist & title-only search launched in parallel
        using var tier2Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tier2Token = tier2Cts.Token;
        Task<(TrackLyrics? Track, bool ServerResponded)>? collabSearchTask = null;

        if (hasArtist && (artist.Contains('&') || artist.Contains(',') || artist.Contains("feat", StringComparison.OrdinalIgnoreCase)))
        {
            string primaryArtist = artist.Split(new[] { '&', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            primaryArtist = FeatRegex.Replace(primaryArtist, "").Trim();
            if (!string.IsNullOrWhiteSpace(primaryArtist) && !string.Equals(primaryArtist, artist, StringComparison.OrdinalIgnoreCase))
            {
                string collabSearch = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(primaryArtist + " " + title)}";
                collabSearchTask = QueryLrcLibArrayAsync(collabSearch, title, primaryArtist, duration, tier2Token);
            }
        }

        string titleOnlyUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(title)}";
        var titleOnlyTask = QueryLrcLibArrayAsync(titleOnlyUrl, title, artist, duration, tier2Token);

        if (collabSearchTask != null)
        {
            var (collabMatch, collabResponded) = await collabSearchTask;
            anyServerResponse |= collabResponded;
            if (collabMatch?.HasSyncedLyrics == true)
            {
                tier2Cts.Cancel();
                return (collabMatch, true);
            }
        }

        var (titleMatch, titleResponded) = await titleOnlyTask;
        anyServerResponse |= titleResponded;
        if (titleMatch?.HasSyncedLyrics == true)
        {
            return (titleMatch, true);
        }

        // Secondary Fallback: Strip any remaining bracketed or parenthesized tags (e.g. "(Sped up)", "[Lyrics]")
        string strippedTitle = Regex.Replace(title, @"\s*[\(\[].*?[\)\]]", "").Trim();
        if (!string.IsNullOrWhiteSpace(strippedTitle) && !string.Equals(strippedTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            string strippedQuery = hasArtist ? $"{artist} {strippedTitle}".Trim() : strippedTitle;
            string strippedUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(strippedQuery)}";
            var (strippedMatch, strippedResponded) = await QueryLrcLibArrayAsync(strippedUrl, strippedTitle, artist, duration, ct);
            anyServerResponse |= strippedResponded;
            if (strippedMatch?.HasSyncedLyrics == true)
            {
                return (strippedMatch, true);
            }
        }

        // Tertiary Fallback: Strip punctuation (e.g. "?", "!", quotes)
        string punctStrippedTitle = Regex.Replace(title, @"[?!\'\""]", "").Trim();
        if (!string.IsNullOrWhiteSpace(punctStrippedTitle) && !string.Equals(punctStrippedTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            string punctQuery = hasArtist ? $"{artist} {punctStrippedTitle}".Trim() : punctStrippedTitle;
            string punctUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(punctQuery)}";
            var (punctMatch, punctResponded) = await QueryLrcLibArrayAsync(punctUrl, punctStrippedTitle, artist, duration, ct);
            anyServerResponse |= punctResponded;
            if (punctMatch?.HasSyncedLyrics == true)
            {
                return (punctMatch, true);
            }
        }

        return (direct ?? fieldMatch ?? queryMatch ?? titleMatch, anyServerResponse);
    }

    private static async Task<(TrackLyrics? Track, bool ServerResponded)> QueryLrcLibSingleAsync(string url, string fallbackTitle, string fallbackArtist, TimeSpan duration, CancellationToken ct)
    {
        try
        {
            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(TimeSpan.FromSeconds(3.2));
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, reqCts.Token);
            bool responded = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(reqCts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: reqCts.Token);
                return (ParseLrcLibJson(doc.RootElement, fallbackTitle, fallbackArtist, duration), true);
            }
            return (null, responded);
        }
        catch
        {
            // Network or parsing failures handled gracefully
            return (null, false);
        }
    }

    private static async Task<(TrackLyrics? Track, bool ServerResponded)> QueryLrcLibArrayAsync(string url, string fallbackTitle, string fallbackArtist, TimeSpan duration, CancellationToken ct)
    {
        try
        {
            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(TimeSpan.FromSeconds(3.5));
            using var searchResp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, reqCts.Token);
            bool responded = searchResp.IsSuccessStatusCode || searchResp.StatusCode == HttpStatusCode.NotFound;
            if (searchResp.IsSuccessStatusCode)
            {
                using var searchStream = await searchResp.Content.ReadAsStreamAsync(reqCts.Token);
                using var searchDoc = await JsonDocument.ParseAsync(searchStream, cancellationToken: reqCts.Token);
                if (searchDoc.RootElement.ValueKind == JsonValueKind.Array && searchDoc.RootElement.GetArrayLength() > 0)
                {
                    // Prefer items with synchronized lyrics
                    var best = searchDoc.RootElement.EnumerateArray()
                        .FirstOrDefault(e => e.TryGetProperty("syncedLyrics", out var syn) &&
                                            syn.ValueKind == JsonValueKind.String &&
                                            !string.IsNullOrWhiteSpace(syn.GetString()));

                    if (best.ValueKind != JsonValueKind.Undefined)
                    {
                        return (ParseLrcLibJson(best, fallbackTitle, fallbackArtist, duration), true);
                    }

                    // Fallback to plain lyrics if synced not available
                    var plain = searchDoc.RootElement.EnumerateArray()
                        .FirstOrDefault(e => e.TryGetProperty("plainLyrics", out var pl) &&
                                            pl.ValueKind == JsonValueKind.String &&
                                            !string.IsNullOrWhiteSpace(pl.GetString()));

                    if (plain.ValueKind != JsonValueKind.Undefined)
                    {
                        return (ParseLrcLibJson(plain, fallbackTitle, fallbackArtist, duration), true);
                    }
                }
            }
            return (null, responded);
        }
        catch
        {
            // Network or parsing failures handled gracefully
            return (null, false);
        }
    }

    private static TrackLyrics ParseLrcLibJson(JsonElement el, string fallbackTitle, string fallbackArtist, TimeSpan duration)
    {
        string title = fallbackTitle;
        if (el.TryGetProperty("trackName", out var tn) && tn.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tn.GetString()))
        {
            title = tn.GetString()!;
        }
        else if (el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
        {
            title = n.GetString()!;
        }

        string artist = fallbackArtist;
        if (el.TryGetProperty("artistName", out var an) && an.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(an.GetString()))
        {
            artist = an.GetString()!;
        }
        else if (el.TryGetProperty("artist", out var a) && a.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(a.GetString()))
        {
            artist = a.GetString()!;
        }

        bool instrumental = el.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True;
        string? synced = el.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String ? sl.GetString() : null;
        string? plain = el.TryGetProperty("plainLyrics", out var pl) && pl.ValueKind == JsonValueKind.String ? pl.GetString() : null;

        var parsedLines = ParseLrc(synced);
        if (parsedLines.Count == 0 && !string.IsNullOrWhiteSpace(plain))
        {
            TimeSpan effectiveDur = duration > TimeSpan.FromSeconds(15) ? duration : TimeSpan.FromMinutes(3.2);
            if (el.TryGetProperty("duration", out var durProp) && durProp.ValueKind == JsonValueKind.Number && durProp.TryGetDouble(out double dSec) && dSec > 15)
            {
                effectiveDur = TimeSpan.FromSeconds(dSec);
            }
            parsedLines = SynthesizeLinesFromPlain(plain, effectiveDur);
        }

        return new TrackLyrics
        {
            Title = title,
            Artist = artist,
            IsInstrumental = instrumental,
            PlainLyrics = plain,
            SyncedLines = parsedLines
        };
    }

    private static List<LrcLine> SynthesizeLinesFromPlain(string plainLyrics, TimeSpan duration)
    {
        var rawLines = plainLyrics
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('['))
            .ToList();

        var result = new List<LrcLine>(rawLines.Count);
        if (rawLines.Count == 0) return result;

        double totalSec = Math.Max(30.0, duration.TotalSeconds * 0.90);
        double stepSec = totalSec / Math.Max(1, rawLines.Count);
        for (int i = 0; i < rawLines.Count; i++)
        {
            result.Add(new LrcLine(TimeSpan.FromSeconds(i * stepSec), rawLines[i]));
        }
        return result;
    }

    private void LoadDiskCache()
    {
        try
        {
            if (!File.Exists(_diskCachePath)) return;
            string json = File.ReadAllText(_diskCachePath);
            var entries = JsonSerializer.Deserialize<List<CachedLyricsEntry>>(json);
            if (entries == null) return;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Lines == null || entry.Lines.Count == 0) continue;
                _cache[entry.Key] = new TrackLyrics
                {
                    Title = entry.Title,
                    Artist = entry.Artist,
                    IsInstrumental = entry.IsInstrumental,
                    SyncedLines = entry.Lines.Select(l => new LrcLine(TimeSpan.FromTicks(l.Ticks), l.Text)).ToList()
                };
            }
        }
        catch { }
    }

    private List<CachedLyricsEntry> BuildDiskCacheSnapshot()
    {
        return _cache
            .Where(kv => kv.Value.HasSyncedLyrics)
            .Take(MaxDiskCacheEntries)
            .Select(kv => new CachedLyricsEntry(
                kv.Key,
                kv.Value.Title,
                kv.Value.Artist,
                kv.Value.IsInstrumental,
                kv.Value.SyncedLines.Select(l => new CachedLineDto(l.Timestamp.Ticks, l.Text)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Parses LRC content into ordered timestamps and cleaned line strings.
    /// Supports multi-timestamp lines (e.g. [00:10.00][00:30.00]Text) and filters out metadata tags.
    /// </summary>
    public static List<LrcLine> ParseLrc(string? lrcContent)
    {
        var result = new List<LrcLine>();
        if (string.IsNullOrWhiteSpace(lrcContent)) return result;

        var timestamps = new List<TimeSpan>(2);
        using var reader = new StringReader(lrcContent);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            timestamps.Clear();
            while (true)
            {
                var match = LrcTimestampRegex.Match(line);
                if (!match.Success) break;

                if (int.TryParse(match.Groups[1].Value, out int minutes) &&
                    int.TryParse(match.Groups[2].Value, out int seconds))
                {
                    int millis = 0;
                    if (match.Groups[3].Success)
                    {
                        string msStr = match.Groups[3].Value;
                        if (msStr.Length == 2) millis = int.Parse(msStr) * 10;
                        else if (msStr.Length == 3) millis = int.Parse(msStr);
                        else if (msStr.Length == 1) millis = int.Parse(msStr) * 100;
                    }

                    timestamps.Add(new TimeSpan(0, 0, minutes, seconds, millis));
                }

                line = line[match.Length..];
            }

            if (timestamps.Count == 0) continue;

            string text = line.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var ts in timestamps)
                {
                    result.Add(new LrcLine(ts, text));
                }
            }
        }

        return result.OrderBy(l => l.Timestamp).ToList();
    }

    /// <summary>
    /// Intelligently parses raw media metadata from browser sessions (Brave, Chrome, Edge, YouTube, etc.)
    /// extracting the true track title and clean artist name even when the browser reports generic
    /// artist placeholders like "YouTube" or "Brave", or embeds the artist into the title string.
    /// </summary>
    public static (string CleanTitle, string CleanArtist) ParseTrackMetadata(string? rawTitle, string? rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return ("", "");

        string workingTitle = rawTitle.Trim();
        string workingArtist = (rawArtist ?? "").Trim();

        // 1. Strip browser/service suffixes from title (e.g. " - YouTube", " | YouTube", " - SoundCloud")
        workingTitle = Regex.Replace(workingTitle, @"\s*[-|]\s*(?:YouTube|Brave|SoundCloud|Spotify)\s*$", "", RegexOptions.IgnoreCase).Trim();

        // 2. Identify generic or browser artist placeholders
        bool isGenericArtist = string.IsNullOrWhiteSpace(workingArtist) ||
                               GenericArtists.Contains(workingArtist);

        // Clean YouTube topic / official suffixes from artist (e.g. "The Weeknd - Topic" -> "The Weeknd")
        if (!isGenericArtist)
        {
            workingArtist = Regex.Replace(workingArtist, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase).Trim();
            workingArtist = Regex.Replace(workingArtist, @"\s*Official(?:\s*Channel)?$", "", RegexOptions.IgnoreCase).Trim();
            workingArtist = Regex.Replace(workingArtist, @"VEVO$", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(workingArtist) || GenericArtists.Contains(workingArtist))
            {
                isGenericArtist = true;
                workingArtist = "";
            }
        }

        string detectedArtist = isGenericArtist ? "" : workingArtist;
        string detectedTitle = workingTitle;

        // 3. Check for common Artist - Title separators: " - ", " – ", " — ", " : ", " | "
        string[] separators = new[] { " - ", " – ", " — ", " : ", " | " };
        int sepIndex = -1;
        string matchedSep = "";
        foreach (var sep in separators)
        {
            int idx = workingTitle.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0 && (sepIndex == -1 || idx < sepIndex))
            {
                sepIndex = idx;
                matchedSep = sep;
            }
        }

        if (sepIndex > 0)
        {
            string left = workingTitle[..sepIndex].Trim();
            string right = workingTitle[(sepIndex + matchedSep.Length)..].Trim();

            if (isGenericArtist || string.IsNullOrEmpty(detectedArtist))
            {
                // Left is the Artist, Right is the Title
                detectedArtist = left;
                detectedTitle = right;
            }
            else
            {
                string leftComp = Regex.Replace(left, @"[\s\-_]+", "");
                string detectedComp = Regex.Replace(detectedArtist, @"[\s\-_]+", "");

                // If left matches detected artist (even ignoring spaces, e.g. "Steve Lacy" vs "SteveLacy"),
                // use left as the cleanly spaced artist and right as the title!
                if (leftComp.Equals(detectedComp, StringComparison.OrdinalIgnoreCase) ||
                    leftComp.StartsWith(detectedComp, StringComparison.OrdinalIgnoreCase) ||
                    detectedComp.StartsWith(leftComp, StringComparison.OrdinalIgnoreCase) ||
                    left.StartsWith(detectedArtist, StringComparison.OrdinalIgnoreCase) ||
                    detectedArtist.StartsWith(left, StringComparison.OrdinalIgnoreCase))
                {
                    detectedArtist = left;
                    detectedTitle = right;
                }
                else
                {
                    // Preserve known non-generic artist and keep full title (e.g. "Song - Remastered 2017")
                    detectedTitle = workingTitle;
                }
            }
        }

        // 4. Sanitize title
        string cleanTitle = CleanTrackTitle(detectedTitle);

        // 5. Sanitize artist
        string cleanArtist = CleanArtistName(detectedArtist);

        return (cleanTitle, cleanArtist);
    }

    /// <summary>
    /// Cleans common suffixes from Windows media titles like "(Official Music Video)", "[Remastered 2021]", "- Radio Edit", etc.
    /// </summary>
    public static string CleanTrackTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        string clean = title.Trim();

        // Strip pipe suffixes: | Official Video, | Live, | Visualizer
        clean = Regex.Replace(clean, @"\s*\|\s*.*$", "");

        // Strip bracketed video tags: (Official Music Video), [Lyrics], (Audio), (Visualizer), etc.
        clean = VideoTagsRegex.Replace(clean, "");

        // Strip Asian decorative brackets: 【MV】, 『Official Video』
        clean = AsianBracketsRegex.Replace(clean, "");

        // Strip trailing version suffixes: - Remastered 2020, - Radio Edit
        clean = TitleSuffixRegex.Replace(clean, "");

        // Strip featuring mentions from title to maximize search match
        clean = FeatRegex.Replace(clean, "");

        // Clean any dangling opening/closing brackets or separators
        clean = Regex.Replace(clean, @"\s*[\(\[]\s*$", "");
        clean = clean.Trim(' ', '"', '\'', '“', '”', '«', '»', '`', '(', ')', '[', ']', '-', '–', '—', ':');

        return clean.Trim();
    }

    /// <summary>
    /// Cleans artist names, removing featuring tags and channel noise.
    /// </summary>
    public static string CleanArtistName(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return "";
        string clean = artist.Trim();

        clean = FeatRegex.Replace(clean, "");
        clean = Regex.Replace(clean, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*Official(?:\s*Channel)?$", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"VEVO$", "", RegexOptions.IgnoreCase);

        return clean.Trim(' ', '"', '\'');
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _diskCacheStore.Dispose();
        _cache.Clear();
    }
}
