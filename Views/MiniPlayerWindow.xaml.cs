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
    private readonly float[] _spectrumBands = new float[AudioSpectrum.BandCount];

    private readonly DispatcherTimer _posTimer;
    private readonly DispatcherTimer _eqTimer;
    private readonly Random _rng = new();

    private bool _isUserSeeking;
    private bool _isUserVolumeSeeking;
    private bool _isUpdatingVolumeUi;
    private int _activePlayersRenderVersion;
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
            PersistPlacementState();
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
        if (IslandWindow.Instance?.Settings is { } settingsService &&
            !double.IsNaN(Left) && !double.IsInfinity(Left) &&
            !double.IsNaN(Top) && !double.IsInfinity(Top))
        {
            settingsService.Update(s =>
            {
                s.MiniPlayerX = Left;
                s.MiniPlayerY = Top;
                s.MiniPlayerCompact = _isCompactMode;
            });
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
            : SubtleSurfaceBrush;
        PinGlyph.Foreground = _isPinned
            ? ThemeBrushes.DarkInkDark
            : (Brush)FindResource("PrimaryTextBrush");
    }

    private void CompactMode_Click(object sender, RoutedEventArgs e) => SetCompactMode(true);
    private void ExpandFromCompact_Click(object sender, RoutedEventArgs e) => SetCompactMode(false);

    internal void SetCompactModeExternal(bool compact) => SetCompactMode(compact);

    private void SetCompactMode(bool compact)
    {
        _isCompactMode = compact;

        if (compact)
        {
            CloseDrawers();
            FullPlayerView.Visibility = Visibility.Collapsed;
            CompactPillView.Visibility = Visibility.Visible;
            FadeInElement(CompactPillView);
            AnimateWindowSize(CompactWidth, CompactHeight);
        }
        else
        {
            CompactPillView.Visibility = Visibility.Collapsed;
            FullPlayerView.Visibility = Visibility.Visible;
            FadeInElement(FullPlayerView);
            AnimateWindowSize(FullWidth, FullHeightBase);
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
