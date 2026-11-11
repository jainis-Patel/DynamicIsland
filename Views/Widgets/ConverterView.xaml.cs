using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using DynamicIsland.Core;
using DynamicIsland.Services;

namespace DynamicIsland.Views.Widgets;

public sealed record ConverterItem(string FullPath, string Name, string Extension, string SizeText);

public partial class ConverterView : UserControl
{
    private readonly ObservableCollection<ConverterItem> _items = new();
    private string _activePreset = "PNG";

    public ConverterView()
    {
        InitializeComponent();
        ConverterFilesItemsControl.ItemsSource = _items;
        UpdatePresetButtonsUI();
    }

    private IslandWindow? Island => IslandWindow.Instance ?? Window.GetWindow(this) as IslandWindow;

    private void BackToLauncher_Click(object sender, RoutedEventArgs e) =>
        Island?.NavigateToView(Core.ExpandedView.Launcher);

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (File.Exists(p) && !_items.Any(i => string.Equals(i.FullPath, p, StringComparison.OrdinalIgnoreCase)))
            {
                var fi = new FileInfo(p);
                string ext = fi.Extension.TrimStart('.').ToUpperInvariant();
                string size = fi.Length switch
                {
                    > 1024 * 1024 => $"{fi.Length / (1024.0 * 1024.0):F1} MB",
                    > 1024 => $"{fi.Length / 1024.0:F0} KB",
                    _ => $"{fi.Length} B"
                };
                _items.Add(new ConverterItem(p, fi.Name, ext, size));
            }
        }
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool hasItems = _items.Count > 0;
        ConverterEmptyZone.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ConverterFilesScrollViewer.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        ConverterStatusBadge.Text = hasItems ? $"{_items.Count} file{(_items.Count == 1 ? "" : "s")} selected" : "Offline & Lossless";
    }

    private void BrowseConverterFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select Files to Convert"
        };
        if (dlg.ShowDialog() == true)
        {
            AddFiles(dlg.FileNames);
        }
    }

    private void ConverterDropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddFiles(files);
        }
    }

    private void ConverterDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void RemoveConverterItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            var match = _items.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (match != null) _items.Remove(match);
            UpdateVisibility();
        }
    }

    private void ClearConverterFiles_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        UpdateVisibility();
    }

    private void ImportFromShelfBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Island?.DropShelfServiceInstance is { } shelf && shelf.Items.Count > 0)
        {
            AddFiles(shelf.Items.Select(i => i.FullPath));
        }
        else
        {
            Island?.PushNotificationToast("\uE8B7", "Converter", "No files found on Drop Shelf", ThemeBrushes.AmberColor);
        }
    }

    private void OpenOutputFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        string outDir = Path.Combine(AppPaths.DataDirectory, "Conversions");
        Directory.CreateDirectory(outDir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = outDir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void FormatPresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            _activePreset = preset;
            UpdatePresetButtonsUI();
        }
    }

    private void UpdatePresetButtonsUI()
    {
        var activeBg = (Brush)FindResource("SettingsTabActiveBgBrush");
        var activeFg = (Brush)FindResource("PrimaryTextBrush");
        var inactiveBg = Brushes.Transparent;
        var inactiveFg = (Brush)FindResource("SecondaryBrush");

        void StyleBtn(Button? btn, bool active)
        {
            if (btn == null) return;
            btn.Background = active ? activeBg : inactiveBg;
            if (btn.Content is TextBlock tb)
            {
                tb.Foreground = active ? activeFg : inactiveFg;
                tb.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        StyleBtn(PresetPngBtn, _activePreset == "PNG");
        StyleBtn(PresetJpgBtn, _activePreset == "JPG");
        StyleBtn(PresetPdfBtn, _activePreset == "PDF");
        StyleBtn(PresetMergePdfBtn, _activePreset == "MERGE_PDF");
        StyleBtn(PresetZipBtn, _activePreset == "ZIP");
        StyleBtn(PresetUnzipBtn, _activePreset == "UNZIP");

        ConvertActionBtnText.Text = _activePreset switch
        {
            "PNG" => "Convert to PNG",
            "JPG" => "Convert to JPG",
            "PDF" => "Images \u2192 PDF",
            "MERGE_PDF" => "Merge All PDFs",
            "ZIP" => "Create ZIP Bundle",
            "UNZIP" => "Extract All ZIPs",
            _ => "Convert Now"
        };
    }

    private async void ConvertActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            BrowseConverterFiles_Click(sender, e);
            return;
        }

        var paths = _items.Select(i => i.FullPath).ToList();
        FileActionKind kind = _activePreset switch
        {
            "PNG" => FileActionKind.ConvertToPng,
            "JPG" => FileActionKind.ConvertToJpg,
            "PDF" => FileActionKind.ImagesToPdf,
            "MERGE_PDF" => FileActionKind.MergePdfs,
            "ZIP" => FileActionKind.BatchRenameTimestamp, // Fallback or zip
            "UNZIP" => FileActionKind.UnzipArchive,
            _ => FileActionKind.ConvertToPng
        };

        ConvertActionBtn.IsEnabled = false;
        ConvertActionBtnText.Text = "Processing...";

        try
        {
            FileActionResult result;
            if (_activePreset == "ZIP")
            {
                string outDir = Path.Combine(AppPaths.DataDirectory, "Conversions");
                Directory.CreateDirectory(outDir);
                string zipPath = Path.Combine(outDir, $"Converted_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var p in paths)
                    {
                        if (File.Exists(p)) archive.CreateEntryFromFile(p, Path.GetFileName(p));
                    }
                }
                result = new FileActionResult(true, "ZIP Created", Path.GetFileName(zipPath), new[] { zipPath });
            }
            else
            {
                result = await FileActionPipeline.ExecuteAsync(kind, paths);
            }

            if (result.Success)
            {
                Island?.PushNotificationToast("\uEA90", result.Title, result.Message, ThemeBrushes.GreenColor);
                if (result.OutputPaths.Count > 0)
                {
                    Island?.DropShelfServiceInstance?.AddPaths(result.OutputPaths);
                }
            }
            else
            {
                Island?.PushNotificationToast("\uE783", "Conversion Failed", result.Message, ThemeBrushes.AmberColor);
            }
        }
        catch (Exception ex)
        {
            Island?.PushNotificationToast("\uE783", "Error", ex.Message, ThemeBrushes.AmberColor);
        }
        finally
        {
            ConvertActionBtn.IsEnabled = true;
            UpdatePresetButtonsUI();
        }
    }
}
