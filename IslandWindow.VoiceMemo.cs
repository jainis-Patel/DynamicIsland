using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    //  Fluid Voice Notes & Audio Dictation
    // =================================================================

    private void ToggleVoiceRecording()
    {
        if (_voiceMemoService.IsRecording)
        {
            _voiceMemoService.StopRecording();
            if (_voiceMemoContainer != null)
            {
                HeroRecordGlyph.Text = "\uE7C8";
                HeroRecordStatus.Text = "Saving voice note to Drop Shelf...";
                RecordHalo.Opacity = 0;
            }
            EvaluateActivity();
        }
        else if (_voiceMemoService.StartRecording())
        {
            if (_voiceMemoContainer != null)
            {
                HeroRecordGlyph.Text = "\uE71A";
                HeroRecordStatus.Text = "Recording audio... Click to save";
                RecordHalo.Opacity = 0.5;
            }
            EvaluateActivity();
            EnsureRendering();
            _machine.PushToast(new ToastContent(
                "\uE7C8",
                "Recording Started",
                "Audio dictation memo active",
                ThemeBrushes.RedColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "voicememo",
                Priority: ToastPriority.System));
        }
    }

    internal void HeroRecordBtn_Click(object sender, RoutedEventArgs e) => ToggleVoiceRecording();

    private void OnVoiceMemoTick(float peak, TimeSpan elapsed)
    {
        CompactRecordText.Text = elapsed.ToString(@"mm\:ss");

        if (_voiceMemoContainer != null)
        {
            HeroRecordTimer.Text = elapsed.ToString(@"mm\:ss\.f");

            double h = Math.Clamp(peak * 14.0, 3.0, 14.0);
            RecBar0.Height = Math.Max(3, h * 0.5);
            RecBar1.Height = Math.Max(3, h * 0.8);
            RecBar2.Height = Math.Max(3, h * 1.0);
            RecBar3.Height = Math.Max(3, h * 0.7);
            RecBar4.Height = Math.Max(3, h * 0.4);

            RecordHalo.Opacity = Math.Clamp(0.2 + peak * 0.8, 0.2, 0.95);
        }
        EnsureRendering();
    }

    private readonly System.Collections.ObjectModel.ObservableCollection<VoiceMemoInfo> _voiceMemos = new();

    private void InitializeVoiceMemoUI()
    {
        if (_voiceMemoContainer == null) return;
        if (VoiceMemosItemsControl != null && VoiceMemosItemsControl.ItemsSource == null)
        {
            VoiceMemosItemsControl.ItemsSource = _voiceMemos;
        }

        SyncObservableCollection(_voiceMemos, _voiceMemoService.RecentMemos);
        bool hasMemos = _voiceMemos.Count > 0;
        if (VoiceMemosScrollViewer != null)
        {
            VoiceMemosScrollViewer.Visibility = hasMemos ? Visibility.Visible : Visibility.Collapsed;
        }
        NoMemoPlaceholder.Visibility = hasMemos ? Visibility.Collapsed : Visibility.Visible;

        if (_voiceMemoService.LastSavedMemo is { } memo)
        {
            LastMemoFileName.Text = memo.FileName;
            LastMemoDuration.Text = FormatTime(memo.Duration);
        }
    }

    private void OnVoiceMemoCompleted(VoiceMemoInfo info)
    {
        if (_voiceMemoContainer != null)
        {
            HeroRecordTimer.Text = "00:00";
            HeroRecordStatus.Text = "Ready";
            HeroRecordGlyph.Text = "\uE7C8";
            RecordHalo.Opacity = 0;
        }

        InitializeVoiceMemoUI();

        _shelfService.AddPaths(new[] { info.FilePath });
        _machine.PushToast(new ToastContent(
            "\uE7C8",
            "Voice Memo Saved",
            $"{info.FileName} \u00B7 {FormatTime(info.Duration)}",
            ThemeBrushes.RedColor,
            TimeSpan.FromSeconds(3.5),
            TargetView: "voicememo",
            Priority: ToastPriority.Notification));
        EvaluateActivity();
    }

    internal void MemoPlayPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceMemoService.IsPlaying)
        {
            _voiceMemoService.StopPlayback();
        }
        else if (!string.IsNullOrEmpty(_voiceMemoService.LastSavedFilePath) && File.Exists(_voiceMemoService.LastSavedFilePath))
        {
            _voiceMemoService.PlayMemo(_voiceMemoService.LastSavedFilePath);
        }
    }

    internal void PlayMemoItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;

        if (_voiceMemoService.IsPlaying && string.Equals(_voiceMemoService.PlayingFilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _voiceMemoService.StopPlayback();
        }
        else if (File.Exists(path))
        {
            _voiceMemoService.PlayMemo(path);
        }
    }

    internal void CopyMemoItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || !File.Exists(path)) return;

        try
        {
            var files = new System.Collections.Specialized.StringCollection { path };
            Clipboard.SetFileDropList(files);
            _machine.PushToast(new ToastContent(
                "\uE8C8",
                "Voice Memo Copied",
                Path.GetFileName(path),
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "voicememo",
                Priority: ToastPriority.Feedback));
        }
        catch { }
    }

    internal void SendMemoItemToShelf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || !File.Exists(path)) return;

        _shelfService.AddPaths(new[] { path });
        _machine.PushToast(new ToastContent(
            "\uE8B7",
            "Added to Shelf",
            Path.GetFileName(path),
            ThemeBrushes.GreenColor,
            TimeSpan.FromSeconds(2.2),
            TargetView: "shelf",
            Priority: ToastPriority.Feedback));
    }

    internal void DeleteMemoItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;

        string name = Path.GetFileName(path);
        if (_voiceMemoService.DeleteMemo(path))
        {
            InitializeVoiceMemoUI();
            _machine.PushToast(new ToastContent(
                "\uE74D",
                "Voice Memo Deleted",
                name,
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.0),
                TargetView: "voicememo",
                Priority: ToastPriority.Feedback));
        }
    }

    private void OnMemoPlaybackStateChanged(bool isPlaying)
    {
        MemoPlayGlyph.Text = isPlaying ? "\uE769" : "\uE768";
        MemoPlayLabel.Text = isPlaying ? "Pause" : "Play";
    }

    internal void SendMemoToShelf_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_voiceMemoService.LastSavedFilePath) && File.Exists(_voiceMemoService.LastSavedFilePath))
        {
            _shelfService.AddPaths(new[] { _voiceMemoService.LastSavedFilePath });
            _machine.PushToast(new ToastContent(
                "\uE8B7",
                "Added to Shelf",
                Path.GetFileName(_voiceMemoService.LastSavedFilePath),
                ThemeBrushes.GreenColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: "shelf",
                Priority: ToastPriority.Feedback));
        }
    }

    internal void OpenMemosFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DynamicIsland_Memos");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"[VoiceMemo] OpenMemosFolder failed: {ex.Message}");
        }
    }

}
