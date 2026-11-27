using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Views;

public partial class MiniPlayerWindow : Window
{
    private enum PlayerViewMode { Full, Compact, Showcase }
    private enum DrawerState { None, Songs, Lyrics }
    private enum SongsTab { AlbumTracks, ActivePlayers, History }

    private static readonly SolidColorBrush SubtleSurfaceBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush SegmentActiveBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush CurrentTrackBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x28, 0xFF, 0x9F, 0x0A));
    private static readonly SolidColorBrush IdleTrackBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ActivePlayerBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x24, 0x0A, 0x84, 0xFF));
    private static readonly SolidColorBrush InactivePlayerBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ActivePlayerBorderBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x60, 0x0A, 0x84, 0xFF));
    private static readonly SolidColorBrush InactivePlayerBorderBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ArtFallbackBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush BrandPipBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0xD5, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush ActivePlayerBadgeBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x40, 0x0A, 0x84, 0xFF));

    private readonly MediaSessionService _mediaService;
    private readonly LyricsService _lyricsService;
    private readonly VolumeService _volumeService;
    private readonly MediaSongListService _songListService;
    private readonly AudioSpectrum? _spectrum;
    private readonly SettingsService? _settingsService;
    private readonly AudioEndpointService? _audioEndpointService;
    private readonly float[] _spectrumBands = new float[AudioSpectrum.BandCount];
    private bool _discRendering;
    private TimeSpan _lastDiscFrame = TimeSpan.Zero;

    private readonly DispatcherTimer _posTimer;
    private readonly DispatcherTimer _eqTimer;
    private DispatcherTimer? _copyFeedbackTimer;
    private readonly Random _rng = new();

    private bool _isUserSeeking;
    private bool _isUserVolumeSeeking;
    private bool _isUpdatingVolumeUi;
    private int _activePlayersRenderVersion;
    private PlayerViewMode _viewMode = PlayerViewMode.Full;
    private bool _isPinned = true;
    private bool _wasShown;
    private bool _isFullscreenSuppressed;
    private bool _isClosed;
    private bool _closingForAppShutdown;
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
    private const double ShowcaseWidth = 300;
    private const double ShowcaseHeight = 348;

    public bool IsFullscreenSuppressed => _isFullscreenSuppressed;

    internal MiniPlayerWindow(
        MediaSessionService mediaService,
        LyricsService lyricsService,
        VolumeService volumeService,
        MediaSongListService songListService,
        AudioSpectrum? spectrum = null,
        SettingsService? settingsService = null,
        AudioEndpointService? audioEndpointService = null)
    {
        _mediaService = mediaService;
        _lyricsService = lyricsService;
        _volumeService = volumeService;
        _songListService = songListService;
        _spectrum = spectrum;
        _settingsService = settingsService;
        _audioEndpointService = audioEndpointService;

        InitializeComponent();

        Width = FullWidth;
        Height = FullHeightBase;
        RootCard.Width = FullWidth - 20;
        RootCard.Height = FullHeightBase - 20;

        Loaded += (_, _) =>
        {
            if (ThemeManager.IsLiquidGlass)
            {
                DynamicIsland.LiquidGlass.GlassHost.Attach(RootCard, DynamicIsland.LiquidGlass.GlassPresets.Regular);
            }
            if (SystemParameters.ClientAreaAnimation)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                RootCard.Opacity = 0.0;
                RootCardScale.ScaleX = 0.94;
                RootCardScale.ScaleY = 0.94;
                RootCard.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
                RootCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                RootCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
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
            _wasShown = true;
            var hwnd = FloatingWindowHelper.ApplyToolWindowStyles(this, noActivate: false);
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
        if (_settingsService != null)
        {
            _settingsService.Changed += OnSettingsChanged;
        }
        if (_audioEndpointService != null)
        {
            _audioEndpointService.EndpointsChanged += OnAudioEndpointsChanged;
            _audioEndpointService.DefaultChanged += OnDefaultAudioEndpointChanged;
        }
        PreviewKeyDown += MiniPlayerWindow_PreviewKeyDown;

        // Scrubber thumb drag handling
        MiniTimelineSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _isUserSeeking = true));
        MiniTimelineSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _isUserSeeking = false;
            CommitSeek();
        }));
        MiniTimelineSlider.LostMouseCapture += (_, _) =>
        {
            if (_isUserSeeking)
            {
                _isUserSeeking = false;
                CommitSeek();
            }
        };

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
            _wasShown = true;
            ApplyAlbumArtStyle(_settingsService?.Current.AlbumArtStyle);
            RefreshAll();
            SyncAudioOutputButton();
            SyncLyricsAnimationStyleLabel();
            UpdateVolumeDisplay(_volumeService.CurrentVolume, _volumeService.IsMuted);
            if (_isFullscreenSuppressed)
            {
                Topmost = false;
                Visibility = Visibility.Hidden;
                return;
            }
            if (_mediaService.Current?.IsPlaying ?? false)
            {
                _posTimer.Start();
                _eqTimer.Start();
            }
            EnsureDiscRendering();
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
        MiniTimelineSlider.CaptureMouse();
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
            MiniTimelineSlider.ReleaseMouseCapture();
            CommitSeek();
        }
    }

    // ── Drag & Drop Snap-to-Island ────────────────────────────────────

    private void RootCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
            CheckSnapToIsland();
        }
    }

    private void RootCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CheckSnapToIsland();

    private void CheckSnapToIsland()
    {
        // Snap-to-Island detection: if released near top center directly over island
        if (IslandWindow.Instance is { } island)
        {
            double centerX = Left + Width / 2.0;
            double topY = Top;

            if (Math.Abs(centerX - island.IslandCenterDipX) < 160 && topY < island.IslandTopDipY + 36)
            {
                DockToIsland();
                return;
            }
        }
        PersistPlacementState();
    }

    private void PersistPlacementState()
    {
        var settingsService = _settingsService ?? IslandWindow.Instance?.Settings;
        if (settingsService != null &&
            !double.IsNaN(Left) && !double.IsInfinity(Left) &&
            !double.IsNaN(Top) && !double.IsInfinity(Top))
        {
            string modeStr = _viewMode.ToString();
            bool isCompact = _viewMode == PlayerViewMode.Compact;
            bool openState = !_isClosed || _closingForAppShutdown;
            settingsService.Update(s =>
            {
                s.MiniPlayerX = Left;
                s.MiniPlayerY = Top;
                s.MiniPlayerCompact = isCompact;
                s.MiniPlayerViewMode = modeStr;
                s.MiniPlayerOpen = openState;
            });
        }
    }

    public void CloseForAppShutdown()
    {
        _closingForAppShutdown = true;
        Close();
    }

    public void SetFullscreenSuppressed(bool suppressed)
    {
        if (_isClosed) return;
        _isFullscreenSuppressed = suppressed;

        if (suppressed)
        {
            _posTimer.Stop();
            _eqTimer.Stop();
            StopDiscRendering();
            CloseAudioOutputFlyout();
            Topmost = false;
            if (_wasShown)
            {
                Visibility = Visibility.Hidden;
            }
        }
        else
        {
            Topmost = _isPinned;
            if (_wasShown)
            {
                Visibility = Visibility.Visible;
            }
            RefreshAll();
            if (_mediaService.Current?.IsPlaying ?? false)
            {
                if (!_posTimer.IsEnabled) _posTimer.Start();
                if (!_eqTimer.IsEnabled) _eqTimer.Start();
            }
            EnsureDiscRendering();
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
            ShowcaseTitleText.Text = "Not Playing";
            ShowcaseArtistText.Text = "System Audio";
            ShowcaseLyricText.Visibility = Visibility.Collapsed;

            MiniArtDisc?.SetArtwork(null);
            CompactArtDisc?.SetArtwork(null);
            ShowcaseArtDisc?.SetArtwork(null);

            MiniPlayPauseGlyph.Text = "\uE768";
            CompactPlayPauseGlyph.Text = "\uE768";
            ShowcasePlayPauseGlyph.Text = "\uE768";

            SourceAppText.Text = "Media";
            SourceAppLogoBox.Visibility = Visibility.Collapsed;
            SourceAppIcon.Visibility = Visibility.Visible;
            SourceCycleIcon.Visibility = Visibility.Collapsed;
            ShowcaseSourceText.Text = "Media";
            TrackIndexBadge.Visibility = Visibility.Collapsed;
            MiniAmbientStop.Color = ThemeBrushes.AmberColor;
            if (CompactBrandPip != null) CompactBrandPip.Visibility = Visibility.Collapsed;
            if (ShowcaseProgressFill != null) ShowcaseProgressFill.Width = 0;

            ResetEqualizerBars();
            return;
        }

        string title = string.IsNullOrWhiteSpace(s.Title) ? "Unknown Track" : s.Title;
        string artist = string.IsNullOrWhiteSpace(s.Artist) ? "System Audio" : s.Artist;

        MiniTitleText.Text = title;
        MiniArtistText.Text = artist;
        CompactTitleText.Text = title;
        CompactArtistText.Text = artist;
        ShowcaseTitleText.Text = title;
        ShowcaseArtistText.Text = artist;

        MiniPlayPauseGlyph.Text = s.IsPlaying ? "\uE769" : "\uE768";
        CompactPlayPauseGlyph.Text = s.IsPlaying ? "\uE769" : "\uE768";
        ShowcasePlayPauseGlyph.Text = s.IsPlaying ? "\uE769" : "\uE768";

        // Artwork & Ambient Reactive Halo
        MiniArtDisc?.SetArtwork(s.Artwork);
        CompactArtDisc?.SetArtwork(s.Artwork);
        ShowcaseArtDisc?.SetArtwork(s.Artwork);

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

        ShowcaseSourceText.Text = brand.DisplayName;
        ShowcaseBrandPath.Data = brand.IconGeometry;
        ShowcaseBrandPath.Fill = brand.BrandBrush;

        UpdateShowcaseLyricLine();

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
            if (ShowcaseProgressTrack != null && ShowcaseProgressFill != null && ShowcaseProgressTrack.ActualWidth > 0)
            {
                ShowcaseProgressFill.Width = ShowcaseProgressTrack.ActualWidth * (pct / 100.0);
            }
        }
        else
        {
            if (MiniTimelineSlider.Value != 0) MiniTimelineSlider.Value = 0;
            if (ShowcaseProgressFill != null) ShowcaseProgressFill.Width = 0;
        }

        // Keep lyrics continuous and synced even if Dynamic Island is collapsed
        _lyricsService.UpdatePosition(pos);
        if (_activeDrawer == DrawerState.Lyrics)
        {
            UpdateLyricsLineProgress(pos);
        }
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
            _isUpdatingVolumeUi = true;
            try
            {
                MiniVolumeSlider.Value = Math.Clamp(vol * 100f, 0, 100);
            }
            finally
            {
                _isUpdatingVolumeUi = false;
            }
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
        if (_isUpdatingVolumeUi) return;
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
        var island = IslandWindow.Instance;
        if (island != null && (island.IsFullscreenSuppressed || island.ShouldThrottleVisuals || !SystemParameters.ClientAreaAnimation))
        {
            ResetEqualizerBars();
            return;
        }

        var s = _mediaService.Current;
        if (s == null || !s.IsPlaying)
        {
            ResetEqualizerBars();
            return;
        }

        if (_spectrum != null)
        {
            _spectrum.ReadBands(_spectrumBands);
            EqScale0.ScaleY = Math.Clamp(0.22 + _spectrumBands[0] * 0.78, 0.22, 1.0);
            EqScale1.ScaleY = Math.Clamp(0.22 + _spectrumBands[1] * 0.88, 0.22, 1.0);
            EqScale2.ScaleY = Math.Clamp(0.22 + _spectrumBands[2] * 0.84, 0.22, 1.0);
            EqScale3.ScaleY = Math.Clamp(0.22 + _spectrumBands[3] * 0.74, 0.22, 1.0);
        }
        else
        {
            EqScale0.ScaleY = 0.25 + _rng.NextDouble() * 0.55;
            EqScale1.ScaleY = 0.32 + _rng.NextDouble() * 0.68;
            EqScale2.ScaleY = 0.30 + _rng.NextDouble() * 0.62;
            EqScale3.ScaleY = 0.24 + _rng.NextDouble() * 0.52;
        }
    }

    private void ResetEqualizerBars()
    {
        EqScale0.ScaleY = 0.22;
        EqScale1.ScaleY = 0.22;
        EqScale2.ScaleY = 0.22;
        EqScale3.ScaleY = 0.22;
    }

    private void OnSettingsChanged()
    {
        string? style = _settingsService?.Current.AlbumArtStyle;
        Dispatcher.InvokeAsync(() =>
        {
            ApplyAlbumArtStyle(style);
            SyncLyricsAnimationStyleLabel();
            if (_activeDrawer == DrawerState.Lyrics)
            {
                ApplyLyricsActiveLine(_lyricsService.CurrentIndex, animate: false);
            }
            EnsureDiscRendering();
        });
    }

    internal void ApplyAlbumArtStyle(string? style)
    {
        string normalized = SettingsService.NormalizeAlbumArtStyle(style);
        MiniArtDisc?.SetStyle(normalized);
        CompactArtDisc?.SetStyle(normalized);
        ShowcaseArtDisc?.SetStyle(normalized);
        EnsureDiscRendering();
    }

    private PlayerDiscControl? GetActiveDiscControl() => _viewMode switch
    {
        PlayerViewMode.Compact => CompactArtDisc,
        PlayerViewMode.Showcase => ShowcaseArtDisc,
        _ => MiniArtDisc
    };

    private void EnsureDiscRendering()
    {
        if (_discRendering || _isFullscreenSuppressed || _isClosed) return;
        string style = SettingsService.NormalizeAlbumArtStyle(_settingsService?.Current.AlbumArtStyle);
        if (style == "StaticSquircle") return;

        bool isPlaying = _mediaService.Current?.IsPlaying ?? false;
        var activeDisc = GetActiveDiscControl();
        bool settled = activeDisc?.IsSettled ?? true;
        if (!isPlaying && settled) return;

        _discRendering = true;
        _lastDiscFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnDiscRendering;
    }

    private void StopDiscRendering()
    {
        if (!_discRendering) return;
        CompositionTarget.Rendering -= OnDiscRendering;
        _discRendering = false;
    }

    private void OnDiscRendering(object? sender, EventArgs e)
    {
        if (_isFullscreenSuppressed || _isClosed)
        {
            StopDiscRendering();
            return;
        }

        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastDiscFrame) return;
        double dt = _lastDiscFrame == TimeSpan.Zero ? 1.0 / 60.0 : (now - _lastDiscFrame).TotalSeconds;
        _lastDiscFrame = now;

        bool isPlaying = _mediaService.Current?.IsPlaying ?? false;
        bool reduced = !SystemParameters.ClientAreaAnimation ||
                       (_settingsService?.Current.ReduceMotion ?? false) ||
                       (_settingsService?.Current.LowMemoryMode ?? false);

        float bass = 0f;
        if (isPlaying && _spectrum != null)
        {
            _spectrum.ReadBands(_spectrumBands);
            bass = _spectrumBands[0];
        }

        var activeDisc = GetActiveDiscControl();
        bool activeSettled = activeDisc?.StepPhysics(dt, isPlaying, bass, reduced) ?? true;
        if (activeSettled)
        {
            StopDiscRendering();
        }
    }

    // ── Event Handlers ───────────────────────────────────────────────

    private void OnMediaChanged() =>
        Dispatcher.InvokeAsync(() =>
        {
            RefreshAll();
            if (!_isFullscreenSuppressed && (_mediaService.Current?.IsPlaying ?? false))
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
            EnsureDiscRendering();
        });

    private void OnTrackChanged() =>
        Dispatcher.InvokeAsync(() =>
        {
            RefreshAll();
            MiniArtDisc?.PulseTrackChange();
            EnsureDiscRendering();
            // If the song list drawer is currently showing album tracks, auto-fetch for the new song
            if (_activeDrawer == DrawerState.Songs && _activeSongsTab == SongsTab.AlbumTracks)
            {
                TriggerFetchAlbumTracks();
            }
        });

    private void OnLyricLineChanged(string prev, string current, string next, int idx) =>
        Dispatcher.InvokeAsync(() =>
        {
            UpdateShowcaseLyricLine();
            UpdateLyricsDisplay();
        });

    private void OnLyricsLoaded() =>
        Dispatcher.InvokeAsync(() =>
        {
            UpdateShowcaseLyricLine();
            UpdateLyricsDisplay();
        });

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
            : SubtleSurfaceBrush;
        PinGlyph.Foreground = _isPinned
            ? ThemeBrushes.DarkInkDark
            : (Brush)FindResource("PrimaryTextBrush");
    }

    private void CompactMode_Click(object sender, RoutedEventArgs e) => SetViewMode(PlayerViewMode.Compact);
    private void ExpandFromCompact_Click(object sender, RoutedEventArgs e) => SetViewMode(PlayerViewMode.Full);
    private void ShowcaseMode_Click(object sender, RoutedEventArgs e) => SetViewMode(PlayerViewMode.Showcase);
    private void ExitShowcase_Click(object sender, RoutedEventArgs e) => SetViewMode(PlayerViewMode.Full);

    private void ShowcaseArtStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        string current = SettingsService.NormalizeAlbumArtStyle(_settingsService.Current.AlbumArtStyle);
        var styles = SettingsService.AvailableAlbumArtStyles;
        int idx = 0;
        for (int i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles[i], current, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }
        string next = styles[(idx + 1) % styles.Count];
        _settingsService.Update(s => s.AlbumArtStyle = next, nameof(AppSettings.AlbumArtStyle));
    }

    internal void SetCompactModeExternal(bool compact) =>
        SetViewMode(compact ? PlayerViewMode.Compact : PlayerViewMode.Full, persist: false);

    internal void SetViewModeExternal(string? mode)
    {
        string normalized = SettingsService.NormalizeMiniPlayerViewMode(mode);
        var target = normalized switch
        {
            "Compact" => PlayerViewMode.Compact,
            "Showcase" => PlayerViewMode.Showcase,
            _ => PlayerViewMode.Full
        };
        SetViewMode(target, persist: false);
    }

    private void SetViewMode(PlayerViewMode mode, bool persist = true)
    {
        _viewMode = mode;

        if (mode != PlayerViewMode.Full)
        {
            CloseDrawers();
            CloseAudioOutputFlyout();
        }

        FullPlayerView.Visibility = mode == PlayerViewMode.Full ? Visibility.Visible : Visibility.Collapsed;
        CompactPillView.Visibility = mode == PlayerViewMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        ShowcaseView.Visibility = mode == PlayerViewMode.Showcase ? Visibility.Visible : Visibility.Collapsed;

        MiniAmbientHalo.Opacity = mode == PlayerViewMode.Showcase ? 0.34 : 0.22;
        MiniAmbientHalo.Height = mode == PlayerViewMode.Showcase ? 210 : 140;

        switch (mode)
        {
            case PlayerViewMode.Compact:
                FadeInElement(CompactPillView);
                AnimateWindowSize(CompactWidth, CompactHeight);
                break;
            case PlayerViewMode.Showcase:
                UpdateShowcaseLyricLine();
                FadeInElement(ShowcaseView);
                AnimateWindowSize(ShowcaseWidth, ShowcaseHeight);
                break;
            default:
                FadeInElement(FullPlayerView);
                AnimateWindowSize(FullWidth, CalculateDrawerTargetHeight());
                break;
        }

        EnsureDiscRendering();
        if (persist)
        {
            PersistPlacementState();
        }
    }

    private int _resizeAnimVersion;

    private static void FadeInElement(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1.0;
            return;
        }
        element.Opacity = 0.0;
        var fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, fade);
    }

    private void ClampWindowToCurrentWorkArea(double targetW, double targetH)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        IntPtr monitor = hwnd != IntPtr.Zero
            ? MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;
        if (monitor == IntPtr.Zero) return;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi)) return;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0.1) scale = 1.0;

        var (clampedX, clampedY) = WindowPlacementHelper.ClampToWorkArea(
            Left,
            Top,
            targetW,
            targetH,
            mi.rcWork.Left / scale,
            mi.rcWork.Top / scale,
            mi.rcWork.Right / scale,
            mi.rcWork.Bottom / scale,
            margin: 12.0);
        Left = clampedX;
        Top = clampedY;
    }

    private void AnimateWindowSize(double targetW, double targetH)
    {
        int animVersion = ++_resizeAnimVersion;
        double cardTargetW = Math.Max(40, targetW - 20);
        double cardTargetH = Math.Max(40, targetH - 20);

        ClampWindowToCurrentWorkArea(targetW, targetH);

        if (!SystemParameters.ClientAreaAnimation)
        {
            RootCard.BeginAnimation(WidthProperty, null);
            RootCard.BeginAnimation(HeightProperty, null);
            RootCard.Width = cardTargetW;
            RootCard.Height = cardTargetH;
            Width = targetW;
            Height = targetH;
            return;
        }

        // Grow the outer transparent Window bounds immediately before expanding so DWM allocates
        // the layered surface once, then smoothly morph RootCard inside it at 60-240 Hz.
        bool growing = targetW > Width || targetH > Height;
        if (growing)
        {
            Width = Math.Max(Width, targetW);
            Height = Math.Max(Height, targetH);
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var wAnim = new DoubleAnimation(cardTargetW, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        var hAnim = new DoubleAnimation(cardTargetH, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        hAnim.Completed += (_, _) =>
        {
            if (animVersion != _resizeAnimVersion) return;
            Width = targetW;
            Height = targetH;
        };

        RootCard.BeginAnimation(WidthProperty, wAnim);
        RootCard.BeginAnimation(HeightProperty, hAnim);
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
        if (SafeClipboard.TrySetText(text))
        {
            CopyTrackGlyph.Text = "\uE73E"; // Checkmark feedback
            _copyFeedbackTimer ??= CreateCopyFeedbackTimer();
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
    }

    private DispatcherTimer CreateCopyFeedbackTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        timer.Tick += (_, _) =>
        {
            CopyTrackGlyph.Text = "\uE8C8";
            timer.Stop();
        };
        return timer;
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
            : SubtleSurfaceBrush;
        ToggleLyricsBtn.Background = drawer == DrawerState.Lyrics
            ? (Brush)FindResource("AccentBrush")
            : SubtleSurfaceBrush;

        FadeInElement(DrawerHost);
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

        ToggleSongListBtn.Background = SubtleSurfaceBrush;
        ToggleLyricsBtn.Background = SubtleSurfaceBrush;

        AnimateWindowSize(FullWidth, FullHeightBase);
    }

}
