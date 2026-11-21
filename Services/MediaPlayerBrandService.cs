using System.Diagnostics;
using System.Windows.Media;

namespace DynamicIsland.Services;

public enum MediaPlayerBrand
{
    Unknown,
    Spotify,
    AppleMusic,
    YouTubeMusic,
    YouTube,
    Tidal,
    AmazonMusic,
    Deezer,
    SoundCloud,
    Vlc,
    WindowsMediaPlayer,
    Browser
}

public sealed class MediaPlayerBrandInfo
{
    public MediaPlayerBrand Brand { get; init; }
    public string DisplayName { get; init; } = "";
    public Color BrandColor { get; init; }
    public SolidColorBrush BrandBrush { get; init; } = null!;
    public Geometry IconGeometry { get; init; } = null!;
    public string SvgPathData { get; init; } = "";
    public string SearchUrlTemplate { get; init; } = "";
}

/// <summary>
/// Intelligence engine for detecting media player applications (Spotify, Apple Music,
/// YouTube Music, Tidal, Amazon Music, VLC, browsers, etc.) from Windows GSMTC sessions,
/// AUMIDs, process names, and window titles. Provides zero-allocation frozen vector brand
/// geometries and brand colors for pixel-perfect rendering across Dynamic Island.
/// </summary>
public static class MediaPlayerBrandService
{
    // ── Pre-allocated, Frozen Vector Geometries (24x24 standard viewBox) ─────

    public const string SpotifyPathData =
        "M12 2C6.477 2 2 6.477 2 12c0 5.523 4.477 10 10 10s10-4.477 10-10c0-5.523-4.477-10-10-10zm4.586 14.424c-.18.295-.563.387-.857.207-2.35-1.435-5.308-1.76-8.794-.963-.335.077-.67-.133-.746-.468-.077-.334.132-.67.467-.746 3.808-.87 7.076-.5 9.723 1.115.294.18.386.562.207.855zm1.226-2.723c-.227.368-.71.485-1.077.258-2.69-1.654-6.79-2.133-9.972-1.168-.413.126-.85-.107-.976-.52-.126-.414.107-.85.52-.977 3.633-1.102 8.147-.568 11.247 1.33.367.227.484.71.258 1.077zm.106-2.835C14.692 8.95 8.082 8.73 4.706 9.756c-.496.15-1.022-.134-1.173-.63-.15-.496.135-1.022.63-1.173 3.944-1.198 11.238-.944 15.068 1.33.447.265.593.848.328 1.295-.265.447-.848.593-1.295.328z";

    public const string AppleMusicPathData =
        "M19 3H9c-1.1 0-2 .9-2 2v10.18c-.61-.35-1.3-.58-2.09-.58-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V8.5h8v4.68c-.61-.35-1.3-.58-2.09-.58-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V5c0-1.1.9-2 2-2z";

    public const string YouTubeMusicPathData =
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14.5c-3.59 0-6.5 2.91-6.5 6.5s2.91 6.5 6.5 6.5 6.5-2.91 6.5-6.5-2.91-6.5-6.5-6.5zm-2 9.5v-6l5 3-5 3z";

    public const string YouTubePathData =
        "M21.58 7.19c-.23-.86-.91-1.54-1.77-1.77C18.25 5 12 5 12 5s-6.25 0-7.81.42c-.86.23-1.54.91-1.77 1.77C2 8.75 2 12 2 12s0 3.25.42 4.81c.23.86.91 1.54 1.77 1.77C5.75 19 12 19 12 19s6.25 0 7.81-.42c.86-.23 1.54-.91 1.77-1.77C22 15.25 22 12 22 12s0-3.25-.42-4.81zM10 15V9l5.2 3-5.2 3z";

    public const string TidalPathData =
        "M12 7.5L8.25 11.25L12 15L15.75 11.25L12 7.5ZM4.5 7.5L0.75 11.25L4.5 15L8.25 11.25L4.5 7.5ZM19.5 7.5L15.75 11.25L19.5 15L23.25 11.25L19.5 7.5ZM12 0L8.25 3.75L12 7.5L15.75 3.75L12 0Z";

    public const string AmazonMusicPathData =
        "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6zm6.86 15.54C16.89 20.14 13.56 21 10 21c-4.41 0-8.2-2.16-9.86-5.46.25.19.53.37.82.52C2.46 17.06 6.02 19 10 19c3.15 0 6.07-.76 7.82-1.99.34-.24.69-.12 1.04.53z";

    public const string SoundCloudPathData =
        "M19.5 13.5c0-.28-.05-.55-.13-.8.64-.52 1.13-1.29 1.13-2.2 0-1.66-1.34-3-3-3-.25 0-.48.04-.71.1-.64-1.26-1.95-2.1-3.29-2.1-1.84 0-3.39 1.3-3.71 3.03C9.36 8.21 8.8 8 8.2 8 6.43 8 5 9.43 5 11.2c0 .44.09.85.25 1.23-.45.4-.75.98-.75 1.62 0 1.21.99 2.2 2.2 2.2h12.8c1.38 0 2.5-1.12 2.5-2.5 0-.17-.02-.34-.05-.5-.27.16-.58.25-.95.25z";

    public const string VlcPathData =
        "M12 2L9.5 9H14.5L12 2ZM8.8 11L7.3 15H16.7L15.2 11H8.8ZM6.5 17L4.5 22H19.5L17.5 17H6.5Z";

    public const string WindowsMediaPlayerPathData =
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 14.5v-9l7 4.5-7 4.5z";

    public const string GenericMusicPathData =
        "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z";

    // ── Static Brand Info Cache ──────────────────────────────────────

    private static readonly MediaPlayerBrandInfo SpotifyInfo = CreateBrandInfo(
        MediaPlayerBrand.Spotify, "Spotify", Color.FromRgb(0x1D, 0xB9, 0x54), SpotifyPathData, "https://open.spotify.com/search/{query}");

    private static readonly MediaPlayerBrandInfo AppleMusicInfo = CreateBrandInfo(
        MediaPlayerBrand.AppleMusic, "Apple Music", Color.FromRgb(0xFA, 0x2D, 0x48), AppleMusicPathData, "https://music.apple.com/search?term={query}");

    private static readonly MediaPlayerBrandInfo YouTubeMusicInfo = CreateBrandInfo(
        MediaPlayerBrand.YouTubeMusic, "YouTube Music", Color.FromRgb(0xFF, 0x00, 0x33), YouTubeMusicPathData, "https://music.youtube.com/search?q={query}");

    private static readonly MediaPlayerBrandInfo YouTubeInfo = CreateBrandInfo(
        MediaPlayerBrand.YouTube, "YouTube", Color.FromRgb(0xFF, 0x00, 0x00), YouTubePathData, "https://www.youtube.com/results?search_query={query}");

    private static readonly MediaPlayerBrandInfo TidalInfo = CreateBrandInfo(
        MediaPlayerBrand.Tidal, "TIDAL", Color.FromRgb(0x00, 0xFF, 0xFF), TidalPathData, "https://listen.tidal.com/search?q={query}");

    private static readonly MediaPlayerBrandInfo AmazonMusicInfo = CreateBrandInfo(
        MediaPlayerBrand.AmazonMusic, "Amazon Music", Color.FromRgb(0x25, 0xD1, 0xDA), AmazonMusicPathData, "https://music.amazon.com/search/{query}");

    private static readonly MediaPlayerBrandInfo DeezerInfo = CreateBrandInfo(
        MediaPlayerBrand.Deezer, "Deezer", Color.FromRgb(0xA2, 0x38, 0xFF), GenericMusicPathData, "https://www.deezer.com/search/{query}");

    private static readonly MediaPlayerBrandInfo SoundCloudInfo = CreateBrandInfo(
        MediaPlayerBrand.SoundCloud, "SoundCloud", Color.FromRgb(0xFF, 0x55, 0x00), SoundCloudPathData, "https://soundcloud.com/search?q={query}");

    private static readonly MediaPlayerBrandInfo VlcInfo = CreateBrandInfo(
        MediaPlayerBrand.Vlc, "VLC", Color.FromRgb(0xFF, 0x88, 0x00), VlcPathData, "");

    private static readonly MediaPlayerBrandInfo WmpInfo = CreateBrandInfo(
        MediaPlayerBrand.WindowsMediaPlayer, "Media Player", Color.FromRgb(0x00, 0x78, 0xD7), WindowsMediaPlayerPathData, "");

    private static readonly MediaPlayerBrandInfo BrowserInfo = CreateBrandInfo(
        MediaPlayerBrand.Browser, "Browser Audio", Color.FromRgb(0x0A, 0x84, 0xFF), GenericMusicPathData, "https://www.google.com/search?q={query}");

    private static readonly MediaPlayerBrandInfo UnknownInfo = CreateBrandInfo(
        MediaPlayerBrand.Unknown, "Media Player", Color.FromRgb(0xFF, 0x9F, 0x0A), GenericMusicPathData, "");

    private static MediaPlayerBrandInfo CreateBrandInfo(
        MediaPlayerBrand brand, string name, Color color, string pathData, string searchUrl)
    {
        var geom = Geometry.Parse(pathData);
        geom.Freeze();

        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return new MediaPlayerBrandInfo
        {
            Brand = brand,
            DisplayName = name,
            BrandColor = color,
            BrandBrush = brush,
            IconGeometry = geom,
            SvgPathData = pathData,
            SearchUrlTemplate = searchUrl
        };
    }

    // ── Multi-Tier Player Brand Detection ────────────────────────────

    /// <summary>
    /// Detects the media service from AUMID, track metadata, and active window titles.
    /// Handles native apps (Spotify, Apple Music, YouTube Music PWA) as well as browser tabs
    /// playing music.youtube.com, Apple Music web, Spotify web, and YouTube.
    /// </summary>
    public static MediaPlayerBrandInfo Detect(string? aumid, string? title = null, string? artist = null, string? album = null)
    {
        string id = aumid?.ToLowerInvariant() ?? "";
        string t = title?.ToLowerInvariant() ?? "";
        string a = artist?.ToLowerInvariant() ?? "";
        string alb = album?.ToLowerInvariant() ?? "";

        // ── Tier 1: Direct AUMID / Process Matching ──────────────────────

        // 1. Spotify
        if (id.Contains("spotify"))
            return SpotifyInfo;

        // 2. Apple Music & iTunes
        if (id.Contains("applemusic") || id.Contains("appleinc.applemusicwin") || id.Contains("itunes"))
            return AppleMusicInfo;

        // 3. YouTube Music (Desktop apps & PWAs)
        if (id.Contains("youtubemusic") || id.Contains("youtube music") || id.Contains("ytmdesktop") || id.Contains("th-ch.youtube-music"))
            return YouTubeMusicInfo;

        // 4. TIDAL
        if (id.Contains("tidal"))
            return TidalInfo;

        // 5. Amazon Music
        if (id.Contains("amazonmusic") || id.Contains("amazon music"))
            return AmazonMusicInfo;

        // 6. Deezer
        if (id.Contains("deezer"))
            return DeezerInfo;

        // 7. SoundCloud
        if (id.Contains("soundcloud"))
            return SoundCloudInfo;

        // 8. VLC
        if (id.Contains("vlc") || id.Contains("videolan"))
            return VlcInfo;

        // 9. Windows Media Player / Groove
        if (id.Contains("zunemusic") || id.Contains("wmplayer") || id.Contains("windowsmediaplayer"))
            return WmpInfo;

        // ── Tier 2: Browser Session Inspection (music.youtube.com, Apple Music, Spotify web) ──
        bool isBrowser = id.Contains("chrome") || id.Contains("msedge") || id.Contains("edge") ||
                         id.Contains("firefox") || id.Contains("brave") || id.Contains("opera") ||
                         id.Contains("vivaldi") || id.Contains("308046b0af4a39cb");

        if (isBrowser)
        {
            // Inspect metadata hints
            if (alb.Contains("youtube music") || t.Contains("youtube music") || a.Contains("youtube music"))
                return YouTubeMusicInfo;

            if (alb.Contains("apple music") || t.Contains("apple music") || a.Contains("apple music"))
                return AppleMusicInfo;

            if (alb.Contains("spotify") || t.Contains("spotify") || a.Contains("spotify"))
                return SpotifyInfo;

            if (alb.Contains("soundcloud") || t.Contains("soundcloud") || a.Contains("soundcloud"))
                return SoundCloudInfo;

            // Live window inspection: examine open browser windows for active music streaming tabs
            var inspectedBrand = InspectBrowserWindowBrand(id);
            if (inspectedBrand != null)
                return inspectedBrand;

            // Secondary heuristic: if video tags or YouTube indicators are present in title
            if (t.Contains("- youtube") || t.Contains("(official music video)") || t.Contains("(official video)") ||
                t.Contains("(lyric video)") || t.Contains("【mv】") || t.Contains("[mv]"))
            {
                // Likely YouTube or YouTube Music
                return YouTubeMusicInfo;
            }

            // Fallback for generic browser audio
            string browserName = "Browser";
            if (id.Contains("chrome")) browserName = "Chrome";
            else if (id.Contains("msedge") || id.Contains("edge")) browserName = "Edge";
            else if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) browserName = "Firefox";
            else if (id.Contains("brave")) browserName = "Brave";
            else if (id.Contains("opera")) browserName = "Opera";

            return new MediaPlayerBrandInfo
            {
                Brand = MediaPlayerBrand.Browser,
                DisplayName = browserName,
                BrandColor = BrowserInfo.BrandColor,
                BrandBrush = BrowserInfo.BrandBrush,
                IconGeometry = BrowserInfo.IconGeometry,
                SvgPathData = BrowserInfo.SvgPathData,
                SearchUrlTemplate = BrowserInfo.SearchUrlTemplate
            };
        }

        // ── Tier 3: Metadata Fallback Checks ─────────────────────────────
        if (alb.Contains("youtube music") || t.EndsWith("youtube music"))
            return YouTubeMusicInfo;
        if (alb.Contains("apple music") || t.EndsWith("apple music"))
            return AppleMusicInfo;

        return UnknownInfo;
    }

    /// <summary>
    /// Inspects running process window titles of the active browser to identify YouTube Music,
    /// Apple Music, Spotify Web, or YouTube tabs.
    /// </summary>
    private static MediaPlayerBrandInfo? InspectBrowserWindowBrand(string browserId)
    {
        try
        {
            string procSearch = "chrome";
            if (browserId.Contains("msedge") || browserId.Contains("edge")) procSearch = "msedge";
            else if (browserId.Contains("firefox") || browserId.Contains("308046b0af4a39cb")) procSearch = "firefox";
            else if (browserId.Contains("brave")) procSearch = "brave";
            else if (browserId.Contains("opera")) procSearch = "opera";

            var procs = Process.GetProcessesByName(procSearch);
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        string title = p.MainWindowTitle;
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        string lower = title.ToLowerInvariant();

                        if (lower.Contains("youtube music")) return YouTubeMusicInfo;
                        if (lower.Contains("apple music")) return AppleMusicInfo;
                        if (lower.Contains("spotify")) return SpotifyInfo;
                        if (lower.Contains("soundcloud")) return SoundCloudInfo;
                        if (lower.Contains("youtube")) return YouTubeMusicInfo;
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Opens the specified song/artist in the user's active media service (e.g. YouTube Music,
    /// Apple Music, or Spotify) using deep links and search URLs.
    /// </summary>
    public static bool OpenTrackInActiveService(MediaPlayerBrandInfo brand, string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        string query = Uri.EscapeDataString($"{title} {artist}".Trim());

        try
        {
            // Desktop URI schemes where available
            if (brand.Brand == MediaPlayerBrand.Spotify)
            {
                try
                {
                    // Prefer native desktop client URI
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"spotify:search:{query}",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch { }
            }
            else if (brand.Brand == MediaPlayerBrand.AppleMusic)
            {
                try
                {
                    // Open in Apple Music web / desktop bridge
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"https://music.apple.com/search?term={query}",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch { }
            }
            else if (brand.Brand == MediaPlayerBrand.YouTubeMusic)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"https://music.youtube.com/search?q={query}",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch { }
            }

            // Fallback to configured SearchUrlTemplate
            if (!string.IsNullOrWhiteSpace(brand.SearchUrlTemplate))
            {
                string url = brand.SearchUrlTemplate.Replace("{query}", query);
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return true;
            }
        }
        catch { }

        return false;
    }
}
