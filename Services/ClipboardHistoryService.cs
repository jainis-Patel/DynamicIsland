using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
    File,
    Code
}

public enum ClipboardCategoryFilter
{
    All,
    Pinned,
    Links,
    Images,
    Colors,
    Code
}

public sealed class ClipboardItem : INotifyPropertyChanged
{
    private bool _isPinned;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ClipboardItemType Type { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string? RawText { get; init; }
    public Color? SwatchColor { get; init; }
    public ImageSource? ImagePreview { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public string TimeAgo => CreatedAt.ToString("HH:mm");

    public int LineCount { get; init; } = 1;
    public int WordCount { get; init; } = 0;
    public string? CodeLanguage { get; init; }

    public string RgbText => SwatchColor.HasValue
        ? $"rgb({SwatchColor.Value.R}, {SwatchColor.Value.G}, {SwatchColor.Value.B})"
        : "";

    public string TypeTag => Type switch
    {
        ClipboardItemType.Code => !string.IsNullOrEmpty(CodeLanguage) ? CodeLanguage : "CODE",
        ClipboardItemType.Url => "URL",
        ClipboardItemType.Color => "COLOR",
        ClipboardItemType.Image => "IMG",
        ClipboardItemType.File => "FILE",
        _ => "TEXT"
    };

    private Brush? _cachedSwatchBrush;

    public Brush BadgeColorBrush => Type switch
    {
        ClipboardItemType.Code => ThemeBrushes.ClipBadgeCode,
        ClipboardItemType.Url => ThemeBrushes.ClipBadgeUrl,
        ClipboardItemType.Color => ThemeBrushes.ClipBadgeColor,
        ClipboardItemType.Image => ThemeBrushes.ClipBadgeImage,
        ClipboardItemType.File => ThemeBrushes.ClipBadgeFile,
        _ => ThemeBrushes.ClipBadgeText
    };

    public Brush SwatchBrush => SwatchColor.HasValue
        ? (_cachedSwatchBrush ??= ThemeBrushes.CreateFrozen(SwatchColor.Value))
        : Brushes.Transparent;

    public bool IsCode => Type == ClipboardItemType.Code;
    public bool IsUrl => Type == ClipboardItemType.Url;
    public bool IsColor => Type == ClipboardItemType.Color;
    public bool IsImage => Type == ClipboardItemType.Image;
    public bool IsText => Type == ClipboardItemType.Text;
    public bool HasImage => ImagePreview != null;
    public bool HasSwatch => SwatchColor.HasValue;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PinGlyph));
                OnPropertyChanged(nameof(PinForeground));
            }
        }
    }

    public string PinGlyph => _isPinned ? "\uE840" : "\uE718";
    public Brush PinForeground => _isPinned ? ThemeBrushes.Amber : ThemeBrushes.SecondaryText;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

/// <summary>
/// Listens to real-time Windows clipboard updates using Win32 AddClipboardFormatListener.
/// Honors Windows and password-manager privacy exclusion markers (e.g. 1Password, Bitwarden, KeePass)
/// so sensitive secrets are never captured or persisted to disk.
/// Categorizes clips into Text, Code, Colors (with swatches), URLs, and Images (with bounded memory footprint),
/// and atomically persists clips across restarts using DPAPI encryption.
/// </summary>
internal sealed class ClipboardHistoryService : IDisposable
{
    private sealed record PersistedClipDto(
        ClipboardItemType Type,
        string Title,
        string Subtitle,
        string? RawText,
        string? SwatchHex,
        DateTime CreatedAt,
        bool IsPinned = false,
        string? CodeLanguage = null);

    private const string DpapiPrefix = "dpapi:";
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("DynamicIsland.ClipboardHistory.v1");

    private readonly string _persistencePath;
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
    private readonly List<ClipboardItem> _pasteQueue = new();
    private const int MaxHistory = 20;
    public const int MaxImagePreviewWidth = 300;

    public int PasteQueueCount
    {
        get { lock (_pasteQueue) return _pasteQueue.Count; }
    }

    public IReadOnlyList<ClipboardItem> PasteQueue
    {
        get { lock (_pasteQueue) return _pasteQueue.ToList(); }
    }

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

    public ClipboardHistoryService(Dispatcher ui, string? persistencePath = null)
    {
        _ui = ui;
        _persistencePath = persistencePath ?? AppPaths.ClipboardFile;
        LoadPersistedHistory();
    }

    /// <summary>
    /// Filters items according to search query and category filter.
    /// </summary>
    public IReadOnlyList<ClipboardItem> GetFilteredItems(string? searchQuery, ClipboardCategoryFilter filter)
    {
        List<ClipboardItem> snapshot;
        lock (_items)
        {
            snapshot = _items.ToList();
        }

        IEnumerable<ClipboardItem> query = snapshot;

        query = filter switch
        {
            ClipboardCategoryFilter.Pinned => query.Where(i => i.IsPinned),
            ClipboardCategoryFilter.Links => query.Where(i => i.Type == ClipboardItemType.Url),
            ClipboardCategoryFilter.Images => query.Where(i => i.Type == ClipboardItemType.Image),
            ClipboardCategoryFilter.Colors => query.Where(i => i.Type == ClipboardItemType.Color),
            ClipboardCategoryFilter.Code => query.Where(i => i.Type == ClipboardItemType.Code),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim().ToLowerInvariant();
            query = query.Where(i =>
                (i.Title != null && i.Title.ToLowerInvariant().Contains(q)) ||
                (i.Subtitle != null && i.Subtitle.ToLowerInvariant().Contains(q)) ||
                (i.RawText != null && i.RawText.ToLowerInvariant().Contains(q)) ||
                (i.CodeLanguage != null && i.CodeLanguage.ToLowerInvariant().Contains(q)));
        }

        return query.ToList();
    }

    /// <summary>
    /// Heuristic analyzer that detects if text is a code snippet and returns a language tag.
    /// </summary>
    public static (bool IsCode, string Language) TryDetectCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, "");

        string trimmed = text.Trim();

        // 1. JSON check
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            if (trimmed.Contains("\":") || trimmed.Contains("\": ") || trimmed.Contains("null") || trimmed.Contains("true") || trimmed.Contains("false"))
            {
                return (true, "JSON");
            }
        }

        // 2. HTML / XML check
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>') &&
            (trimmed.Contains("</") || trimmed.Contains("/>") || trimmed.Contains("<div") || trimmed.Contains("<span") || trimmed.Contains("<p") || trimmed.Contains("<button") || trimmed.Contains("<Grid") || trimmed.Contains("<Border")))
        {
            return (true, "HTML");
        }

        // 3. SQL check
        var upper = text.ToUpperInvariant();
        if (upper.Contains("SELECT ") && (upper.Contains(" FROM ") || upper.Contains(" WHERE ") || upper.Contains(" JOIN ")))
        {
            return (true, "SQL");
        }
        if (upper.Contains("INSERT INTO ") || (upper.Contains("UPDATE ") && upper.Contains(" SET ")) || upper.Contains("CREATE TABLE "))
        {
            return (true, "SQL");
        }

        // 4. C# / Java / TypeScript / Python checks
        if (text.Contains("using System") || text.Contains("namespace ") || text.Contains("public class ") || text.Contains("Console.WriteLine") || text.Contains("Task<") || text.Contains("private readonly") || text.Contains("internal sealed"))
        {
            return (true, "C#");
        }

        if (text.Contains("import ") && (text.Contains("from '") || text.Contains("from \"")))
        {
            return (true, "JS");
        }
        if (text.Contains("export default") || text.Contains("const ") || text.Contains("let ") || text.Contains("console.log") || text.Contains("=>"))
        {
            return (true, "JS");
        }

        if (text.Contains("def ") && text.Contains(":") && (text.Contains("self") || text.Contains("print(")))
        {
            return (true, "PY");
        }

        // General code syntax token heuristics
        int codeScore = 0;
        if (text.Contains(" {") || text.Contains("{\n") || text.Contains("{\r\n")) codeScore++;
        if (text.Contains(" }") || text.Contains("\n}") || text.Contains("\r\n}")) codeScore++;
        if (text.Contains("();") || text.Contains(");\n") || text.Contains(");\r\n")) codeScore++;
        if (text.Contains(" = ") && (text.Contains(";") || text.Contains("=="))) codeScore++;
        if (text.Contains("++") || text.Contains("--") || text.Contains("!=") || text.Contains("===")) codeScore++;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines.Length >= 2)
        {
            int indentedLines = lines.Count(l => l.StartsWith("  ") || l.StartsWith("\t"));
            if (indentedLines >= 2) codeScore += 2;
        }

        if (codeScore >= 3)
        {
            return (true, "CODE");
        }

        return (false, "");
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

    public static (BitmapSource? Image, int OrigW, int OrigH) TryExtractBoundedClipboardImage(IDataObject? dataObj, int targetWidth = MaxImagePreviewWidth)
    {
        if (dataObj is null) return (null, 0, 0);

        try
        {
            // 1. Prefer compressed PNG stream if present (WIC decodes directly at target width, avoiding transient 33+ MB 4K LOH allocations)
            if (dataObj.GetDataPresent("PNG"))
            {
                var raw = dataObj.GetData("PNG");
                if (raw is MemoryStream ms)
                {
                    int origW = targetWidth;
                    int origH = targetWidth;
                    try
                    {
                        ms.Position = 0;
                        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        if (decoder.Frames.Count > 0)
                        {
                            origW = decoder.Frames[0].PixelWidth;
                            origH = decoder.Frames[0].PixelHeight;
                        }
                    }
                    catch { }

                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.DecodePixelWidth = targetWidth;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (bmp, origW, origH);
                }
            }

            // 2. Fallback to standard Clipboard.GetImage() and downscale to targetWidth immediately
            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    int origW = img.PixelWidth;
                    int origH = img.PixelHeight;
                    var bounded = BoundImageSize(img, targetWidth);
                    return (bounded, origW, origH);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ClipboardHistory] Error extracting image: {ex.Message}");
        }

        return (null, 0, 0);
    }

    private void ProcessClipboardContent()
    {
        int retries = 3;
        while (retries > 0)
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

                if (dataObj != null && (dataObj.GetDataPresent("PNG") || Clipboard.ContainsImage()))
                {
                    var (boundedImg, origW, origH) = TryExtractBoundedClipboardImage(dataObj, MaxImagePreviewWidth);
                    if (boundedImg != null)
                    {
                        newItem = new ClipboardItem
                        {
                            Type = ClipboardItemType.Image,
                            Title = $"Image ({origW} × {origH})",
                            Subtitle = "Screenshot / Image",
                            ImagePreview = boundedImg
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

                    // 3. Check if it's Code Snippet
                    if (newItem == null)
                    {
                        var (isCode, lang) = TryDetectCode(text);
                        if (isCode)
                        {
                            int lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                            string firstLine = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
                            if (firstLine.Length > 40) firstLine = firstLine[..37] + "...";

                            newItem = new ClipboardItem
                            {
                                Type = ClipboardItemType.Code,
                                Title = firstLine,
                                Subtitle = $"{lines} line{(lines == 1 ? "" : "s")} · {lang}",
                                RawText = text,
                                LineCount = lines,
                                CodeLanguage = lang
                            };
                        }
                    }

                    // 4. Fallback: General Text snippet
                    if (newItem == null)
                    {
                        int lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                        int chars = text.Length;
                        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                        string singleLine = text.Replace("\r", " ").Replace("\n", " ");
                        if (singleLine.Length > 40) singleLine = singleLine[..37] + "...";

                        newItem = new ClipboardItem
                        {
                            Type = ClipboardItemType.Text,
                            Title = singleLine,
                            Subtitle = $"{chars} char{(chars == 1 ? "" : "s")} · {words} word{(words == 1 ? "" : "s")}",
                            RawText = text,
                            LineCount = lines,
                            WordCount = words
                        };
                    }
                }

                if (newItem != null)
                {
                    InsertClipItem(newItem);
                }
                return;
            }
            catch (COMException) when (--retries > 0)
            {
                Thread.Sleep(25);
            }
            catch (Exception ex)
            {
                App.Log($"[ClipboardHistory] Error reading clipboard: {ex.Message}");
                return;
            }
        }
    }

    internal void InsertClipItem(ClipboardItem newItem)
    {
        lock (_items)
        {
            if (_items.Any(i => i.RawText != null && i.RawText == newItem.RawText))
                return;

            // Insert right after any pinned items (or at index 0 if the new item itself is pinned)
            int insertIndex = newItem.IsPinned ? 0 : _items.TakeWhile(i => i.IsPinned).Count();
            _items.Insert(insertIndex, newItem);

            while (_items.Count > MaxHistory)
            {
                int evictIdx = _items.FindLastIndex(i => !i.IsPinned);
                if (evictIdx >= 0)
                    _items.RemoveAt(evictIdx);
                else
                    break;
            }
        }

        SavePersistedHistoryAsync();
        Changed?.Invoke();
    }

    public bool TogglePin(string id)
    {
        bool pinned = false;
        bool found = false;
        lock (_items)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                item.IsPinned = !item.IsPinned;
                pinned = item.IsPinned;
                found = true;

                // Keep pinned items grouped at the front in chronological order
                var ordered = _items
                    .OrderByDescending(i => i.IsPinned)
                    .ThenByDescending(i => i.CreatedAt)
                    .ToList();
                _items.Clear();
                _items.AddRange(ordered);
            }
        }

        if (found)
        {
            SavePersistedHistoryAsync();
            Changed?.Invoke();
        }
        return pinned;
    }

    /// <summary>
    /// Encrypts a clipboard text payload using Windows DPAPI (<see cref="DataProtectionScope.CurrentUser"/>)
    /// so persisted clipboard snippets are unreadable outside the current Windows user profile.
    /// </summary>
    public static string? ProtectText(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(plainBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(encrypted);
        }
        catch
        {
            return plainText;
        }
    }

    /// <summary>
    /// Decrypts a DPAPI-protected clipboard payload, or returns legacy plaintext strings unchanged.
    /// </summary>
    public static string? UnprotectText(string? storedText)
    {
        if (string.IsNullOrEmpty(storedText)) return storedText;
        if (!storedText.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            return storedText;
        }

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(storedText[DpapiPrefix.Length..]);
            byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    public void CopyItem(ClipboardItem item) => CopyItem(item, plainTextOnly: false);

    public void CopyItem(ClipboardItem item, bool plainTextOnly)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                if (!plainTextOnly && item.ImagePreview is BitmapSource bmp)
                {
                    Clipboard.SetImage(bmp);
                }
                else if (!string.IsNullOrEmpty(item.RawText))
                {
                    string payload = plainTextOnly ? StripFormattingToPlainText(item.RawText) : item.RawText;
                    var data = new DataObject();
                    data.SetData(DataFormats.UnicodeText, payload);
                    data.SetData(DataFormats.Text, payload);
                    Clipboard.SetDataObject(data, copy: true);
                    _lastText = payload;
                }
                return;
            }
            catch (COMException) when (--retries > 0)
            {
                Thread.Sleep(25);
            }
            catch (Exception ex)
            {
                App.Log($"[ClipboardHistory] CopyItem error: {ex.Message}");
                return;
            }
        }
    }

    public static string StripFormattingToPlainText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        // Normalize line endings and strip zero-width / control formatting characters
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    public int EnqueueForSequentialPaste(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        int count;
        lock (_pasteQueue)
        {
            if (!_pasteQueue.Any(x => x.Id == item.Id))
            {
                _pasteQueue.Add(item);
            }
            count = _pasteQueue.Count;
        }
        Changed?.Invoke();
        return count;
    }

    public ClipboardItem? DequeueNextPasteItem(bool copyToClipboard = true)
    {
        ClipboardItem? next = null;
        lock (_pasteQueue)
        {
            if (_pasteQueue.Count > 0)
            {
                next = _pasteQueue[0];
                _pasteQueue.RemoveAt(0);
            }
        }

        if (next != null)
        {
            if (copyToClipboard)
            {
                CopyItem(next, plainTextOnly: true);
            }
            Changed?.Invoke();
        }
        return next;
    }

    public void ClearPasteQueue()
    {
        lock (_pasteQueue)
        {
            if (_pasteQueue.Count == 0) return;
            _pasteQueue.Clear();
        }
        Changed?.Invoke();
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
        lock (_items)
        {
            // Preserve pinned items when clearing clipboard history
            _items.RemoveAll(i => !i.IsPinned);
        }
        SavePersistedHistoryAsync();
        Changed?.Invoke();
    }

    private void LoadPersistedHistory()
    {
        var dtos = AtomicJsonStore.Load<List<PersistedClipDto>?>(_persistencePath, () => null);
        if (dtos == null) return;

        lock (_items)
        {
            foreach (var dto in dtos.Take(MaxHistory))
            {
                string? rawText = UnprotectText(dto.RawText);
                if (string.IsNullOrEmpty(rawText)) continue;
                string title = UnprotectText(dto.Title) ?? dto.Title;
                string subtitle = UnprotectText(dto.Subtitle) ?? dto.Subtitle;

                Color? swatch = null;
                if (!string.IsNullOrEmpty(dto.SwatchHex))
                {
                    try { swatch = (Color)ColorConverter.ConvertFromString(dto.SwatchHex); }
                    catch { }
                }

                _items.Add(new ClipboardItem
                {
                    Type = dto.Type,
                    Title = title,
                    Subtitle = subtitle,
                    RawText = rawText,
                    SwatchColor = swatch,
                    CreatedAt = dto.CreatedAt,
                    IsPinned = dto.IsPinned,
                    CodeLanguage = dto.CodeLanguage,
                    LineCount = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length
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
                    ProtectText(i.Title) ?? i.Title,
                    ProtectText(i.Subtitle) ?? i.Subtitle,
                    ProtectText(i.RawText),
                    i.SwatchColor?.ToString(),
                    i.CreatedAt,
                    i.IsPinned,
                    i.CodeLanguage))
                .ToList();
        }

        _ = Task.Run(() => AtomicJsonStore.Save(_persistencePath, snapshot, SaveFileLock));
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
