using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicIsland.Services;

namespace DynamicIsland.Core;

public enum ClipboardTransformKind
{
    PlainText,
    TrimLines,
    JsonPretty,
    JsonMinify,
    Base64Encode,
    Base64Decode,
    UrlEncode,
    UrlDecode,
    UpperCase,
    LowerCase,
    TitleCase,
    CamelCase,
    SnakeCase,
    ExtractUrls,
    ExtractEmails
}

/// <summary>
/// Pure, offline clipboard text transformation engine (zero external dependencies).
/// Supports formatting cleanup, JSON pretty/minify, Base64/URL encoding, case conversions, and URL/email extraction.
/// </summary>
public static partial class ClipboardTransformer
{
    private static readonly JsonSerializerOptions PrettyJsonOpts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CompactJsonOpts = new() { WriteIndented = false };
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""'\)\]]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex EmailRegex = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex WordBoundaryRegex = new(
        @"(?<=[a-z0-9])(?=[A-Z])|[_\-\s]+",
        RegexOptions.Compiled,
        RegexTimeout);

    public static (bool Success, string Result, string Label) TryTransform(string? input, ClipboardTransformKind kind)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (false, string.Empty, "Empty text");

        return kind switch
        {
            ClipboardTransformKind.PlainText => TransformPlainText(input),
            ClipboardTransformKind.TrimLines => TransformTrimLines(input),
            ClipboardTransformKind.JsonPretty => TransformJson(input, indented: true),
            ClipboardTransformKind.JsonMinify => TransformJson(input, indented: false),
            ClipboardTransformKind.Base64Encode => (true, Convert.ToBase64String(Encoding.UTF8.GetBytes(input)), "Base64 Encoded"),
            ClipboardTransformKind.Base64Decode => TransformBase64Decode(input),
            ClipboardTransformKind.UrlEncode => (true, Uri.EscapeDataString(input), "URL Encoded"),
            ClipboardTransformKind.UrlDecode => (true, Uri.UnescapeDataString(input), "URL Decoded"),
            ClipboardTransformKind.UpperCase => (true, input.ToUpperInvariant(), "UPPERCASE"),
            ClipboardTransformKind.LowerCase => (true, input.ToLowerInvariant(), "lowercase"),
            ClipboardTransformKind.TitleCase => (true, CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower()), "Title Case"),
            ClipboardTransformKind.CamelCase => (true, ToDelimitedCase(input, isCamel: true), "camelCase"),
            ClipboardTransformKind.SnakeCase => (true, ToDelimitedCase(input, isCamel: false), "snake_case"),
            ClipboardTransformKind.ExtractUrls => ExtractMatches(input, UrlRegex, "Extracted URLs", "No URLs found"),
            ClipboardTransformKind.ExtractEmails => ExtractMatches(input, EmailRegex, "Extracted Emails", "No emails found"),
            _ => (false, string.Empty, "Unknown transform")
        };
    }

    private static (bool, string, string) TransformPlainText(string input)
    {
        string plain = ClipboardHistoryService.StripFormattingToPlainText(input);
        return string.IsNullOrEmpty(plain)
            ? (false, string.Empty, "Nothing to strip")
            : (true, plain, "Plain Text");
    }

    private static (bool, string, string) TransformTrimLines(string input)
    {
        var lines = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
        return lines.Length == 0
            ? (false, string.Empty, "No non-empty lines")
            : (true, string.Join(Environment.NewLine, lines), "Trimmed Lines");
    }

    private static (bool, string, string) TransformJson(string input, bool indented)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            string formatted = JsonSerializer.Serialize(doc.RootElement, indented ? PrettyJsonOpts : CompactJsonOpts);
            return (true, formatted, indented ? "JSON Pretty" : "JSON Minified");
        }
        catch (JsonException)
        {
            return (false, string.Empty, "Invalid JSON");
        }
    }

    private static (bool, string, string) TransformBase64Decode(string input)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(input.Trim());
            return (true, Encoding.UTF8.GetString(bytes), "Base64 Decoded");
        }
        catch (FormatException)
        {
            return (false, string.Empty, "Invalid Base64");
        }
    }

    private static string ToDelimitedCase(string input, bool isCamel)
    {
        var words = WordBoundaryRegex.Split(input.Trim())
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .Select(w => w.ToLowerInvariant())
            .ToArray();

        if (words.Length == 0) return input.Trim();
        if (!isCamel) return string.Join('_', words);

        var sb = new StringBuilder(words[0]);
        for (int i = 1; i < words.Length; i++)
        {
            var w = words[i];
            sb.Append(char.ToUpperInvariant(w[0]));
            if (w.Length > 1) sb.Append(w, 1, w.Length - 1);
        }
        return sb.ToString();
    }

    private static (bool, string, string) ExtractMatches(string input, Regex regex, string okLabel, string emptyLabel)
    {
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            foreach (Match m in regex.Matches(input))
            {
                if (m.Success && seen.Add(m.Value))
                {
                    ordered.Add(m.Value);
                }
            }
            return ordered.Count == 0
                ? (false, string.Empty, emptyLabel)
                : (true, string.Join(Environment.NewLine, ordered), $"{okLabel} ({ordered.Count})");
        }
        catch (RegexMatchTimeoutException)
        {
            return (false, string.Empty, emptyLabel);
        }
    }
}
