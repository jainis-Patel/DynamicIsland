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

    // ── 3. Playback History Listing ──────────────────────────────────

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _songListService.ClearHistory();
        e.Handled = true;
    }

    private void RenderHistoryTab()
    {
        HistoryListHost.Children.Clear();
        var history = _songListService.History;

        if (ClearHistoryBtn != null)
        {
            ClearHistoryBtn.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (history.Count == 0)
        {
            HistoryListHost.Children.Add(new TextBlock
            {
                Text = "No songs recorded yet.",
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
                Padding = new Thickness(7, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 3.5),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var artBorder = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Background = ArtFallbackBgBrush,
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (rec.Artwork != null)
            {
                artBorder.Child = new Image { Source = rec.Artwork, Stretch = Stretch.UniformToFill };
            }
            else
            {
                artBorder.Child = new TextBlock
                {
                    Style = (Style)FindResource("Glyph"),
                    Text = "\uE8D6",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("SecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            Grid.SetColumn(artBorder, 0);
            grid.Children.Add(artBorder);

            var sp = new StackPanel { Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
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
                Text = string.IsNullOrWhiteSpace(rec.SourceApp) ? rec.Artist : $"{rec.Artist} • {rec.SourceApp}",
                FontSize = 8.5,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            };
            sp.Children.Add(title);
            sp.Children.Add(meta);
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            var time = new TextBlock
            {
                Text = rec.TimeAgo,
                FontSize = 8.5,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(time, 2);
            grid.Children.Add(time);

            item.Child = grid;

            item.MouseLeftButtonDown += (_, e) =>
            {
                SafeClipboard.TrySetText($"{rec.Artist} - {rec.Title}");
                e.Handled = true;
            };
            item.MouseRightButtonUp += (_, e) =>
            {
                var curBrand = _mediaService.Current?.BrandInfo ?? MediaPlayerBrandService.Detect(_mediaService.Current?.SourceAppUserModelId);
                MediaPlayerBrandService.OpenTrackInActiveService(curBrand, rec.Title, rec.Artist);
                e.Handled = true;
            };

            item.ToolTip = $"Left-click to copy \"{rec.Artist} - {rec.Title}\" · Right-click to open in player";

            HistoryListHost.Children.Add(item);
        }

        if (_activeDrawer == DrawerState.Songs && _activeSongsTab == SongsTab.History)
        {
            AnimateWindowSize(FullWidth, CalculateDrawerTargetHeight());
        }
    }

    // ── 4. Inline Audio Output Switcher ──────────────────────────────

    private void MiniPlayerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && AudioOutputFlyout.Visibility == Visibility.Visible)
        {
            CloseAudioOutputFlyout();
            e.Handled = true;
        }
    }

    private void OnAudioEndpointsChanged(IReadOnlyList<AudioEndpointInfo> endpoints) =>
        Dispatcher.InvokeAsync(() =>
        {
            SyncAudioOutputButton();
            if (AudioOutputFlyout.Visibility == Visibility.Visible)
            {
                RenderAudioOutputFlyout();
            }
        });

    private void OnDefaultAudioEndpointChanged(AudioEndpointInfo info) =>
        Dispatcher.InvokeAsync(() =>
        {
            SyncAudioOutputButton();
            if (AudioOutputFlyout.Visibility == Visibility.Visible)
            {
                RenderAudioOutputFlyout();
            }
        });

    private void SyncAudioOutputButton()
    {
        if (MiniOutputDeviceGlyph == null) return;
        var cur = _audioEndpointService?.CurrentDefault;
        if (cur != null)
        {
            MiniOutputDeviceGlyph.Text = cur.Glyph;
            AudioOutputBtn.ToolTip = $"Output: {cur.Name} (Click to switch)";
        }
        else
        {
            MiniOutputDeviceGlyph.Text = "\uE7F5";
            AudioOutputBtn.ToolTip = "Switch Audio Output Device";
        }
    }

    private void AudioOutputBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AudioOutputFlyout.Visibility == Visibility.Visible)
        {
            CloseAudioOutputFlyout();
        }
        else
        {
            RenderAudioOutputFlyout();
            AudioOutputFlyout.Visibility = Visibility.Visible;
            FadeInElement(AudioOutputFlyout);
            AudioOutputBtn.Background = (Brush)FindResource("AccentBrush");
            MiniOutputDeviceGlyph.Foreground = ThemeBrushes.DarkInkDark;
        }
        e.Handled = true;
    }

    private void CloseAudioOutputFlyout_Click(object sender, RoutedEventArgs e)
    {
        CloseAudioOutputFlyout();
        e.Handled = true;
    }

    private void CloseAudioOutputFlyout()
    {
        if (AudioOutputFlyout == null) return;
        AudioOutputFlyout.Visibility = Visibility.Collapsed;
        if (AudioOutputBtn != null) AudioOutputBtn.Background = SubtleSurfaceBrush;
        if (MiniOutputDeviceGlyph != null) MiniOutputDeviceGlyph.Foreground = (Brush)FindResource("PrimaryTextBrush");
    }

    private void RenderAudioOutputFlyout()
    {
        AudioOutputListHost.Children.Clear();
        var endpoints = _audioEndpointService?.Endpoints;
        if (endpoints == null || endpoints.Count == 0)
        {
            AudioOutputListHost.Children.Add(new TextBlock
            {
                Text = "No active audio output devices found.",
                FontSize = 10,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var ep in endpoints)
        {
            var row = new Border
            {
                Background = ep.IsDefault ? ActivePlayerBgBrush : IdleTrackBgBrush,
                BorderBrush = ep.IsDefault ? ActivePlayerBorderBrush : SubtleSurfaceBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 5.5, 8, 5.5),
                Margin = new Thickness(0, 0, 0, 3.5),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = new TextBlock
            {
                Style = (Style)FindResource("Glyph"),
                Text = ep.Glyph,
                FontSize = 10.5,
                Foreground = ep.IsDefault ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("PrimaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(glyph, 0);
            grid.Children.Add(glyph);

            var nameText = new TextBlock
            {
                Text = ep.Name,
                FontSize = 10,
                FontWeight = ep.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            };
            Grid.SetColumn(nameText, 1);
            grid.Children.Add(nameText);

            if (ep.IsDefault)
            {
                var check = new TextBlock
                {
                    Style = (Style)FindResource("Glyph"),
                    Text = "\uE73E",
                    FontSize = 9.5,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(check, 2);
                grid.Children.Add(check);
            }
            else if (ep.BatteryPercent.HasValue)
            {
                var batt = new TextBlock
                {
                    Text = $"{ep.BatteryPercent.Value}%",
                    FontSize = 8.5,
                    Foreground = (Brush)FindResource("SecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(batt, 2);
                grid.Children.Add(batt);
            }

            row.Child = grid;
            string endpointId = ep.Id;
            row.MouseLeftButtonDown += (_, args) =>
            {
                _audioEndpointService?.SetDefaultEndpoint(endpointId);
                CloseAudioOutputFlyout();
                args.Handled = true;
            };
            AudioOutputListHost.Children.Add(row);
        }
    }

    // ── 5. Scrollable & Animated Karaoke Lyrics Display ───────────────

    private sealed class LyricLineVisual
    {
        public required Border Container { get; init; }
        public required TextBlock TextBlock { get; init; }
        public Border? ProgressBar { get; init; }
        public ScaleTransform? ProgressScale { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required TranslateTransform Translate { get; init; }
        public int Index { get; init; }
        public TimeSpan Timestamp { get; init; }
        public TimeSpan? NextTimestamp { get; init; }
    }

    private readonly List<LyricLineVisual> _lyricVisuals = new();
    private TrackLyrics? _renderedLyricsTrack;
    private int _renderedActiveLineIdx = -2;
    private long _userLyricsScrollUntilTick;
    private int _lyricsScrollAnimVersion;

    private string CurrentLyricsAnimStyle =>
        SettingsService.NormalizeLyricsAnimationStyle(_settingsService?.Current.LyricsAnimationStyle);

    private bool IsReducedLyricsMotion =>
        !SystemParameters.ClientAreaAnimation ||
        (_settingsService?.Current.ReduceMotion ?? false) ||
        (_settingsService?.Current.LowMemoryMode ?? false);

    private void SyncLyricsAnimationStyleLabel()
    {
        if (LyricsAnimStyleText == null) return;
        LyricsAnimStyleText.Text = CurrentLyricsAnimStyle switch
        {
            "KaraokeGlow" => "✨ Glow",
            "MinimalFade" => "• Minimal",
            _ => "✦ Wave"
        };
    }

    private void LyricsAnimStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService == null) return;
        string current = CurrentLyricsAnimStyle;
        var styles = SettingsService.AvailableLyricsAnimationStyles;
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
        _settingsService.Update(s => s.LyricsAnimationStyle = next, nameof(AppSettings.LyricsAnimationStyle));
        SyncLyricsAnimationStyleLabel();
        if (_activeDrawer == DrawerState.Lyrics)
        {
            ApplyLyricsActiveLine(_lyricsService.CurrentIndex, animate: true);
        }
        e.Handled = true;
    }

    private void LyricsRetry_Click(object sender, RoutedEventArgs e)
    {
        var s = _mediaService.Current;
        if (s != null && !string.IsNullOrWhiteSpace(s.Title))
        {
            _lyricsService.LoadLyricsForTrack(s.Title, s.Artist, s.Duration, forceRetry: true);
        }
        e.Handled = true;
    }

    private void LyricsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _userLyricsScrollUntilTick = Environment.TickCount64 + 3500;
    }

    private void UpdateShowcaseLyricLine()
    {
        if (ShowcaseLyricText == null) return;
        if (_viewMode != PlayerViewMode.Showcase || !_lyricsService.HasLyrics || _lyricsService.IsInstrumental)
        {
            ShowcaseLyricText.Visibility = Visibility.Collapsed;
            return;
        }

        string line = _lyricsService.CurrentLineText;
        if (string.IsNullOrWhiteSpace(line))
        {
            ShowcaseLyricText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowcaseLyricText.Text = line;
            ShowcaseLyricText.Visibility = Visibility.Visible;
        }
    }

    internal void UpdateLyricsDisplay()
    {
        if (_activeDrawer != DrawerState.Lyrics) return;

        SyncLyricsAnimationStyleLabel();

        if (_lyricsService.IsLoading)
        {
            _renderedLyricsTrack = null;
            _lyricVisuals.Clear();
            LyricsLinesHost.Children.Clear();
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
            LyricsEmptyStatePanel.Visibility = Visibility.Visible;
            LyricsEmptyText.Text = "♪ Searching lyrics on LRCLIB...";
            LyricsRetryBtn.Visibility = Visibility.Collapsed;
            LyricsStatusText.Text = "Loading...";
            return;
        }

        if (_lyricsService.IsInstrumental)
        {
            _renderedLyricsTrack = null;
            _lyricVisuals.Clear();
            LyricsLinesHost.Children.Clear();
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
            LyricsEmptyStatePanel.Visibility = Visibility.Visible;
            LyricsEmptyText.Text = "♪ Instrumental Track (No Lyrics)";
            LyricsRetryBtn.Visibility = Visibility.Collapsed;
            LyricsStatusText.Text = "Instrumental";
            return;
        }

        var track = _lyricsService.CurrentTrack;
        if (track == null || !track.HasAnyLyrics)
        {
            _renderedLyricsTrack = null;
            _lyricVisuals.Clear();
            LyricsLinesHost.Children.Clear();
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
            LyricsEmptyStatePanel.Visibility = Visibility.Visible;
            LyricsEmptyText.Text = "♪ No lyrics available for this song.";
            LyricsRetryBtn.Visibility = _mediaService.Current != null ? Visibility.Visible : Visibility.Collapsed;
            LyricsStatusText.Text = "No Lyrics";
            return;
        }

        LyricsEmptyStatePanel.Visibility = Visibility.Collapsed;
        LyricsScrollViewer.Visibility = Visibility.Visible;

        if (!ReferenceEquals(track, _renderedLyricsTrack))
        {
            RebuildLyricsStage(track);
        }

        if (track.HasSyncedLyrics && !track.IsPlainLyricsOnly)
        {
            LyricsStatusText.Text = "Synced";
            int activeIdx = _lyricsService.CurrentIndex;
            if (activeIdx != _renderedActiveLineIdx)
            {
                ApplyLyricsActiveLine(activeIdx, animate: true);
            }
            if (_mediaService.Current is { } media)
            {
                UpdateLyricsLineProgress(media.GetPosition(DateTimeOffset.Now));
            }
        }
        else
        {
            LyricsStatusText.Text = "Plain Lyrics";
        }
    }

    private void RebuildLyricsStage(TrackLyrics track)
    {
        _renderedLyricsTrack = track;
        _renderedActiveLineIdx = -2;
        _lyricVisuals.Clear();
        LyricsLinesHost.Children.Clear();

        if (!track.HasSyncedLyrics || track.IsPlainLyricsOnly)
        {
            string plainText = !string.IsNullOrWhiteSpace(track.PlainLyrics)
                ? track.PlainLyrics
                : string.Join("\n", track.SyncedLines.Select(l => l.Text));

            var rawLines = plainText.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            foreach (var raw in rawLines)
            {
                string trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    LyricsLinesHost.Children.Add(new Border { Height = 8 });
                    continue;
                }
                LyricsLinesHost.Children.Add(new TextBlock
                {
                    Text = trimmed,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)FindResource("PrimaryTextBrush"),
                    Opacity = 0.88,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(6, 3, 6, 3)
                });
            }
            LyricsScrollViewer.ScrollToTop();
            return;
        }

        var lines = track.SyncedLines;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            TimeSpan? nextTs = i + 1 < lines.Count ? lines[i + 1].Timestamp : null;

            var scale = new ScaleTransform(1.0, 1.0);
            var translate = new TranslateTransform(0.0, 0.0);
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(translate);

            var tb = new TextBlock
            {
                Text = line.Text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("SecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };

            var progressScale = new ScaleTransform(0.0, 1.0);
            var progressBar = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(12, 4, 12, 0),
                Background = (Brush)FindResource("AccentBrush"),
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = progressScale,
                Visibility = Visibility.Collapsed
            };

            var stack = new StackPanel();
            stack.Children.Add(tb);
            stack.Children.Add(progressBar);

            var container = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(4, 2, 4, 2),
                Background = Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = tg,
                Cursor = Cursors.Hand,
                Opacity = 0.45,
                Child = stack,
                ToolTip = $"Seek to {FormatTime(line.Timestamp)}"
            };

            TimeSpan targetTs = line.Timestamp;
            container.MouseLeftButtonDown += (_, e) =>
            {
                _userLyricsScrollUntilTick = 0;
                _mediaService.Seek(targetTs);
                e.Handled = true;
            };

            LyricsLinesHost.Children.Add(container);
            _lyricVisuals.Add(new LyricLineVisual
            {
                Container = container,
                TextBlock = tb,
                ProgressBar = progressBar,
                ProgressScale = progressScale,
                Scale = scale,
                Translate = translate,
                Index = i,
                Timestamp = line.Timestamp,
                NextTimestamp = nextTs
            });
        }
    }

    private void ApplyLyricsActiveLine(int activeIdx, bool animate)
    {
        _renderedActiveLineIdx = activeIdx;
        if (_lyricVisuals.Count == 0) return;

        string style = IsReducedLyricsMotion ? "MinimalFade" : CurrentLyricsAnimStyle;
        bool useAnimation = animate && !IsReducedLyricsMotion;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var animDuration = TimeSpan.FromMilliseconds(240);

        Brush accentBrush = (Brush)FindResource("AccentBrush");
        Brush secondaryBrush = (Brush)FindResource("SecondaryBrush");

        LyricLineVisual? activeVisual = null;

        foreach (var v in _lyricVisuals)
        {
            bool isActive = v.Index == activeIdx;
            if (isActive) activeVisual = v;

            int dist = activeIdx >= 0 ? Math.Abs(v.Index - activeIdx) : v.Index + 1;

            double targetOpacity = style == "MinimalFade"
                ? (isActive ? 1.0 : 0.42)
                : isActive
                    ? 1.0
                    : dist == 1 ? 0.58 : dist == 2 ? 0.36 : 0.22;

            double targetScale = isActive
                ? (style == "KaraokeGlow" ? 1.06 : style == "SpringWave" ? 1.05 : 1.0)
                : 1.0;

            v.TextBlock.FontSize = isActive ? 13.5 : 11.5;
            v.TextBlock.FontWeight = isActive ? FontWeights.Bold : FontWeights.Medium;
            v.TextBlock.Foreground = isActive ? accentBrush : secondaryBrush;

            v.Container.Background = (isActive && style == "KaraokeGlow")
                ? CurrentTrackBgBrush
                : Brushes.Transparent;

            if (v.ProgressBar != null)
            {
                v.ProgressBar.Visibility = (isActive && style == "KaraokeGlow")
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                if (!isActive && v.ProgressScale != null)
                {
                    v.ProgressScale.ScaleX = 0.0;
                }
            }

            if (useAnimation)
            {
                v.Container.BeginAnimation(OpacityProperty, new DoubleAnimation(targetOpacity, animDuration) { EasingFunction = ease });
                v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, animDuration) { EasingFunction = ease });
                v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, animDuration) { EasingFunction = ease });

                if (style == "SpringWave" && dist <= 3)
                {
                    double startY = isActive ? 5.0 : (v.Index > activeIdx ? 3.5 : -2.5);
                    v.Translate.Y = startY;
                    v.Translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
                }
                else
                {
                    v.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                    v.Translate.Y = 0.0;
                }
            }
            else
            {
                v.Container.BeginAnimation(OpacityProperty, null);
                v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                v.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                v.Container.Opacity = targetOpacity;
                v.Scale.ScaleX = targetScale;
                v.Scale.ScaleY = targetScale;
                v.Translate.Y = 0.0;
            }
        }

        if (activeVisual != null && Environment.TickCount64 >= _userLyricsScrollUntilTick)
        {
            ScrollToLyricVisual(activeVisual, useAnimation);
        }
    }

    private void ScrollToLyricVisual(LyricLineVisual target, bool animate)
    {
        if (LyricsScrollViewer == null || LyricsScrollViewer.ViewportHeight <= 0)
        {
            target.Container.BringIntoView();
            return;
        }

        try
        {
            var pt = target.Container.TransformToAncestor(LyricsLinesHost).Transform(new Point(0, 0));
            double centerOffset = pt.Y - (LyricsScrollViewer.ViewportHeight / 2.0) + (target.Container.ActualHeight / 2.0);
            double clampedOffset = Math.Clamp(centerOffset, 0.0, LyricsScrollViewer.ScrollableHeight);

            if (!animate)
            {
                LyricsScrollViewer.ScrollToVerticalOffset(clampedOffset);
                return;
            }

            int version = ++_lyricsScrollAnimVersion;
            double start = LyricsScrollViewer.VerticalOffset;
            double delta = clampedOffset - start;
            if (Math.Abs(delta) < 1.0)
            {
                LyricsScrollViewer.ScrollToVerticalOffset(clampedOffset);
                return;
            }

            var sw = Stopwatch.StartNew();
            const double durationMs = 260.0;
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (version != _lyricsScrollAnimVersion || _isClosed)
                {
                    CompositionTarget.Rendering -= handler;
                    return;
                }
                double t = Math.Clamp(sw.Elapsed.TotalMilliseconds / durationMs, 0.0, 1.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
                LyricsScrollViewer.ScrollToVerticalOffset(start + delta * eased);
                if (t >= 1.0)
                {
                    CompositionTarget.Rendering -= handler;
                }
            };
            CompositionTarget.Rendering += handler;
        }
        catch
        {
            target.Container.BringIntoView();
        }
    }

    private void UpdateLyricsLineProgress(TimeSpan pos)
    {
        if (_renderedActiveLineIdx < 0 || _renderedActiveLineIdx >= _lyricVisuals.Count) return;
        if (CurrentLyricsAnimStyle != "KaraokeGlow" || IsReducedLyricsMotion) return;

        var active = _lyricVisuals[_renderedActiveLineIdx];
        if (active.ProgressScale == null) return;

        TimeSpan endTs = active.NextTimestamp ?? (_mediaService.Current?.Duration > active.Timestamp
            ? _mediaService.Current!.Duration
            : active.Timestamp + TimeSpan.FromSeconds(4));

        double totalMs = Math.Max(400.0, (endTs - active.Timestamp).TotalMilliseconds);
        double frac = Math.Clamp((pos - active.Timestamp).TotalMilliseconds / totalMs, 0.0, 1.0);
        active.ProgressScale.ScaleX = frac;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isClosed) return;
        _isClosed = true;

        DynamicIsland.LiquidGlass.GlassHost.Detach(RootCard);
        StopDiscRendering();
        PersistPlacementState();
        base.OnClosed(e);

        // Stop and unhook timers to prevent background CPU usage
        _posTimer.Stop();
        _eqTimer.Stop();
        _copyFeedbackTimer?.Stop();

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
        if (_settingsService != null)
        {
            _settingsService.Changed -= OnSettingsChanged;
        }
        if (_audioEndpointService != null)
        {
            _audioEndpointService.EndpointsChanged -= OnAudioEndpointsChanged;
            _audioEndpointService.DefaultChanged -= OnDefaultAudioEndpointChanged;
        }
        PreviewKeyDown -= MiniPlayerWindow_PreviewKeyDown;
    }
}
