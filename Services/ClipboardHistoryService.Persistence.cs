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

internal sealed partial class ClipboardHistoryService
{
    /// <summary>
    /// Encrypts a clipboard text payload using Windows DPAPI (<see cref="DataProtectionScope.CurrentUser"/>)
    /// so persisted clipboard snippets are unreadable outside the current Windows user profile.
    /// Returns <see langword="null"/> if encryption fails so plaintext is never persisted.
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
        catch (Exception ex)
        {
            App.Log($"[ClipboardHistory] DPAPI ProtectText failed: {ex.Message}");
            return null;
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
        PasteQueueChanged?.Invoke();
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
            PasteQueueChanged?.Invoke();
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
        PasteQueueChanged?.Invoke();
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

                bool isSecret = DetectSecret(rawText, out string secretType);
                if (isSecret)
                {
                    title = $"{secretType} detected";
                    subtitle = MaskSecret(rawText);
                }

                _items.Add(new ClipboardItem
                {
                    Type = isSecret ? ClipboardItemType.Text : dto.Type,
                    IsSecret = isSecret,
                    SecretType = isSecret ? secretType : null,
                    Title = title,
                    Subtitle = subtitle,
                    RawText = rawText,
                    SwatchColor = swatch,
                    CreatedAt = dto.CreatedAt,
                    IsPinned = dto.IsPinned,
                    CodeLanguage = isSecret ? null : dto.CodeLanguage,
                    LineCount = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length
                });
            }

            if (_items.Count > 0)
            {
                _lastText = _items[0].RawText;
            }
        }
    }

    private List<PersistedClipDto> BuildPersistedSnapshot()
    {
        var snapshot = new List<PersistedClipDto>();
        lock (_items)
        {
            foreach (var i in _items)
            {
                if (i.Type == ClipboardItemType.Image || i.IsSecret || string.IsNullOrEmpty(i.RawText))
                    continue;

                string? protectedRaw = ProtectText(i.RawText);
                if (string.IsNullOrEmpty(protectedRaw))
                    continue; // Never persist plaintext when DPAPI fails

                string? protectedTitle = string.IsNullOrEmpty(i.Title) ? i.Title : ProtectText(i.Title);
                string? protectedSubtitle = string.IsNullOrEmpty(i.Subtitle) ? i.Subtitle : ProtectText(i.Subtitle);
                if (protectedTitle == null || protectedSubtitle == null)
                    continue;

                snapshot.Add(new PersistedClipDto(
                    i.Type,
                    protectedTitle,
                    protectedSubtitle,
                    protectedRaw,
                    i.SwatchColor?.ToString(),
                    i.CreatedAt,
                    i.IsPinned,
                    i.CodeLanguage));
            }
        }
        return snapshot;
    }

    private void SavePersistedHistoryAsync()
    {
        if (_disposed) return;
        _store.ScheduleSave();
    }

    private static readonly Regex GitHubTokenRegex = new(@"\b(gh[pousr]_[A-Za-z0-9_]{36,}|github_pat_[A-Za-z0-9_]{22,})\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex OpenAiKeyRegex = new(@"\b(sk-(?:proj-|ant-|svcacct-)?[A-Za-z0-9_-]{20,})\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex AwsKeyRegex = new(@"\b(AKIA[0-9A-Z]{16})\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex PrivateKeyRegex = new(@"-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex SlackTokenRegex = new(@"\b(xox[baprs]-[0-9A-Za-z-]{10,})\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex JwtRegex = new(@"\beyJ[A-Za-z0-9-_]{10,}\.eyJ[A-Za-z0-9-_]{10,}\.[A-Za-z0-9-_]+\b", RegexOptions.Compiled, RegexTimeout);

    public static bool DetectSecret(string text, out string secretType)
    {
        secretType = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (SafeIsMatch(GitHubTokenRegex, text))
        {
            secretType = "GitHub Token";
            return true;
        }
        if (SafeIsMatch(OpenAiKeyRegex, text))
        {
            secretType = "OpenAI API Key";
            return true;
        }
        if (SafeIsMatch(AwsKeyRegex, text))
        {
            secretType = "AWS Access Key";
            return true;
        }
        if (SafeIsMatch(PrivateKeyRegex, text))
        {
            secretType = "Private Key";
            return true;
        }
        if (SafeIsMatch(SlackTokenRegex, text))
        {
            secretType = "Slack Token";
            return true;
        }
        if (SafeIsMatch(JwtRegex, text))
        {
            secretType = "JWT Bearer Token";
            return true;
        }

        return false;
    }

    public static string MaskSecret(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string clean = text.Trim();
        if (clean.Length <= 8) return "••••••••";
        return $"{clean[..4]}••••••••{clean[^4..]}";
    }

}
