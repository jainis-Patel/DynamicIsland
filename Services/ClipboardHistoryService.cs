using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

public enum ClipboardItemType
{
    Text,
    Color,
    Url,
    Image,
    File
}

public sealed class ClipboardItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ClipboardItemType Type { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string? RawText { get; init; }
    public Color? SwatchColor { get; init; }
    public ImageSource? ImagePreview { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string TimeAgo => CreatedAt.ToString("HH:mm");
}

/// <summary>
/// Listens to real-time Windows clipboard updates using Win32 AddClipboardFormatListener.
/// Honors Windows and password-manager privacy exclusion markers (e.g. 1Password, Bitwarden, KeePass)
/// so sensitive secrets are never captured or persisted to disk.
/// Categorizes clips into Text, Colors (with swatches), URLs, and Images (with bounded memory footprint),
/// and atomically persists text/URL/color clips across restarts.
/// </summary>
internal sealed class ClipboardHistoryService : IDisposable
{
    private sealed record PersistedClipDto(
        ClipboardItemType Type,
        string Title,
        string Subtitle,
        string? RawText,
        string? SwatchHex,
        DateTime CreatedAt);

    private static readonly string PersistencePath = AppPaths.ClipboardFile;
    private static readonly object SaveFileLock = new();

    /// <summary>
    /// Standard clipboard format names used by Windows 10/11 and password managers (1Password, Bitwarden,
    /// KeePass, LastPass, Windows Security) to mark sensitive/ephemeral clipboard data.
    /// </summary>
    public const string FormatExcludeFromMonitoring = "ExcludeClipboardContentFromMonitorProcessing";
    public const string FormatCanIncludeInHistory = "CanIncludeInClipboardHistory";
    public const string FormatClipboardViewerIgnore = "Clipboard Viewer Ignore";

    private readonly Dispatcher _ui;
    private IntPtr _hwnd;
    private bool _listening;
    private string? _lastText;
    private readonly List<ClipboardItem> _items = new();
    private const int MaxHistory = 15;
    public const int MaxImagePreviewWidth = 640;

    public IReadOnlyList<ClipboardItem> Items
    {
        get
        {
            lock (_items) return _items.ToList();
        }
    }

    public event Action? Changed;

    private static readonly Regex HexColorRegex = new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"^(https?:\/\/)?([\da-z\.-]+)\.([a-z\.]{2,6})([\/\w \.-]*)*\/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ClipboardHistoryService(Dispatcher ui)
    {
        _ui = ui;
        LoadPersistedHistory();
    }

    /// <summary>
    /// Inspects an <see cref="IDataObject"/> for Windows / password-manager privacy markers.
    /// Returns <see langword="true"/> when the clipboard entry must NOT be captured or persisted.
    /// </summary>
    public static bool ShouldExcludeClipboardData(IDataObject? dataObject)
    {
        if (dataObject is null) return true;

        try
        {
            // 1. Explicit exclusion marker set by password managers and security tools
            if (dataObject.GetDataPresent(FormatExcludeFromMonitoring))
            {
                var raw = TryGetFormatData(dataObject, FormatExcludeFromMonitoring);
                if (raw is null || !IsExplicitZeroOrFalse(raw))
                {
                    return true;
                }
            }

            if (dataObject.GetDataPresent(FormatClipboardViewerIgnore))
            {
                return true;
            }

            // 2. Windows 10/11 CanIncludeInClipboardHistory (DWORD == 0 means exclude from history)
            if (dataObject.GetDataPresent(FormatCanIncludeInHistory))
            {
                var raw = TryGetFormatData(dataObject, FormatCanIncludeInHistory);
                if (raw is not null && IsExplicitZeroOrFalse(raw))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ClipboardHistory] Privacy check error: {ex.Message}");
        }

        return false;
    }

    private static object? TryGetFormatData(IDataObject dataObject, string format)
    {
        try
        {
            return dataObject.GetData(format);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExplicitZeroOrFalse(object value) => value switch
    {
        bool b => !b,
        int i => i == 0,
        uint u => u == 0,
        byte by => by == 0,
        byte[] bytes => bytes.Length > 0 && bytes.All(b => b == 0),
        MemoryStream ms => IsStreamAllZero(ms),
        _ => false
    };

    private static bool IsStreamAllZero(MemoryStream ms)
    {
        if (ms.Length == 0) return false;
        long pos = ms.Position;
        try
        {
            ms.Position = 0;
            for (int i = 0; i < Math.Min(ms.Length, 4); i++)
            {
                if (ms.ReadByte() != 0) return false;
            }
            return true;
        }
        finally
        {
            ms.Position = pos;
        }
    }

    /// <summary>
    /// Downscales large clipboard bitmaps (e.g. 4K screenshots) to a bounded thumbnail width
    /// and materializes a detached, frozen <see cref="BitmapSource"/> so the original multi-megabyte
    /// buffer is immediately eligible for GC.
    /// </summary>
    public static BitmapSource BoundImageSize(BitmapSource source, int maxWidth = MaxImagePreviewWidth)
    {
        if (source.PixelWidth <= maxWidth || maxWidth <= 0)
        {
            if (source.CanFreeze && !source.IsFrozen) source.Freeze();
            return source;
        }

        double scale = (double)maxWidth / source.PixelWidth;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var formatted = new FormatConvertedBitmap(transformed, PixelFormats.Bgra32, null, 0);

        int w = formatted.PixelWidth;
        int h = formatted.PixelHeight;
        int stride = w * 4;
        byte[] pixels = new byte[h * stride];
        formatted.CopyPixels(pixels, stride, 0);

        var detached = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        detached.Freeze();
        return detached;
    }

    public void Attach(IntPtr hwnd)
    {
        if (_listening || hwnd == IntPtr.Zero) return;
        _hwnd = hwnd;
        if (AddClipboardFormatListener(_hwnd))
        {
            _listening = true;
        }
    }

    public void HandleClipboardUpdate()
    {
        _ui.InvokeAsync(ProcessClipboardContent, DispatcherPriority.Background);
    }

    private void ProcessClipboardContent()
    {
        try
        {
            IDataObject? dataObj = Clipboard.GetDataObject();
            if (ShouldExcludeClipboardData(dataObj))
            {
                App.Log("[ClipboardHistory] Ignored sensitive clipboard update (privacy exclusion flag present).");
                return;
            }

            if (!Clipboard.ContainsText() && !Clipboard.ContainsImage() && !Clipboard.ContainsFileDropList())
                return;

            ClipboardItem? newItem = null;

            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    int origW = img.PixelWidth;
                    int origH = img.PixelHeight;
                    var bounded = BoundImageSize(img);
                    newItem = new ClipboardItem
                    {
                        Type = ClipboardItemType.Image,
                        Title = $"Image ({origW} × {origH})",
                        Subtitle = "Screenshot / Image",
                        ImagePreview = bounded
                    };
                }
            }
            else if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                if (string.IsNullOrWhiteSpace(text) || text == _lastText) return;
                _lastText = text;

                // 1. Check if it's a Hex Color Code
                var colorMatch = HexColorRegex.Match(text);
                if (colorMatch.Success)
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(text);
                        newItem = new ClipboardItem
                        {
                            Type = ClipboardItemType.Color,
                            Title = text.ToUpperInvariant(),
                            Subtitle = $"R:{color.R} G:{color.G} B:{color.B}",
                            RawText = text,
                            SwatchColor = color
                        };
                    }
                    catch
                    {
                        // Not a valid color string; continue to URL/Text checks
                    }
                }

                // 2. Check if it's a Web URL
                if (newItem == null && (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                       text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                       UrlRegex.IsMatch(text)))
                {
                    string domain = text;
                    try
                    {
                        var uri = new Uri(text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : "https://" + text);
                        domain = uri.Host;
                    }
                    catch
                    {
                        // Keep raw text as title fallback
                    }

                    newItem = new ClipboardItem
                    {
                        Type = ClipboardItemType.Url,
                        Title = domain,
                        Subtitle = text,
                        RawText = text
                    };
                }

                // 3. Fallback: General Text snippet
                if (newItem == null)
                {
                    string singleLine = text.Replace("\r", " ").Replace("\n", " ");
                    if (singleLine.Length > 40) singleLine = singleLine[..37] + "...";

                    newItem = new ClipboardItem
                    {
                        Type = ClipboardItemType.Text,
                        Title = singleLine,
                        Subtitle = $"{text.Length} character{(text.Length == 1 ? "" : "s")}",
                        RawText = text
                    };
                }
            }

            if (newItem != null)
            {
                lock (_items)
                {
                    if (_items.Count > 0 && _items[0].RawText != null && _items[0].RawText == newItem.RawText)
                        return;

                    _items.Insert(0, newItem);
                    if (_items.Count > MaxHistory)
                        _items.RemoveAt(_items.Count - 1);
                }
                SavePersistedHistoryAsync();
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ClipboardHistory] Error reading clipboard: {ex.Message}");
        }
    }

    public void CopyItem(ClipboardItem item)
    {
        try
        {
            if (item.ImagePreview is BitmapSource bmp)
            {
                Clipboard.SetImage(bmp);
            }
            else if (!string.IsNullOrEmpty(item.RawText))
            {
                Clipboard.SetText(item.RawText);
                _lastText = item.RawText;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ClipboardHistory] CopyItem error: {ex.Message}");
        }
    }

    public void Remove(string id)
    {
        bool removed;
        lock (_items)
        {
            removed = _items.RemoveAll(i => i.Id == id) > 0;
        }
        if (removed)
        {
            SavePersistedHistoryAsync();
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        lock (_items) _items.Clear();
        SavePersistedHistoryAsync();
        Changed?.Invoke();
    }

    private void LoadPersistedHistory()
    {
        var dtos = AtomicJsonStore.Load<List<PersistedClipDto>?>(PersistencePath, () => null);
        if (dtos == null) return;

        lock (_items)
        {
            foreach (var dto in dtos.Take(MaxHistory))
            {
                if (string.IsNullOrEmpty(dto.RawText)) continue;
                Color? swatch = null;
                if (!string.IsNullOrEmpty(dto.SwatchHex))
                {
                    try { swatch = (Color)ColorConverter.ConvertFromString(dto.SwatchHex); }
                    catch { }
                }

                _items.Add(new ClipboardItem
                {
                    Type = dto.Type,
                    Title = dto.Title,
                    Subtitle = dto.Subtitle,
                    RawText = dto.RawText,
                    SwatchColor = swatch,
                    CreatedAt = dto.CreatedAt
                });
            }

            if (_items.Count > 0)
            {
                _lastText = _items[0].RawText;
            }
        }
    }

    private void SavePersistedHistoryAsync()
    {
        List<PersistedClipDto> snapshot;
        lock (_items)
        {
            snapshot = _items
                .Where(i => i.Type != ClipboardItemType.Image && !string.IsNullOrEmpty(i.RawText))
                .Select(i => new PersistedClipDto(
                    i.Type,
                    i.Title,
                    i.Subtitle,
                    i.RawText,
                    i.SwatchColor?.ToString(),
                    i.CreatedAt))
                .ToList();
        }

        _ = Task.Run(() => AtomicJsonStore.Save(PersistencePath, snapshot, SaveFileLock));
    }

    public void Dispose()
    {
        if (_listening && _hwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwnd);
            _listening = false;
        }
    }
}
