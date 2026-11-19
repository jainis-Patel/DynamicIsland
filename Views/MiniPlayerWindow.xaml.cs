using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Views;

public partial class MiniPlayerWindow : Window
{
    private enum DrawerState { None, Songs, Lyrics }
    private enum SongsTab { AlbumTracks, ActivePlayers, History }

    private readonly MediaSessionService _mediaService;
    private readonly LyricsService _lyricsService;
    private readonly VolumeService _volumeService;
    private readonly MediaSongListService _songListService;
    private readonly AudioSpectrum? _spectrum;
    private readonly float[] _spectrumBands = new float[AudioSpectrum.BandCount];

    private readonly DispatcherTimer _posTimer;
    private readonly DispatcherTimer _eqTimer;
    private readonly Random _rng = new();

    private bool _isUserSeeking;
    private bool _isUserVolumeSeeking;
    private bool _isCompactMode;
    private bool _isPinned = true;
    private DrawerState _activeDrawer = DrawerState.None;
    private SongsTab _activeSongsTab = SongsTab.AlbumTracks;

    private AlbumTracklistResult? _currentFetchedAlbum;
    private CancellationTokenSource? _fetchCts;
    private string _lastFetchedTrackKey = "";
    private string _lastFetchedAlbumKey = "";

    private const double FullWidth = 408;
    private const double FullHeightBase = 240;
    private const double FullHeightWithDrawer = 484;
    private const double CompactWidth = 286;
    private const double CompactHeight = 64;

    internal MiniPlayerWindow(
        MediaSessionService mediaService,
        LyricsService lyricsService,
        VolumeService volumeService,
        MediaSongListService songListService,
        AudioSpectrum? spectrum = null)
    {
        _mediaService = mediaService;
        _lyricsService = lyricsService;
        _volumeService = volumeService;
        _songListService = songListService;
        _spectrum = spectrum;

        InitializeComponent();

        Width = FullWidth;
        Height = FullHeightBase;

        Loaded += (_, _) =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.Attach(RootCard, DynamicIsland.LiquidGlass.GlassPresets.Regular);
            }
        };

        LocationChanged += (_, _) =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.ForceRender(RootCard);
            }
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowDisplayAffinity(hwnd, 0x00000011 /* WDA_EXCLUDEFROMCAPTURE */);
            }
        };

        _posTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _posTimer.Tick += (_, _) => UpdatePosition();

        _eqTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(110)
        };
        _eqTimer.Tick += (_, _) => AnimateEqualizerBars();

        // Subscriptions
        _mediaService.Changed += OnMediaChanged;
        _mediaService.TrackChanged += OnTrackChanged;
        _lyricsService.LineChanged += OnLyricLineChanged;
        _lyricsService.LyricsLoaded += OnLyricsLoaded;
        _volumeService.VolumeChanged += OnVolumeChanged;
        _songListService.HistoryChanged += OnHistoryChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;

        // Scrubber thumb drag handling
        MiniTimelineSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _isUserSeeking = true));
        MiniTimelineSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _isUserSeeking = false;
            CommitSeek();
        }));

        MiniVolumeSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _isUserVolumeSeeking = true));
        MiniVolumeSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _isUserVolumeSeeking = false));
        MiniVolumeSlider.PreviewMouseWheel += (_, e) =>
        {
            float step = e.Delta > 0 ? 0.02f : -0.02f;
            _volumeService.StepVolume(step);
            e.Handled = true;
        };

        // Window drag & snap handlers
        RootCard.MouseLeftButtonDown += RootCard_MouseLeftButtonDown;
        RootCard.MouseLeftButtonUp += RootCard_MouseLeftButtonUp;

        Loaded += (_, _) =>
        {
            RefreshAll();
            UpdateVolumeDisplay(_volumeService.CurrentVolume, _volumeService.IsMuted);
            if (_mediaService.Current?.IsPlaying ?? false)
            {
                _posTimer.Start();
                _eqTimer.Start();
            }
        };

        Closed += (_, _) =>
        {
            _posTimer.Stop();
            _eqTimer.Stop();
            _fetchCts?.Cancel();
            _mediaService.Changed -= OnMediaChanged;
            _mediaService.TrackChanged -= OnTrackChanged;
            _lyricsService.LineChanged -= OnLyricLineChanged;
            _lyricsService.LyricsLoaded -= OnLyricsLoaded;
            _volumeService.VolumeChanged -= OnVolumeChanged;
            _songListService.HistoryChanged -= OnHistoryChanged;
            ThemeManager.ThemeChanged -= OnThemeChanged;
        };
    }

    private void OnThemeChanged(AppTheme theme)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.Attach(RootCard, DynamicIsland.LiquidGlass.GlassPresets.Regular);
                DynamicIsland.LiquidGlass.GlassHost.ForceRender(RootCard);
            }
            else
            {
                DynamicIsland.LiquidGlass.GlassHost.Detach(RootCard);
            }
            RefreshAll();
        });
    }

    private void MiniTimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = true;
        double mouseX = e.GetPosition(MiniTimelineSlider).X;
        double width = MiniTimelineSlider.ActualWidth;
        if (width > 0)
        {
            double fraction = Math.Clamp(mouseX / width, 0.0, 1.0);
            MiniTimelineSlider.Value = fraction * 100.0;
            var s = _mediaService.Current;
            if (s != null && s.Duration > TimeSpan.Zero)
            {
                var previewPos = TimeSpan.FromSeconds(s.Duration.TotalSeconds * fraction);
                MiniPosText.Text = FormatTime(previewPos);
            }
        }
    }

    private void MiniTimelineSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isUserSeeking && e.LeftButton == MouseButtonState.Pressed)
        {
            double mouseX = e.GetPosition(MiniTimelineSlider).X;
            double width = MiniTimelineSlider.ActualWidth;
            if (width > 0)
            {
                double fraction = Math.Clamp(mouseX / width, 0.0, 1.0);
                MiniTimelineSlider.Value = fraction * 100.0;
                var s = _mediaService.Current;
                if (s != null && s.Duration > TimeSpan.Zero)
                {
                    var previewPos = TimeSpan.FromSeconds(s.Duration.TotalSeconds * fraction);
                    MiniPosText.Text = FormatTime(previewPos);
                }
            }
        }
    }

    private void MiniTimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isUserSeeking)
        {
            _isUserSeeking = false;
            CommitSeek();
        }
    }

    // ── Drag & Drop Snap-to-Island ────────────────────────────────────

    private void RootCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }

    private void RootCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Snap-to-Island detection: if released near top center directly over island
        if (IslandWindow.Instance is { } island)
        {
            double centerX = Left + Width / 2.0;
            double topY = Top;

            if (Math.Abs(centerX - island.IslandCenterDipX) < 160 && topY < island.IslandTopDipY + 36)
            {
                DockToIsland();
            }
        }
    }

    // ── Core UI Refresh ──────────────────────────────────────────────

    private void RefreshAll()
    {
        var s = _mediaService.Current;
        if (s == null)
        {
            MiniTitleText.Text = "Not Playing";
            MiniArtistText.Text = "System Audio";
            CompactTitleText.Text = "Not Playing";
            CompactArtistText.Text = "System Audio";

            MiniArtFallback.Visibility = Visibility.Visible;
            MiniArtImage.Source = null;
            CompactArtImage.Source = null;

            MiniPlayPauseGlyph.Text = "\uE768";
            CompactPlayPauseGlyph.Text = "\uE768";

            SourceAppText.Text = "Media";
            SourceAppLogoBox.Visibility = Visibility.Collapsed;
            SourceAppIcon.Visibility = Visibility.Visible;
            SourceCycleIcon.Visibility = Visibility.Collapsed;
            TrackIndexBadge.Visibility = Visibility.Collapsed;
            MiniAmbientStop.Color = ThemeBrushes.AmberColor;
            if (CompactBrandPip != null) CompactBrandPip.Visibility = Visibility.Collapsed;

            ResetEqualizerBars();
            return;
        }

        string title = string.IsNullOrWhiteSpace(s.Title) ? "Unknown Track" : s.Title;
        string artist = string.IsNullOrWhiteSpace(s.Artist) ? "System Audio" : s.Artist;

        MiniTitleText.Text = title;
        MiniArtistText.Text = artist;
        CompactTitleText.Text = title;
        CompactArtistText.Text = artist;

        MiniPlayPauseGlyph.Text = s.IsPlaying ? "\uE769" : "\uE768";
        CompactPlayPauseGlyph.Text = s.IsPlaying ? "\uE769" : "\uE768";

        // Artwork & Ambient Reactive Halo
        if (s.Artwork != null)
        {
            MiniArtImage.Source = s.Artwork;
            CompactArtImage.Source = s.Artwork;
            MiniArtFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            MiniArtImage.Source = null;
            CompactArtImage.Source = null;
            MiniArtFallback.Visibility = Visibility.Visible;
        }

        var accentColor = s.Accent ?? ThemeBrushes.AmberColor;
        MiniAmbientStop.Color = accentColor;

        // Source brand badge with authentic brand vector logo
        var brand = s.BrandInfo ?? MediaPlayerBrandService.Detect(s.SourceAppUserModelId, s.Title, s.Artist, s.AlbumTitle);
        SourceAppText.Text = brand.DisplayName;
        SourceAppLogoPath.Data = brand.IconGeometry;
        SourceAppLogoPath.Fill = brand.BrandBrush;
        SourceAppLogoBox.Visibility = Visibility.Visible;
        SourceAppIcon.Visibility = Visibility.Collapsed;
        SourceAppBadge.ToolTip = $"Playing on {brand.DisplayName} · Click to cycle active media players";
        SourceCycleIcon.Visibility = s.ActiveSessionCount > 1 ? Visibility.Visible : Visibility.Collapsed;

        // Compact pill brand indicator
        if (CompactBrandPath != null && CompactBrandPip != null)
        {
            CompactBrandPath.Data = brand.IconGeometry;
            CompactBrandPath.Fill = brand.BrandBrush;
            CompactBrandPip.Visibility = Visibility.Visible;
        }

        // Track number if available from album metadata
        if (s.TrackNumber > 0)
        {
            TrackIndexText.Text = s.AlbumTrackCount > 0
                ? $"#{s.TrackNumber}/{s.AlbumTrackCount}"
                : $"#{s.TrackNumber}";
            TrackIndexBadge.Visibility = Visibility.Visible;
        }
        else
        {
            TrackIndexBadge.Visibility = Visibility.Collapsed;
        }

        // Shuffle & Repeat states
        UpdateShuffleRepeatStates(s);

        // Position & timeline
        UpdatePosition();

        // If lyrics drawer is open, refresh lyrics
        if (_activeDrawer == DrawerState.Lyrics)
        {
            UpdateLyricsDisplay();
        }
    }

    private void UpdateShuffleRepeatStates(MediaInfo s)
    {
        // Shuffle active highlight
        if (s.IsShuffleActive == true)
        {
            MiniShuffleGlyph.Foreground = (Brush)FindResource("AccentBrush");
            MiniShuffleBtn.Opacity = 1.0;
        }
        else
        {
            MiniShuffleGlyph.Foreground = (Brush)FindResource("PrimaryTextBrush");
            MiniShuffleBtn.Opacity = s.CanShuffle ? 1.0 : 0.45;
        }

        // Repeat mode (None, List/All, Track/One)
        if (s.AutoRepeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.Track)
        {
            MiniRepeatGlyph.Text = "\uE8ED"; // Repeat One
            MiniRepeatGlyph.Foreground = (Brush)FindResource("AccentBrush");
        }
        else if (s.AutoRepeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.List)
        {
            MiniRepeatGlyph.Text = "\uE8EE"; // Repeat All
            MiniRepeatGlyph.Foreground = (Brush)FindResource("AccentBrush");
        }
        else
        {
            MiniRepeatGlyph.Text = "\uE8EE";
            MiniRepeatGlyph.Foreground = (Brush)FindResource("PrimaryTextBrush");
            MiniRepeatBtn.Opacity = s.CanRepeat ? 1.0 : 0.45;
        }
    }

    private void UpdatePosition()
    {
        var s = _mediaService.Current;
        if (s == null)
        {
            if (!_isUserSeeking && MiniTimelineSlider.Value != 0)
            {
                MiniTimelineSlider.Value = 0;
                MiniPosText.Text = "0:00";
                MiniDurText.Text = "0:00";
            }
            return;
        }

        var dur = s.Duration;
        MiniDurText.Text = dur > TimeSpan.Zero ? FormatTime(dur) : "0:00";

        if (_isUserSeeking)
        {
            if (dur > TimeSpan.Zero)
            {
                var previewPos = TimeSpan.FromSeconds(dur.TotalSeconds * (MiniTimelineSlider.Value / 100.0));
                MiniPosText.Text = FormatTime(previewPos);
            }
            return;
        }

        var pos = s.GetPosition(DateTimeOffset.Now);
        MiniPosText.Text = FormatTime(pos);

        if (dur > TimeSpan.Zero)
        {
            double pct = Math.Clamp((pos.TotalSeconds / dur.TotalSeconds) * 100.0, 0, 100);
            if (Math.Abs(MiniTimelineSlider.Value - pct) > 0.05)
            {
                MiniTimelineSlider.Value = pct;
            }
        }
        else
        {
            if (MiniTimelineSlider.Value != 0) MiniTimelineSlider.Value = 0;
        }

        // Keep lyrics continuous and synced even if Dynamic Island is collapsed
        _lyricsService.UpdatePosition(pos);
    }

    private void CommitSeek()
    {
        var s = _mediaService.Current;
        if (s == null || s.Duration <= TimeSpan.Zero) return;
        double frac = Math.Clamp(MiniTimelineSlider.Value / 100.0, 0.0, 1.0);
        var target = TimeSpan.FromSeconds(s.Duration.TotalSeconds * frac);
        _mediaService.Seek(target);
    }

    private static string FormatTime(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }

    // ── Volume Handling ──────────────────────────────────────────────

    private void OnVolumeChanged(float vol, bool isMuted) =>
        Dispatcher.InvokeAsync(() => UpdateVolumeDisplay(vol, isMuted));

    private void UpdateVolumeDisplay(float vol, bool isMuted)
    {
        if (!_isUserVolumeSeeking)
        {
            MiniVolumeSlider.Value = Math.Clamp(vol * 100f, 0, 100);
        }

        if (isMuted || vol <= 0.01f)
        {
            MiniVolumeGlyph.Text = "\uE74F"; // Mute
            MiniVolumeGlyph.Foreground = (Brush)FindResource("SecondaryBrush");
        }
        else if (vol < 0.35f)
        {
            MiniVolumeGlyph.Text = "\uE992"; // Low volume
            MiniVolumeGlyph.Foreground = (Brush)FindResource("PrimaryTextBrush");
        }
        else if (vol < 0.70f)
        {
            MiniVolumeGlyph.Text = "\uE993"; // Med volume
            MiniVolumeGlyph.Foreground = (Brush)FindResource("PrimaryTextBrush");
        }
        else
        {
            MiniVolumeGlyph.Text = "\uE767"; // High volume
            MiniVolumeGlyph.Foreground = (Brush)FindResource("PrimaryTextBrush");
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded && (MiniVolumeSlider.IsMouseOver || _isUserVolumeSeeking))
        {
            float targetScalar = (float)Math.Clamp(MiniVolumeSlider.Value / 100.0, 0.0, 1.0);
            _volumeService.SetVolume(targetScalar);
        }
    }

    private void VolumeMute_Click(object sender, RoutedEventArgs e) =>
        _volumeService.ToggleMute();

    // ── Audio Equalizer Visualizer Bars ──────────────────────────────

    private void AnimateEqualizerBars()
    {
        var s = _mediaService.Current;
        if (s == null || !s.IsPlaying)
        {
            ResetEqualizerBars();
            return;
        }

        if (_spectrum != null)
        {
            _spectrum.ReadBands(_spectrumBands);
            EqBar0.Height = Math.Clamp(3 + (int)(_spectrumBands[0] * 12f), 3, 14);
            EqBar1.Height = Math.Clamp(3 + (int)(_spectrumBands[1] * 14f), 3, 16);
            EqBar2.Height = Math.Clamp(3 + (int)(_spectrumBands[2] * 13f), 3, 15);
            EqBar3.Height = Math.Clamp(3 + (int)(_spectrumBands[3] * 11f), 3, 13);
        }
        else
        {
            EqBar0.Height = 3 + _rng.Next(0, 6);
            EqBar1.Height = 4 + _rng.Next(0, 8);
            EqBar2.Height = 4 + _rng.Next(0, 7);
            EqBar3.Height = 3 + _rng.Next(0, 6);
        }
    }

    private void ResetEqualizerBars()
    {
        EqBar0.Height = 3;
        EqBar1.Height = 3;
        EqBar2.Height = 3;
        EqBar3.Height = 3;
    }

    // ── Event Handlers ───────────────────────────────────────────────

    private void OnMediaChanged() =>
        Dispatcher.InvokeAsync(() =>
        {
            RefreshAll();
            if (_mediaService.Current?.IsPlaying ?? false)
            {
                if (!_posTimer.IsEnabled) _posTimer.Start();
                if (!_eqTimer.IsEnabled) _eqTimer.Start();
            }
            else
            {
                _posTimer.Stop();
                _eqTimer.Stop();
                ResetEqualizerBars();
            }
        });

    private void OnTrackChanged() =>
        Dispatcher.InvokeAsync(() =>
        {
            RefreshAll();
            // If the song list drawer is currently showing album tracks, auto-fetch for the new song
            if (_activeDrawer == DrawerState.Songs && _activeSongsTab == SongsTab.AlbumTracks)
            {
                TriggerFetchAlbumTracks();
            }
        });

    private void OnLyricLineChanged(string prev, string current, string next, int idx) =>
        Dispatcher.InvokeAsync(UpdateLyricsDisplay);

    private void OnLyricsLoaded() =>
        Dispatcher.InvokeAsync(UpdateLyricsDisplay);

    private void OnHistoryChanged() =>
        Dispatcher.InvokeAsync(() =>
        {
            if (_activeDrawer == DrawerState.Songs && _activeSongsTab == SongsTab.History)
            {
                RenderHistoryTab();
            }
        });

    // ── Transport Commands ───────────────────────────────────────────

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _mediaService.TogglePlayPause();
    private void Prev_Click(object sender, RoutedEventArgs e) => _mediaService.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _mediaService.Next();
    private void Shuffle_Click(object sender, RoutedEventArgs e) => _mediaService.ToggleShuffle();
    private void Repeat_Click(object sender, RoutedEventArgs e) => _mediaService.CycleRepeatMode();

    private void Artwork_Click(object sender, MouseButtonEventArgs e)
    {
        _mediaService.FocusActivePlayerWindow();
        e.Handled = true;
    }

    private void Title_Click(object sender, MouseButtonEventArgs e)
    {
        CopyCurrentTrackToClipboard();
        e.Handled = true;
    }

    private void SourceBadge_Click(object sender, MouseButtonEventArgs e)
    {
        string? switched = _mediaService.CycleNextSession();
        if (!string.IsNullOrEmpty(switched))
        {
            SourceAppText.Text = switched;
        }
        e.Handled = true;
    }

    // ── Window Controls & Modes ──────────────────────────────────────

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        PinGlyph.Text = _isPinned ? "\uE718" : "\uE77A"; // Pin vs Unpin
        PinButton.Background = _isPinned
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        PinGlyph.Foreground = _isPinned
            ? new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C))
            : (Brush)FindResource("PrimaryTextBrush");
    }

    private void CompactMode_Click(object sender, RoutedEventArgs e) => SetCompactMode(true);
    private void ExpandFromCompact_Click(object sender, RoutedEventArgs e) => SetCompactMode(false);

    private void SetCompactMode(bool compact)
    {
        _isCompactMode = compact;

        if (compact)
        {
            CloseDrawers();
            FullPlayerView.Visibility = Visibility.Collapsed;
            CompactPillView.Visibility = Visibility.Visible;
            AnimateWindowSize(CompactWidth, CompactHeight);
        }
        else
        {
            CompactPillView.Visibility = Visibility.Collapsed;
            FullPlayerView.Visibility = Visibility.Visible;
            AnimateWindowSize(FullWidth, FullHeightBase);
        }
    }

    private void AnimateWindowSize(double targetW, double targetH)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var wAnim = new DoubleAnimation(targetW, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        var hAnim = new DoubleAnimation(targetH, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };

        BeginAnimation(WidthProperty, wAnim);
        BeginAnimation(HeightProperty, hAnim);
    }

    private void DockToIsland_Click(object sender, RoutedEventArgs e) => DockToIsland();

    public void DockToIsland()
    {
        Close();
        if (IslandWindow.Instance is { } island)
        {
            island.Dispatcher.InvokeAsync(() =>
            {
                island.ExpandAndSwitchView(ExpandedView.Media);
            });
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Search & Clipboard Tools ─────────────────────────────────────

    private void CopyTrack_Click(object sender, RoutedEventArgs e) => CopyCurrentTrackToClipboard();

    private void CopyCurrentTrackToClipboard()
    {
        var s = _mediaService.Current;
        if (s == null || string.IsNullOrWhiteSpace(s.Title)) return;

        string text = string.IsNullOrWhiteSpace(s.Artist) ? s.Title : $"{s.Artist} - {s.Title}";
        try
        {
            Clipboard.SetDataObject(text);
            CopyTrackGlyph.Text = "\uE73E"; // Checkmark feedback
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                CopyTrackGlyph.Text = "\uE8C8";
                timer.Stop();
            };
            timer.Start();
        }
        catch { }
    }

    private void SearchTrack_Click(object sender, RoutedEventArgs e)
    {
        var s = _mediaService.Current;
        if (s == null || string.IsNullOrWhiteSpace(s.Title)) return;

        var brand = s.BrandInfo ?? MediaPlayerBrandService.Detect(s.SourceAppUserModelId, s.Title, s.Artist, s.AlbumTitle);
        MediaPlayerBrandService.OpenTrackInActiveService(brand, s.Title, s.Artist);
    }

    // ═════════════════════════════════════════════════════════════════
    //  DRAWER & TAB NAVIGATION
    // ═════════════════════════════════════════════════════════════════

    private void ToggleSongList_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDrawer == DrawerState.Songs)
        {
            CloseDrawers();
        }
        else
        {
            OpenDrawer(DrawerState.Songs);
            SwitchSongsTab(_activeSongsTab);
        }
    }

    private void ToggleLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDrawer == DrawerState.Lyrics)
        {
            CloseDrawers();
        }
        else
        {
            OpenDrawer(DrawerState.Lyrics);
            UpdateLyricsDisplay();
        }
    }

    private void OpenDrawer(DrawerState drawer)
    {
        _activeDrawer = drawer;
        DrawerHost.Visibility = Visibility.Visible;

        SongListDrawer.Visibility = drawer == DrawerState.Songs ? Visibility.Visible : Visibility.Collapsed;
        LyricsDrawer.Visibility = drawer == DrawerState.Lyrics ? Visibility.Visible : Visibility.Collapsed;

        ToggleSongListBtn.Background = drawer == DrawerState.Songs
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        ToggleLyricsBtn.Background = drawer == DrawerState.Lyrics
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

        AnimateWindowSize(FullWidth, CalculateDrawerTargetHeight());
    }

    private double CalculateDrawerTargetHeight()
    {
        if (_activeDrawer == DrawerState.None) return FullHeightBase;

        if (_activeDrawer == DrawerState.Lyrics)
        {
            return 370; // Clean, focused karaoke lyrics height
        }

        // Songs drawer: scale smoothly based on child count rather than leaving an empty void
        if (_activeSongsTab == SongsTab.AlbumTracks)
        {
            int count = AlbumTracksListHost.Children.Count;
            if (count == 0) return 330;
            double desired = FullHeightBase + 72 + (count * 38);
            return Math.Clamp(desired, 330, FullHeightWithDrawer);
        }
        else if (_activeSongsTab == SongsTab.ActivePlayers)
        {
            int count = ActivePlayersListHost.Children.Count;
            if (count <= 1) return 335;
            double desired = FullHeightBase + 72 + (count * 44);
            return Math.Clamp(desired, 335, FullHeightWithDrawer);
        }
        else // History
        {
            int count = HistoryListHost.Children.Count;
            if (count == 0) return 330;
            double desired = FullHeightBase + 72 + (count * 36);
            return Math.Clamp(desired, 330, FullHeightWithDrawer);
        }
    }

    private void CloseDrawers()
    {
        _activeDrawer = DrawerState.None;
        DrawerHost.Visibility = Visibility.Collapsed;
        SongListDrawer.Visibility = Visibility.Collapsed;
        LyricsDrawer.Visibility = Visibility.Collapsed;

        ToggleSongListBtn.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        ToggleLyricsBtn.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

        AnimateWindowSize(FullWidth, FullHeightBase);
    }

    // ═════════════════════════════════════════════════════════════════
    //  SONG LIST & ACTIVE PLAYERS HUB (THE CORE REQUIREMENT)
    // ═════════════════════════════════════════════════════════════════

    private void TabAlbumTracks_Click(object sender, RoutedEventArgs e) => SwitchSongsTab(SongsTab.AlbumTracks);
    private void TabActivePlayers_Click(object sender, RoutedEventArgs e) => SwitchSongsTab(SongsTab.ActivePlayers);
    private void TabHistory_Click(object sender, RoutedEventArgs e) => SwitchSongsTab(SongsTab.History);

    private void SwitchSongsTab(SongsTab tab)
    {
        _activeSongsTab = tab;

        // Visual segmented tab button states
        HighlightSegmentBtn(TabAlbumTracksBtn, tab == SongsTab.AlbumTracks);
        HighlightSegmentBtn(TabActivePlayersBtn, tab == SongsTab.ActivePlayers);
        HighlightSegmentBtn(TabHistoryBtn, tab == SongsTab.History);

        AlbumTracksPanel.Visibility = tab == SongsTab.AlbumTracks ? Visibility.Visible : Visibility.Collapsed;
        ActivePlayersPanel.Visibility = tab == SongsTab.ActivePlayers ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = tab == SongsTab.History ? Visibility.Visible : Visibility.Collapsed;

        switch (tab)
        {
            case SongsTab.AlbumTracks:
                TriggerFetchAlbumTracks();
                break;
            case SongsTab.ActivePlayers:
                _ = RenderActivePlayersTabAsync();
                break;
            case SongsTab.History:
                RenderHistoryTab();
                break;
        }

        AnimateWindowSize(FullWidth, CalculateDrawerTargetHeight());
    }

    private void HighlightSegmentBtn(Button btn, bool active)
    {
        btn.Background = active ? new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
        btn.Foreground = active ? (Brush)FindResource("PrimaryTextBrush") : (Brush)FindResource("SecondaryBrush");
        btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Medium;
    }

    private void RefreshTracks_Click(object sender, RoutedEventArgs e)
    {
        switch (_activeSongsTab)
        {
            case SongsTab.AlbumTracks:
                _lastFetchedTrackKey = ""; // Force fresh query
                TriggerFetchAlbumTracks();
                break;
            case SongsTab.ActivePlayers:
                _ = RenderActivePlayersTabAsync();
                break;
            case SongsTab.History:
                RenderHistoryTab();
                break;
        }
    }

    // ── 1. Album Tracklist Fetching ───────────────────────────────────

    private void TriggerFetchAlbumTracks()
    {
        var s = _mediaService.Current;
        if (s == null || string.IsNullOrWhiteSpace(s.Title))
        {
            AlbumSummaryText.Text = "No active audio playing to fetch album tracks for.";
            AlbumTracksListHost.Children.Clear();
            return;
        }

        string cleanArtist = MediaSongListService.CleanArtist(s.Artist, s.Title);
        string cleanAlbum = string.IsNullOrWhiteSpace(s.AlbumTitle) ? "" : s.AlbumTitle.Trim();
        string albumKey = $"{cleanArtist}::{cleanAlbum}".ToLowerInvariant();
        string trackKey = $"{s.Title}::{s.Artist}::{s.AlbumTitle}".ToLowerInvariant();

        // If the current album is already loaded, avoid destructive re-fetch and smoothly sync queue highlight
        if (_currentFetchedAlbum != null && !string.IsNullOrWhiteSpace(cleanAlbum) && albumKey == _lastFetchedAlbumKey)
        {
            if (AlbumTracksListHost.Children.Count > 0)
            {
                UpdateActiveQueueHighlight();
                return;
            }
            else
            {
                RenderAlbumTracks(_currentFetchedAlbum);
                return;
            }
        }

        if (_currentFetchedAlbum != null && trackKey == _lastFetchedTrackKey)
        {
            if (AlbumTracksListHost.Children.Count > 0)
            {
                UpdateActiveQueueHighlight();
            }
            else
            {
                RenderAlbumTracks(_currentFetchedAlbum);
            }
            return;
        }

        AlbumSummaryText.Text = $"Fetching tracklist for \"{s.Title}\"...";
        AlbumTracksListHost.Children.Clear();

        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        var token = _fetchCts.Token;

        _ = Task.Run(async () =>
        {
            var result = await _songListService.FetchSongListAsync(s.Title, s.Artist, s.AlbumTitle, token);
            if (!token.IsCancellationRequested)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    _lastFetchedAlbumKey = albumKey;
                    _lastFetchedTrackKey = trackKey;
                    _currentFetchedAlbum = result;
                    RenderAlbumTracks(result);
                });
            }
        }, token);
    }

    private void RenderAlbumTracks(AlbumTracklistResult? result)
    {
        AlbumTracksListHost.Children.Clear();

        if (result == null || result.Tracks.Count == 0)
        {
            AlbumSummaryText.Text = "No tracklist found online for this stream.";
            return;
        }

        string typeLabel = result.IsArtistTopTracks ? "Top tracks" : "Album tracklist";
        AlbumSummaryText.Text = $"{result.AlbumName} • {result.Tracks.Count} songs ({typeLabel})";

        var currentTitle = _mediaService.Current?.Title ?? "";
        var activeBrand = _mediaService.Current?.BrandInfo ?? MediaPlayerBrandService.Detect(_mediaService.Current?.SourceAppUserModelId);

        foreach (var track in result.Tracks)
        {
            bool isCurrent = IsMatchingTrack(track.Title, currentTitle);

            var itemBorder = new Border
            {
                Tag = track,
                Background = isCurrent
                    ? new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0x9F, 0x0A))
                    : new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
                BorderBrush = isCurrent
                    ? (Brush)FindResource("AccentBrush")
                    : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 3.5),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

            // Track Number or Active Wave
            var numText = new TextBlock
            {
                Text = isCurrent ? "♪" : track.DisplayTrackNumber,
                FontSize = isCurrent ? 11 : 9.5,
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isCurrent ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(numText, 0);
            grid.Children.Add(numText);

            // Title & Artist
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock
            {
                Text = track.Title,
                FontSize = 10.5,
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isCurrent ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var artistText = new TextBlock
            {
                Text = track.Artist,
                FontSize = 9,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            };
            sp.Children.Add(titleText);
            sp.Children.Add(artistText);
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            // Duration
            var durText = new TextBlock
            {
                Text = track.FormattedDuration,
                FontSize = 9,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(durText, 2);
            grid.Children.Add(durText);

            itemBorder.Child = grid;

            // Click action: open in user's active media service (Spotify, YouTube Music, Apple Music)
            itemBorder.MouseLeftButtonDown += (_, e) =>
            {
                var curBrand = _mediaService.Current?.BrandInfo ?? MediaPlayerBrandService.Detect(_mediaService.Current?.SourceAppUserModelId);
                MediaPlayerBrandService.OpenTrackInActiveService(curBrand, track.Title, track.Artist);
                e.Handled = true;
            };

            itemBorder.ToolTip = $"Click to play \"{track.Title}\" on {activeBrand.DisplayName}";

            AlbumTracksListHost.Children.Add(itemBorder);

            if (isCurrent)
            {
                itemBorder.BringIntoView();
            }
        }

        if (_activeDrawer == DrawerState.Songs && _activeSongsTab == SongsTab.AlbumTracks)
        {
            AnimateWindowSize(FullWidth, CalculateDrawerTargetHeight());
        }
    }

    private void UpdateActiveQueueHighlight()
    {
        var currentTitle = _mediaService.Current?.Title ?? "";
        if (string.IsNullOrWhiteSpace(currentTitle)) return;

        foreach (UIElement child in AlbumTracksListHost.Children)
        {
            if (child is Border itemBorder && itemBorder.Tag is SongTrackItem track)
            {
                bool isCurrent = IsMatchingTrack(track.Title, currentTitle);

                itemBorder.Background = isCurrent
                    ? new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0x9F, 0x0A))
                    : new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
                itemBorder.BorderBrush = isCurrent
                    ? (Brush)FindResource("AccentBrush")
                    : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

                if (itemBorder.Child is Grid grid && grid.Children.Count >= 2)
                {
                    if (grid.Children[0] is TextBlock numText)
                    {
                        numText.Text = isCurrent ? "♪" : track.DisplayTrackNumber;
                        numText.FontSize = isCurrent ? 11 : 9.5;
                        numText.FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal;
                        numText.Foreground = isCurrent ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SecondaryBrush");
                    }
                    if (grid.Children[1] is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock titleText)
                    {
                        titleText.FontWeight = isCurrent ? FontWeights.Bold : FontWeights.SemiBold;
                        titleText.Foreground = isCurrent ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("PrimaryTextBrush");
                    }
                }

                if (isCurrent)
                {
                    itemBorder.BringIntoView();
                }
            }
        }
    }

    private static bool IsMatchingTrack(string itemTitle, string currentTitle)
    {
        if (string.Equals(itemTitle, currentTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        string cleanItem = MediaSongListService.CleanMetadata(itemTitle).ToLowerInvariant();
        string cleanCurr = MediaSongListService.CleanMetadata(currentTitle).ToLowerInvariant();

        if (string.Equals(cleanItem, cleanCurr, StringComparison.OrdinalIgnoreCase))
            return true;

        if (cleanItem.Length > 2 && cleanCurr.Length > 2)
        {
            if (cleanItem.Contains(cleanCurr) || cleanCurr.Contains(cleanItem))
                return true;
        }

        return false;
    }

    // ── 2. Active Windows Players Listing ─────────────────────────────

    private async Task RenderActivePlayersTabAsync()
    {
        ActivePlayersListHost.Children.Clear();
        var sessions = await _mediaService.GetActiveSessionsAsync();

        if (sessions.Count == 0)
        {
            ActivePlayersListHost.Children.Add(new TextBlock
            {
                Text = "No active media players detected on Windows.",
                FontSize = 10,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var sess in sessions)
        {
            var brand = sess.BrandInfo ?? MediaPlayerBrandService.Detect(sess.Aumid, sess.Title, sess.Artist);

            var item = new Border
            {
                Background = sess.IsCurrent
                    ? new SolidColorBrush(Color.FromArgb(0x24, 0x0A, 0x84, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                BorderBrush = sess.IsCurrent
                    ? new SolidColorBrush(Color.FromArgb(0x60, 0x0A, 0x84, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Artwork / App Brand Icon
            var artBorder = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (sess.Artwork != null)
            {
                var artGrid = new Grid();
                artGrid.Children.Add(new Image { Source = sess.Artwork, Stretch = Stretch.UniformToFill });

                var pip = new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.FromArgb(0xD5, 0x00, 0x00, 0x00)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 1, 1)
                };
                var pipVb = new Viewbox { Width = 6.5, Height = 6.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                pipVb.Child = new System.Windows.Shapes.Path { Data = brand.IconGeometry, Fill = brand.BrandBrush, Stretch = Stretch.Uniform };
                pip.Child = pipVb;
                artGrid.Children.Add(pip);

                artBorder.Child = artGrid;
            }
            else
            {
                var vb = new Viewbox { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                vb.Child = new System.Windows.Shapes.Path { Data = brand.IconGeometry, Fill = brand.BrandBrush, Stretch = Stretch.Uniform };
                artBorder.Child = vb;
            }
            Grid.SetColumn(artBorder, 0);
            grid.Children.Add(artBorder);

            // Title & App Name
            var sp = new StackPanel { Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                Text = sess.Title,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var app = new TextBlock
            {
                Text = $"{brand.DisplayName} • {sess.Artist}",
                FontSize = 9,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            };
            sp.Children.Add(title);
            sp.Children.Add(app);
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            // Switch / Current Badge
            var badge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Background = sess.IsCurrent ? new SolidColorBrush(Color.FromArgb(0x40, 0x0A, 0x84, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = sess.IsCurrent ? "Active" : (sess.IsPlaying ? "Playing" : "Paused"),
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = sess.IsCurrent ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SecondaryBrush")
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);

            item.Child = grid;

            // Click to switch control to this player
            item.MouseLeftButtonDown += (_, e) =>
            {
                _mediaService.SwitchToSession(sess.Aumid);
                _ = RenderActivePlayersTabAsync();
                e.Handled = true;
            };

            ActivePlayersListHost.Children.Add(item);
        }

        if (_activeDrawer == DrawerState.Songs && _activeSongsTab == SongsTab.ActivePlayers)
        {
            AnimateWindowSize(FullWidth, CalculateDrawerTargetHeight());
        }
    }

    // ── 3. Session Playback History Listing ───────────────────────────

    private void RenderHistoryTab()
    {
        HistoryListHost.Children.Clear();
        var history = _songListService.History;

        if (history.Count == 0)
        {
            HistoryListHost.Children.Add(new TextBlock
            {
                Text = "No songs recorded in this session yet.",
                FontSize = 10,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var rec in history)
        {
            var item = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 4.5, 7, 4.5),
                Margin = new Thickness(0, 0, 0, 3.5),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                Text = rec.Title,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var meta = new TextBlock
            {
                Text = $"{rec.Artist} • {rec.SourceApp}",
                FontSize = 8.5,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            };
            sp.Children.Add(title);
            sp.Children.Add(meta);
            Grid.SetColumn(sp, 0);
            grid.Children.Add(sp);

            var time = new TextBlock
            {
                Text = rec.TimeAgo,
                FontSize = 8.5,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(time, 1);
            grid.Children.Add(time);

            item.Child = grid;

            // Click to copy or search
            item.MouseLeftButtonDown += (_, e) =>
            {
                try
                {
                    Clipboard.SetDataObject($"{rec.Artist} - {rec.Title}");
                }
                catch { }
                e.Handled = true;
            };

            item.ToolTip = $"Click to copy \"{rec.Artist} - {rec.Title}\"";

            HistoryListHost.Children.Add(item);
        }

        if (_activeDrawer == DrawerState.Songs && _activeSongsTab == SongsTab.History)
        {
            AnimateWindowSize(FullWidth, CalculateDrawerTargetHeight());
        }
    }

    // ── 4. Synced Karaoke Lyrics Display ──────────────────────────────

    private void UpdateLyricsDisplay()
    {
        if (_activeDrawer != DrawerState.Lyrics) return;

        if (_lyricsService.IsLoading)
        {
            LyricLinePrev.Text = "";
            LyricLineCurrent.Text = "♪ Searching lyrics on LRCLIB...";
            LyricLineNext.Text = "";
            LyricsStatusText.Text = "Loading...";
            return;
        }

        if (_lyricsService.IsInstrumental)
        {
            LyricLinePrev.Text = "";
            LyricLineCurrent.Text = "♪ Instrumental Track (No Lyrics)";
            LyricLineNext.Text = "";
            LyricsStatusText.Text = "Instrumental";
            return;
        }

        if (!_lyricsService.HasLyrics)
        {
            LyricLinePrev.Text = "";
            LyricLineCurrent.Text = "♪ No synced lyrics available for this song.";
            LyricLineNext.Text = "";
            LyricsStatusText.Text = "No Lyrics";
            return;
        }

        LyricLinePrev.Text = _lyricsService.PreviousLineText;
        LyricLineCurrent.Text = string.IsNullOrWhiteSpace(_lyricsService.CurrentLineText)
            ? "♪ • • •"
            : _lyricsService.CurrentLineText;
        LyricLineNext.Text = _lyricsService.NextLineText;
        LyricsStatusText.Text = "Synced";
    }

    protected override void OnClosed(EventArgs e)
    {
        DynamicIsland.LiquidGlass.GlassHost.Detach(RootCard);
        base.OnClosed(e);

        // Stop and unhook timers to prevent background CPU usage
        _posTimer.Stop();
        _eqTimer.Stop();

        // Cancel any active album tracklist HTTP fetching
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = null;

        // Unhook from long-lived singleton services to prevent memory leaks
        _mediaService.Changed -= OnMediaChanged;
        _mediaService.TrackChanged -= OnTrackChanged;
        _lyricsService.LineChanged -= OnLyricLineChanged;
        _lyricsService.LyricsLoaded -= OnLyricsLoaded;
        _volumeService.VolumeChanged -= OnVolumeChanged;
        _songListService.HistoryChanged -= OnHistoryChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }
}