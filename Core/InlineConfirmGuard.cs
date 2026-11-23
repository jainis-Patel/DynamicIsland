using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DynamicIsland.Core;

/// <summary>
/// Coordinates consistent, non-blocking inline 2-step confirmation for destructive UI actions.
/// First click arms the button with an amber confirmation label for <see cref="ConfirmWindow"/> (default 3.2s)
/// before automatically reverting; a second click within the window (or Shift+Click bypass) executes.
/// </summary>
internal sealed class InlineConfirmGuard
{
    private sealed class ArmedEntry
    {
        public DateTimeOffset ExpiresAtUtc { get; init; }
        public long Nonce { get; init; }
        public TextBlock? Label { get; init; }
        public string? OriginalText { get; init; }
        public object? OriginalLocalForeground { get; init; }
    }

    private readonly Dictionary<string, ArmedEntry> _armed = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private long _nextNonce;

    public TimeSpan ConfirmWindow { get; }

    public InlineConfirmGuard(TimeSpan? confirmWindow = null)
    {
        ConfirmWindow = confirmWindow ?? TimeSpan.FromSeconds(3.2);
    }

    /// <summary>
    /// Pure state transition helper for deterministic verification without WPF UI dependencies.
    /// Returns true if confirmed (second click within window or bypass=true); false if newly armed.
    /// </summary>
    public bool TryConfirmState(string key, DateTimeOffset nowUtc, bool bypass = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_lock)
        {
            if (bypass)
            {
                _armed.Remove(key);
                return true;
            }

            if (_armed.TryGetValue(key, out var existing) && nowUtc <= existing.ExpiresAtUtc)
            {
                _armed.Remove(key);
                return true;
            }

            _armed[key] = new ArmedEntry
            {
                ExpiresAtUtc = nowUtc + ConfirmWindow,
                Nonce = ++_nextNonce
            };
            return false;
        }
    }

    /// <summary>
    /// Arms or confirms the destructive action identified by <paramref name="key"/>, updating the target
    /// button label to <paramref name="confirmLabel"/> in amber on first click and reverting automatically.
    /// </summary>
    public bool TryConfirm(
        string key,
        object? sender = null,
        string confirmLabel = "Confirm?",
        TextBlock? explicitLabel = null,
        bool bypass = false,
        Dispatcher? dispatcher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ArmedEntry? toRestore = null;
        ArmedEntry? expiredToRestore = null;
        long armedNonce = 0;
        bool confirmed;

        lock (_lock)
        {
            if (bypass)
            {
                if (_armed.Remove(key, out var prev))
                {
                    toRestore = prev;
                }
                confirmed = true;
            }
            else if (_armed.TryGetValue(key, out var existing) && now <= existing.ExpiresAtUtc)
            {
                _armed.Remove(key);
                toRestore = existing;
                confirmed = true;
            }
            else
            {
                if (existing != null)
                {
                    _armed.Remove(key);
                    expiredToRestore = existing;
                }
                confirmed = false;
            }
        }

        if (expiredToRestore != null)
        {
            RestoreLabel(expiredToRestore);
        }

        if (confirmed)
        {
            if (toRestore != null)
            {
                RestoreLabel(toRestore);
            }
            return true;
        }

        TextBlock? label = explicitLabel ?? FindLabelTextBlock(sender);
        string? origText = label?.Text;
        object? origLocalFg = label?.ReadLocalValue(TextBlock.ForegroundProperty);

        lock (_lock)
        {
            armedNonce = ++_nextNonce;
            _armed[key] = new ArmedEntry
            {
                ExpiresAtUtc = now + ConfirmWindow,
                Nonce = armedNonce,
                Label = label,
                OriginalText = origText,
                OriginalLocalForeground = origLocalFg
            };
        }

        if (label != null)
        {
            label.Text = confirmLabel;
            label.Foreground = ThemeBrushes.Amber;
        }

        Dispatcher? targetDispatcher = dispatcher ?? label?.Dispatcher ?? (sender as DependencyObject)?.Dispatcher;
        _ = ScheduleAutoRevertAsync(key, armedNonce, targetDispatcher);
        return false;
    }

    public bool IsArmed(string key)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            return _armed.TryGetValue(key, out var entry) && now <= entry.ExpiresAtUtc;
        }
    }

    public void Disarm(string key)
    {
        ArmedEntry? entry;
        lock (_lock)
        {
            _armed.Remove(key, out entry);
        }
        if (entry != null)
        {
            RestoreLabel(entry);
        }
    }

    public void DisarmAll()
    {
        ArmedEntry[] entries;
        lock (_lock)
        {
            entries = _armed.Values.ToArray();
            _armed.Clear();
        }
        foreach (var entry in entries)
        {
            RestoreLabel(entry);
        }
    }

    private async Task ScheduleAutoRevertAsync(string key, long nonce, Dispatcher? dispatcher)
    {
        try
        {
            await Task.Delay(ConfirmWindow + TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
            ArmedEntry? expired = null;
            lock (_lock)
            {
                if (_armed.TryGetValue(key, out var entry) && entry.Nonce == nonce)
                {
                    _armed.Remove(key);
                    expired = entry;
                }
            }

            if (expired != null)
            {
                if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    await dispatcher.InvokeAsync(() => RestoreLabel(expired));
                }
                else
                {
                    RestoreLabel(expired);
                }
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("InlineConfirmGuard", ex);
        }
    }

    private static void RestoreLabel(ArmedEntry entry)
    {
        if (entry.Label == null) return;
        try
        {
            if (entry.OriginalText != null)
            {
                entry.Label.Text = entry.OriginalText;
            }
            if (entry.OriginalLocalForeground == DependencyProperty.UnsetValue || entry.OriginalLocalForeground == null)
            {
                entry.Label.ClearValue(TextBlock.ForegroundProperty);
            }
            else
            {
                entry.Label.SetValue(TextBlock.ForegroundProperty, entry.OriginalLocalForeground);
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("InlineConfirmGuard", ex);
        }
    }

    private static TextBlock? FindLabelTextBlock(object? element)
    {
        if (element is TextBlock tb)
        {
            return tb;
        }

        if (element is ContentControl cc)
        {
            return FindLabelInObject(cc.Content);
        }

        if (element is Decorator dec)
        {
            return FindLabelInObject(dec.Child);
        }

        if (element is Panel panel)
        {
            return FindLabelInPanel(panel);
        }

        return null;
    }

    private static TextBlock? FindLabelInObject(object? obj)
    {
        if (obj is TextBlock tb) return tb;
        if (obj is Panel panel) return FindLabelInPanel(panel);
        if (obj is Decorator dec) return FindLabelInObject(dec.Child);
        if (obj is ContentControl cc) return FindLabelInObject(cc.Content);
        return null;
    }

    private static TextBlock? FindLabelInPanel(Panel panel)
    {
        TextBlock? fallback = null;
        foreach (object? child in panel.Children)
        {
            var candidate = FindLabelInObject(child);
            if (candidate == null) continue;
            fallback = candidate;
            string t = candidate.Text;
            if (!string.IsNullOrWhiteSpace(t) && !(t.Length == 1 && t[0] >= '\uE000'))
            {
                return candidate;
            }
        }
        return fallback;
    }
}
