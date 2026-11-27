using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIsland.Core;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Represents a captured Windows notification stored in the in-memory Notification History ring buffer.
/// </summary>
public sealed class NotificationHistoryItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private static readonly Regex OtpKeywordRegex = new(
        @"\b(code|otp|verify|verification|passcode|pin|2fa|one-time|security|confirm|login|sign-in|sign in|auth)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OtpDashedRegex = new(@"(?<!\d)(\d{3}-\d{3})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex OtpDigitsRegex = new(@"(?<!\d)(\d{4,8})(?!\d)", RegexOptions.Compiled);

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public uint UserNotificationId { get; init; }
    public string AppUserModelId { get; init; } = "";
    public string AppName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string Glyph { get; init; } = "\uE7E7";
    public Color Accent { get; init; }
    public Brush AccentBrush { get; init; } = ThemeBrushes.Blue;
    public Brush IconBgBrush { get; init; } = ThemeBrushes.DockActiveBg;
    public DateTime ReceivedAt { get; init; } = DateTime.Now;
    public bool IsMessagingApp { get; init; }
    public string? DetectedOtpCode { get; init; }

    public bool HasDistinctBody =>
        !string.IsNullOrWhiteSpace(Body) &&
        !string.Equals(Body.Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase);

    public Visibility BodyVisibility => HasDistinctBody ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReplyVisibility => IsMessagingApp ? Visibility.Visible : Visibility.Collapsed;
    public bool HasOtpCode => !string.IsNullOrEmpty(DetectedOtpCode);
    public Visibility OtpVisibility => HasOtpCode ? Visibility.Visible : Visibility.Collapsed;
    public string OtpButtonText => HasOtpCode ? $"Copy {DetectedOtpCode}" : "";

    public string TimeText
    {
        get
        {
            var diff = DateTime.Now - ReceivedAt;
            if (diff.TotalSeconds < 60) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return ReceivedAt.ToString("HH:mm");
        }
    }

    public void RefreshTimeText() =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(TimeText)));

    public static string? ExtractOtpCode(string? title, string? body)
    {
        string combined = $"{title} {body}".Trim();
        if (string.IsNullOrWhiteSpace(combined) || !OtpKeywordRegex.IsMatch(combined))
            return null;

        var dashed = OtpDashedRegex.Match(combined);
        if (dashed.Success)
            return dashed.Groups[1].Value.Replace("-", "");

        var digits = OtpDigitsRegex.Match(combined);
        return digits.Success ? digits.Groups[1].Value : null;
    }
}

/// <summary>
/// Aggregates notifications from the same application into an expandable stack card.
/// </summary>
public sealed class NotificationAppGroup : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string AppName { get; init; } = "";
    public string AppUserModelId { get; init; } = "";
    public string Glyph { get; init; } = "\uE7E7";
    public Color Accent { get; init; }
    public Brush AccentBrush { get; init; } = ThemeBrushes.Blue;
    public Brush IconBgBrush { get; init; } = ThemeBrushes.DockActiveBg;
    public List<NotificationHistoryItem> Notifications { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ExpandGlyph)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ExpandLabel)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DetailsVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CollapsedStackDeckVisibility)));
            }
        }
    }

    public int Count => Notifications.Count;
    public bool HasMultiple => Count > 1;
    public Visibility MultipleBadgeVisibility => HasMultiple ? Visibility.Visible : Visibility.Collapsed;
    public string CountBadgeText => $"{Count} alerts";
    public string ExpandGlyph => _isExpanded ? "\uE70E" : "\uE70D";
    public string ExpandLabel => _isExpanded ? "Hide" : $"+{Count - 1} more";
    public Visibility DetailsVisibility => (HasMultiple && _isExpanded) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CollapsedStackDeckVisibility => (HasMultiple && !_isExpanded) ? Visibility.Visible : Visibility.Collapsed;
    public NotificationHistoryItem? LatestItem => Notifications.FirstOrDefault();
    public IEnumerable<NotificationHistoryItem> SecondaryNotifications => Notifications.Skip(1);
    public string LatestId => LatestItem?.Id ?? "";
    public string LatestTitle => LatestItem?.Title ?? AppName;
    public string LatestBody => LatestItem?.Body ?? "";
    public bool HasDistinctLatestBody => LatestItem?.HasDistinctBody ?? false;
    public Visibility LatestBodyVisibility => HasDistinctLatestBody ? Visibility.Visible : Visibility.Collapsed;
    public string LatestTimeText => LatestItem?.TimeText ?? "";
    public bool IsMessagingApp => LatestItem?.IsMessagingApp ?? false;
    public Visibility ReplyVisibility => LatestItem?.ReplyVisibility ?? Visibility.Collapsed;
    public string? LatestOtpCode => LatestItem?.DetectedOtpCode;
    public Visibility OtpVisibility => LatestItem?.OtpVisibility ?? Visibility.Collapsed;
    public string OtpButtonText => LatestItem?.OtpButtonText ?? "";
    public Visibility BottomActionsVisibility =>
        (ReplyVisibility == Visibility.Visible || OtpVisibility == Visibility.Visible || MultipleBadgeVisibility == Visibility.Visible)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public void RefreshTimeText()
    {
        foreach (var n in Notifications)
        {
            n.RefreshTimeText();
        }
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LatestTimeText)));
    }

    public static NotificationAppGroup FromSingle(NotificationHistoryItem item)
    {
        var group = new NotificationAppGroup
        {
            AppName = item.AppName,
            AppUserModelId = item.AppUserModelId,
            Glyph = item.Glyph,
            Accent = item.Accent,
            AccentBrush = item.AccentBrush,
            IconBgBrush = item.IconBgBrush
        };
        group.Notifications.Add(item);
        return group;
    }

    public override bool Equals(object? obj) =>
        obj is NotificationAppGroup other &&
        string.Equals(AppName, other.AppName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(LatestId, other.LatestId, StringComparison.Ordinal) &&
        Count == other.Count &&
        _isExpanded == other._isExpanded;

    public override int GetHashCode() =>
        HashCode.Combine(AppName?.ToLowerInvariant(), LatestId, Count, _isExpanded);
}

/// <summary>
/// Listens to incoming Windows Action Center notifications (e.g. Discord, Slack, WhatsApp, Outlook)
/// using Windows 10/11 UserNotificationListener and converts them into Dynamic Island toasts.
/// Maintains a 25-item ring buffer of recent notifications so dismissed banners can be reviewed,
/// and respects Do Not Disturb, Windows Focus Assist / Quiet Time, self-notification filtering, and per-app mute lists.
/// </summary>
/// <remarks>
/// <see cref="UserNotificationListener.NotificationChanged"/> is only raised for packaged (MSIX) apps with
/// package identity. For this unpackaged Win32 app subscribing either throws ("Element not found") or the
/// event silently never fires, so new notifications are detected by polling
/// <see cref="UserNotificationListener.GetNotificationsAsync"/> and diffing notification IDs.
/// </remarks>
internal sealed partial class NotificationListenerService : IDisposable
{
    public const string ChoreId = "notifications_poll";
    public const int MaxHistoryCount = 25;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly Dispatcher _ui;
    private readonly Func<AppSettings>? _getSettings;
    private readonly HashSet<uint> _seenIds = new();
    private readonly HashSet<uint> _currentIds = new();
    private readonly List<NotificationHistoryItem> _history = new();
    private UserNotificationListener? _listener;
    private DispatcherTimer? _pollTimer;
    private Choreographer? _choreographer;
    private bool _eventSubscribed;
    private bool _polling;
    private bool _suspended;
    private bool _disposed;

    public IReadOnlyList<NotificationHistoryItem> History
    {
        get
        {
            lock (_history) return _history.ToList();
        }
    }

    public int HistoryCount
    {
        get
        {
            lock (_history) return _history.Count;
        }
    }

    public event Action<ToastContent>? NotificationReceived;
    public event Action? HistoryChanged;

    public NotificationListenerService(Dispatcher ui, Func<AppSettings>? getSettings = null)
    {
        _ui = ui;
        _getSettings = getSettings;
    }

    public bool IsListening => _pollTimer?.IsEnabled ?? false;
    public bool IsAccessDenied { get; private set; }

    public void SetSuspended(bool suspended)
    {
        if (_disposed || _suspended == suspended) return;
        _suspended = suspended;
        if (_pollTimer == null) return;
        if (suspended)
        {
            _pollTimer.Stop();
        }
        else
        {
            _pollTimer.Start();
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
            {
                App.Log("[NotificationListener] UserNotificationListener API not present on this Windows build.");
                return;
            }

            _listener = UserNotificationListener.Current;

            var access = _listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                // RequestAccessAsync must be invoked on the UI thread.
                access = await _listener.RequestAccessAsync();
            }
            App.Log($"[NotificationListener] Access status: {access}");

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                IsAccessDenied = true;
                App.Log("[NotificationListener] Access denied. Enable 'Settings > Privacy & security > Notifications > Let apps access your notifications'.");
                return;
            }
            IsAccessDenied = false;

            // Snapshot existing notifications so Action Center history isn't replayed on startup.
            var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in existing) _seenIds.Add(n.Id);

            // Best-effort event subscription (works only when running with package identity).
            try
            {
                _listener.NotificationChanged += OnNotificationChanged;
                _eventSubscribed = true;
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationListener] NotificationChanged unavailable (unpackaged app), using polling: {ex.Message}");
            }

            _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = PollInterval };
            _pollTimer.Tick += (_, _) => PollAsync().FireAndForget("Notifications.Poll");
            if (!_suspended && !_disposed)
            {
                if (_choreographer != null)
                    _choreographer.Register(ChoreId, () => PollAsync().FireAndForget("Notifications.Chore"), (int)Math.Max(1, PollInterval.TotalSeconds), PowerSensitivity.Relaxed);
                else
                    _pollTimer.Start();
            }
            App.Log($"[NotificationListener] Listening ({_seenIds.Count} existing notifications ignored).");
        }
        catch (Exception ex)
        {
            App.Log($"[NotificationListener] Init failed: {ex.Message}");
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _disposed || _suspended || _listener == null) return;
        _polling = true;
        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            _currentIds.Clear();
            foreach (var notif in current)
            {
                _currentIds.Add(notif.Id);
                if (_seenIds.Add(notif.Id))
                {
                    ProcessNotification(notif);
                }
            }

            // Forget IDs that have left Action Center so the set doesn't grow unbounded.
            _seenIds.IntersectWith(_currentIds);
        }
        catch (Exception ex)
        {
            App.Log($"[NotificationListener] Poll failed: {ex.Message}");
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>
    /// Evaluates whether a notification from <paramref name="appName"/> should be suppressed due to
    /// Do Not Disturb, self-notification filtering, or per-app mute settings.
    /// </summary>
    internal static bool ShouldSuppressNotification(string appName, AppSettings? settings, bool isQuietTime = false)
    {
        if (isQuietTime) return true;
        if (settings is { DoNotDisturb: true }) return true;

        if (!string.IsNullOrWhiteSpace(appName))
        {
            // Ignore notifications originating from Dynamic Island itself
            if (appName.Contains("Dynamic Island", StringComparison.OrdinalIgnoreCase) ||
                appName.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (settings?.MutedNotificationApps is { Count: > 0 } mutedList)
            {
                foreach (var muted in mutedList)
                {
                    if (!string.IsNullOrWhiteSpace(muted) &&
                        appName.Contains(muted, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsWindowsFocusAssistActive()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                return state is QUERY_USER_NOTIFICATION_STATE.QUNS_QUIET_TIME
                             or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE
                             or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN;
            }
        }
        catch
        {
        }
        return false;
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added) return;

        // Event can fire on a background thread; marshal to UI so _seenIds stays single-threaded.
        _ui.InvokeAsync(() =>
        {
            try
            {
                if (!_seenIds.Add(args.UserNotificationId)) return;
                var notif = sender.GetNotification(args.UserNotificationId);
                if (notif != null) ProcessNotification(notif);
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationListener] Error reading notification: {ex.Message}");
            }
        });
    }

    private void ProcessNotification(UserNotification notif)
    {
        try
        {
            string appName = SafeAppName(notif);
            string aumid = SafeAumid(notif);
            var settings = _getSettings?.Invoke();
            if (ShouldSuppressNotification(appName, settings, IsWindowsFocusAssistActive()))
            {
                return;
            }

            var (title, body) = ExtractText(notif);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                return;

            string glyph = GlyphForAppName(appName);
            Color accent = AccentForAppName(appName);
            bool isMessaging = IsMessagingApp(appName, aumid);

            var item = RecordNotification(appName, title, body, glyph, accent, aumid, notif.Id);

            App.Log($"[NotificationListener] Showing notification from '{appName}' (AUMID: '{aumid}').");
            NotificationReceived?.Invoke(new ToastContent(
                glyph,
                string.IsNullOrWhiteSpace(title) ? appName : $"{appName} · {title}",
                body,
                accent,
                TimeSpan.FromSeconds(isMessaging ? 5.5 : 4),
                TargetView: "notifications",
                Priority: ToastPriority.Notification,
                Category: "notification",
                CanReply: isMessaging,
                HistoryItem: item));

            // Show it in the island *instead of* the Windows banner: removing the notification
            // dismisses the native toast popup (it is also removed from Action Center).
            if (settings?.ReplaceWindowsBanners ?? false)
            {
                try { _listener?.RemoveNotification(notif.Id); }
                catch (Exception ex) { App.Log($"[NotificationListener] Could not dismiss Windows banner: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[NotificationListener] Error reading notification: {ex.Message}");
        }
    }

    public NotificationHistoryItem RecordNotification(
        string appName,
        string title,
        string body,
        string? glyphOverride = null,
        Color? accentOverride = null,
        string? aumid = null,
        uint userNotificationId = 0)
    {
        string glyph = glyphOverride ?? GlyphForAppName(appName);
        Color accent = accentOverride ?? AccentForAppName(appName);
        bool isMessaging = IsMessagingApp(appName, aumid);
        string cleanTitle = string.IsNullOrWhiteSpace(title) ? appName : title.Trim();
        string cleanBody = string.IsNullOrWhiteSpace(body) ? "" : body.Trim();
        var item = new NotificationHistoryItem
        {
            AppName = string.IsNullOrWhiteSpace(appName) ? "Notification" : appName.Trim(),
            AppUserModelId = aumid ?? "",
            UserNotificationId = userNotificationId,
            Title = cleanTitle,
            Body = cleanBody,
            Glyph = glyph,
            Accent = accent,
            AccentBrush = ThemeBrushes.CreateFrozen(accent),
            IconBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x28, accent.R, accent.G, accent.B)),
            ReceivedAt = DateTime.Now,
            IsMessagingApp = isMessaging,
            DetectedOtpCode = NotificationHistoryItem.ExtractOtpCode(cleanTitle, cleanBody)
        };

        lock (_history)
        {
            _history.Insert(0, item);
            while (_history.Count > MaxHistoryCount)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }

        HistoryChanged?.Invoke();
        return item;
    }

    public bool RemoveHistoryItem(string id)
    {
        bool removed;
        uint userNotifId = 0;
        lock (_history)
        {
            var item = _history.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                userNotifId = item.UserNotificationId;
                removed = _history.Remove(item);
            }
            else
            {
                removed = false;
            }
        }
        if (removed)
        {
            if (userNotifId > 0)
            {
                RemoveNativeNotification(userNotifId);
            }
            HistoryChanged?.Invoke();
        }
        return removed;
    }

    public int RemoveHistoryItemsByApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return 0;
        int count = 0;
        var toRemoveIds = new List<uint>();
        lock (_history)
        {
            var matching = _history.Where(i => string.Equals(i.AppName, appName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var item in matching)
            {
                if (item.UserNotificationId > 0) toRemoveIds.Add(item.UserNotificationId);
                _history.Remove(item);
                count++;
            }
        }

        if (count > 0)
        {
            foreach (var uid in toRemoveIds)
            {
                RemoveNativeNotification(uid);
            }
            HistoryChanged?.Invoke();
        }
        return count;
    }

    public static List<NotificationAppGroup> GroupNotifications(IEnumerable<NotificationHistoryItem> items)
    {
        var groups = new List<NotificationAppGroup>();
        foreach (var group in items.GroupBy(i => string.IsNullOrWhiteSpace(i.AppName) ? (string.IsNullOrWhiteSpace(i.AppUserModelId) ? "System" : i.AppUserModelId) : i.AppName, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var appGroup = new NotificationAppGroup
            {
                AppName = first.AppName,
                AppUserModelId = first.AppUserModelId,
                Glyph = first.Glyph,
                Accent = first.Accent,
                AccentBrush = first.AccentBrush,
                IconBgBrush = first.IconBgBrush
            };
            appGroup.Notifications.AddRange(group);
            groups.Add(appGroup);
        }
        return groups;
    }

    public static List<NotificationHistoryItem> FilterNotifications(
        IEnumerable<NotificationHistoryItem> items,
        string filter,
        string? query)
    {
        IEnumerable<NotificationHistoryItem> result = items;

        if (string.Equals(filter, "Messages", StringComparison.OrdinalIgnoreCase))
        {
            result = result.Where(i => i.IsMessagingApp);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            result = result.Where(i =>
                i.AppName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Body.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToList();
    }

    public void RemoveNativeNotification(uint userNotificationId)
    {
        if (userNotificationId == 0 || _listener == null) return;
        try
        {
            _listener.RemoveNotification(userNotificationId);
        }
        catch (Exception ex)
        {
            App.Log($"[NotificationListener] Could not dismiss native notification {userNotificationId}: {ex.Message}");
        }
    }


    public void ClearHistory()
    {
        bool hadItems;
        lock (_history)
        {
            hadItems = _history.Count > 0;
            _history.Clear();
        }
        if (hadItems)
        {
            HistoryChanged?.Invoke();
        }
    }

    private static string SafeAppName(UserNotification notif)
    {
        try
        {
            string? name = notif.AppInfo?.DisplayInfo?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch
        {
            // AppInfo.DisplayInfo throws for some unpackaged (AUMID-only) senders.
        }

        string aumid = SafeAumid(notif);
        if (!string.IsNullOrWhiteSpace(aumid))
        {
            string lower = aumid.ToLowerInvariant();
            if (lower.Contains("whatsapp")) return "WhatsApp";
            if (lower.Contains("yourphone") || lower.Contains("phonelink")) return "Phone Link";
            if (lower.Contains("telegram")) return "Telegram";
            if (lower.Contains("discord")) return "Discord";
            if (lower.Contains("slack")) return "Slack";
            if (lower.Contains("teams")) return "Microsoft Teams";
            if (lower.Contains("signal")) return "Signal";
            if (lower.Contains("skype")) return "Skype";
            if (lower.Contains("spotify")) return "Spotify";
            if (lower.Contains("outlook")) return "Outlook";
        }

        return "Notification";
    }

    private static (string Title, string Body) ExtractText(UserNotification notif)
    {
        var visual = notif.Notification?.Visual;
        if (visual == null) return ("", "");

        // Prefer ToastGeneric, but fall back to the first binding for legacy templates (ToastText02, etc.).
        var binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric) ?? visual.Bindings.FirstOrDefault();
        if (binding == null) return ("", "");

        var texts = binding.GetTextElements()
            .Select(t => t.Text?.Trim() ?? "")
            .Where(t => t.Length > 0)
            .ToList();

        string title = texts.Count > 0 ? texts[0] : "";
        string body = texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : "";
        return (title, body);
    }

    private static string GlyphForAppName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("mail") || n.Contains("outlook")) return "\uE715";
        if (n.Contains("phone link") || n.Contains("your phone")) return "\uE8EA";
        if (n.Contains("chat") || n.Contains("message") || n.Contains("whatsapp") || n.Contains("telegram") || n.Contains("slack") || n.Contains("discord") || n.Contains("signal")) return "\uE8BD";
        if (n.Contains("teams") || n.Contains("zoom") || n.Contains("meet") || n.Contains("call")) return "\uE717";
        if (n.Contains("update") || n.Contains("windows")) return "\uE777";
        return "\uE7E7";
    }

    private static Color AccentForAppName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("whatsapp")) return Color.FromRgb(0x25, 0xD3, 0x66);
        if (n.Contains("telegram")) return Color.FromRgb(0x00, 0x88, 0xCC);
        if (n.Contains("discord")) return Color.FromRgb(0x58, 0x65, 0xF2);
        if (n.Contains("slack")) return Color.FromRgb(0xEC, 0xB2, 0x2E);
        if (n.Contains("teams")) return Color.FromRgb(0x62, 0x64, 0xA7);
        if (n.Contains("zoom")) return Color.FromRgb(0x0B, 0x5C, 0xFF);
        if (n.Contains("phone link") || n.Contains("your phone")) return Color.FromRgb(0x00, 0x78, 0xD4);
        if (n.Contains("signal")) return Color.FromRgb(0x3A, 0x76, 0xF0);
        if (n.Contains("mail") || n.Contains("outlook")) return Color.FromRgb(0x00, 0x78, 0xD4);
        return Color.FromRgb(0x0A, 0x84, 0xFF);
    }

    /// <summary>
    /// Identifies whether the application or AUMID represents an instant messaging or communication app
    /// that supports interactive Quick Reply in Dynamic Island.
    /// </summary>
    public static bool IsMessagingApp(string? appName, string? aumid = null)
    {
        string n = (appName ?? "").ToLowerInvariant();
        string a = (aumid ?? "").ToLowerInvariant();
        string combined = $"{n} {a}".Trim();
        if (string.IsNullOrWhiteSpace(combined)) return false;

        return combined.Contains("whatsapp") ||
               combined.Contains("telegram") ||
               combined.Contains("discord") ||
               combined.Contains("slack") ||
               combined.Contains("teams") ||
               combined.Contains("signal") ||
               combined.Contains("skype") ||
               combined.Contains("phone link") ||
               combined.Contains("yourphone") ||
               combined.Contains("your phone") ||
               combined.Contains("phoneexperiencehost") ||
               combined.Contains("messages") ||
               combined.Contains("messenger") ||
               combined.Contains("wechat") ||
               combined.Contains("viber") ||
               combined.Contains("chat");
    }

    public static bool IsMessagingApp(string? appName) => IsMessagingApp(appName, null);

    public void AttachChoreographer(Choreographer choreographer)
    {
        if (_disposed || choreographer == null) return;
        _choreographer = choreographer;
        _pollTimer?.Stop();
        if (!_suspended && _listener != null)
        {
            _choreographer.Register(ChoreId, () => PollAsync().FireAndForget("Notifications.Chore"), (int)Math.Max(1, PollInterval.TotalSeconds), PowerSensitivity.Relaxed);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer?.Stop();
        _choreographer?.Unregister(ChoreId);
        if (_listener != null && _eventSubscribed)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }
    }
}
