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

public partial class MiniPlayerWindow
{
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
        btn.Background = active ? SegmentActiveBgBrush : Brushes.Transparent;
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

        Task.Run(async () =>
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
        }, token).FireAndForget("MiniPlayer.Tracklist");
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
                Background = isCurrent ? CurrentTrackBgBrush : IdleTrackBgBrush,
                BorderBrush = isCurrent
                    ? (Brush)FindResource("AccentBrush")
                    : SubtleSurfaceBrush,
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

                itemBorder.Background = isCurrent ? CurrentTrackBgBrush : IdleTrackBgBrush;
                itemBorder.BorderBrush = isCurrent
                    ? (Brush)FindResource("AccentBrush")
                    : SubtleSurfaceBrush;

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
        int version = ++_activePlayersRenderVersion;
        var sessions = await _mediaService.GetActiveSessionsAsync();
        if (version != _activePlayersRenderVersion) return;

        ActivePlayersListHost.Children.Clear();

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
                Background = sess.IsCurrent ? ActivePlayerBgBrush : InactivePlayerBgBrush,
                BorderBrush = sess.IsCurrent ? ActivePlayerBorderBrush : InactivePlayerBorderBrush,
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
                Background = ArtFallbackBgBrush,
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
                    Background = BrandPipBgBrush,
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
                Background = sess.IsCurrent ? ActivePlayerBadgeBgBrush : InactivePlayerBorderBrush,
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
                Background = IdleTrackBgBrush,
                BorderBrush = SubtleSurfaceBrush,
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
