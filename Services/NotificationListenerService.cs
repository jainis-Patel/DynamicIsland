using System.Diagnostics;
using System.Text.RegularExpressions;
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
public sealed class NotificationHistoryItem
{
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
internal sealed class NotificationListenerService : IDisposable
{
    public const int MaxHistoryCount = 25;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly Dispatcher _ui;
    private readonly Func<AppSettings>? _getSettings;
    private readonly HashSet<uint> _seenIds = new();
    private readonly List<NotificationHistoryItem> _history = new();
    private UserNotificationListener? _listener;
    private DispatcherTimer? _pollTimer;
    private bool _eventSubscribed;
    private bool _polling;
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
                App.Log("[NotificationListener] Access denied. Enable 'Settings > Privacy & security > Notifications > Let apps access your notifications'.");
                return;
            }

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
            _pollTimer.Tick += async (_, _) => await PollAsync();
            _pollTimer.Start();
            App.Log($"[NotificationListener] Listening ({_seenIds.Count} existing notifications ignored).");
        }
        catch (Exception ex)
        {
            App.Log($"[NotificationListener] Init failed: {ex.Message}");
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _disposed || _listener == null) return;
        _polling = true;
        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var currentIds = new HashSet<uint>();
            foreach (var notif in current)
            {
                currentIds.Add(notif.Id);
                if (_seenIds.Add(notif.Id))
                {
                    ProcessNotification(notif);
                }
            }

            // Forget IDs that have left Action Center so the set doesn't grow unbounded.
            _seenIds.IntersectWith(currentIds);
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
            bool isMessaging = IsMessagingApp(appName);

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
        bool isMessaging = IsMessagingApp(appName);
        var item = new NotificationHistoryItem
        {
            AppName = string.IsNullOrWhiteSpace(appName) ? "Notification" : appName.Trim(),
            AppUserModelId = aumid ?? "",
            UserNotificationId = userNotificationId,
            Title = string.IsNullOrWhiteSpace(title) ? appName : title.Trim(),
            Body = string.IsNullOrWhiteSpace(body) ? title.Trim() : body.Trim(),
            Glyph = glyph,
            Accent = accent,
            AccentBrush = ThemeBrushes.CreateFrozen(accent),
            IconBgBrush = ThemeBrushes.CreateFrozen(Color.FromArgb(0x28, accent.R, accent.G, accent.B)),
            ReceivedAt = DateTime.Now,
            IsMessagingApp = isMessaging
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

    /// <summary>
    /// Activates the action that would occur when clicking a Windows notification banner:
    /// Brings the sending application to the foreground, opens URLs, or launches the app via AUMID.
    /// </summary>
    public static bool ActivateNotification(NotificationHistoryItem item)
    {
        if (item == null) return false;

        // 1. If notification body contains a URL, open the URL in default browser
        if (!string.IsNullOrWhiteSpace(item.Body))
        {
            var match = Regex.Match(item.Body, @"https?://[^\s]+");
            if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    return true;
                }
                catch { }
            }
        }

        // 2. Try to find and focus an existing running window for this application
        bool focused = TryFocusAppWindow(item.AppName, item.AppUserModelId);
        if (focused) return true;

        // 3. If not already focused, activate via Windows AUMID (shell:AppsFolder\<AppUserModelId>)
        if (!string.IsNullOrWhiteSpace(item.AppUserModelId))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{item.AppUserModelId}")
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationListener] Could not launch via AUMID '{item.AppUserModelId}': {ex.Message}");
            }
        }

        // 4. Fallback: try launching or focusing by known application name
        return TryLaunchKnownApp(item.AppName);
    }

    private static void BringWindowToForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }
            SetForegroundWindow(hWnd);
        }
        catch { }
    }

    private static bool TryFocusAppWindow(string appName, string aumid)
    {
        if (string.IsNullOrWhiteSpace(appName) && string.IsNullOrWhiteSpace(aumid))
            return false;

        string cleanName = appName.Trim().ToLowerInvariant();

        // Check common process aliases
        List<string> candidateNames = new();
        if (cleanName.Contains("visual studio code") || cleanName.Contains("code"))
            candidateNames.Add("code");
        if (cleanName.Contains("chatgpt"))
            candidateNames.Add("ChatGPT");
        if (cleanName.Contains("slack"))
            candidateNames.Add("slack");
        if (cleanName.Contains("discord"))
            candidateNames.Add("Discord");
        if (cleanName.Contains("telegram"))
            candidateNames.Add("Telegram");
        if (cleanName.Contains("whatsapp"))
            candidateNames.Add("WhatsApp");
        if (cleanName.Contains("spotify"))
            candidateNames.Add("Spotify");
        if (cleanName.Contains("chrome"))
            candidateNames.Add("chrome");
        if (cleanName.Contains("edge"))
            candidateNames.Add("msedge");
        if (cleanName.Contains("outlook"))
        {
            candidateNames.Add("OUTLOOK");
            candidateNames.Add("olk");
        }
        if (cleanName.Contains("terminal"))
            candidateNames.Add("WindowsTerminal");

        candidateNames.Add(cleanName.Replace(" ", ""));

        try
        {
            foreach (var candidate in candidateNames)
            {
                var procs = Process.GetProcessesByName(candidate);
                try
                {
                    foreach (var p in procs)
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            BringWindowToForeground(p.MainWindowHandle);
                            return true;
                        }
                    }
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }

            // Also search by MainWindowTitle containing appName
            if (cleanName.Length > 2)
            {
                var allProcs = Process.GetProcesses();
                try
                {
                    foreach (var p in allProcs)
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero &&
                                !string.IsNullOrWhiteSpace(p.MainWindowTitle) &&
                                p.MainWindowTitle.ToLowerInvariant().Contains(cleanName))
                            {
                                BringWindowToForeground(p.MainWindowHandle);
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    foreach (var p in allProcs) p.Dispose();
                }
            }
        }
        catch { }

        return false;
    }

    private static bool TryLaunchKnownApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return false;
        string clean = appName.ToLowerInvariant();

        try
        {
            if (clean.Contains("spotify"))
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
                return true;
            }
            if (clean.Contains("discord"))
            {
                Process.Start(new ProcessStartInfo("discord:") { UseShellExecute = true });
                return true;
            }
            if (clean.Contains("telegram"))
            {
                Process.Start(new ProcessStartInfo("tg:") { UseShellExecute = true });
                return true;
            }
            if (clean.Contains("whatsapp"))
            {
                Process.Start(new ProcessStartInfo("whatsapp:") { UseShellExecute = true });
                return true;
            }
            if (clean.Contains("mail") || clean.Contains("outlook"))
            {
                Process.Start(new ProcessStartInfo("mailto:") { UseShellExecute = true });
                return true;
            }
            if (clean.Contains("visual studio code") || clean.Contains("code"))
            {
                Process.Start(new ProcessStartInfo("code") { UseShellExecute = true });
                return true;
            }
            if (clean.Contains("chatgpt"))
            {
                Process.Start(new ProcessStartInfo("chatgpt") { UseShellExecute = true });
                return true;
            }
        }
        catch { }

        return false;
    }

    private static string SafeAumid(UserNotification notif)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(notif.AppInfo?.AppUserModelId))
                return notif.AppInfo.AppUserModelId;
            if (!string.IsNullOrWhiteSpace(notif.AppInfo?.Id))
                return notif.AppInfo.Id;
        }
        catch { }
        return "";
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
        if (n.Contains("chat") || n.Contains("message") || n.Contains("whatsapp") || n.Contains("telegram") || n.Contains("slack") || n.Contains("discord")) return "\uE8BD";
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
        if (n.Contains("mail") || n.Contains("outlook")) return Color.FromRgb(0x00, 0x78, 0xD4);
        return Color.FromRgb(0x0A, 0x84, 0xFF);
    }

    /// <summary>
    /// Identifies whether the application is an instant messaging or communication app
    /// that supports interactive Quick Reply in Dynamic Island.
    /// </summary>
    public static bool IsMessagingApp(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return false;
        string n = appName.ToLowerInvariant();
        return n.Contains("whatsapp") ||
               n.Contains("telegram") ||
               n.Contains("discord") ||
               n.Contains("slack") ||
               n.Contains("teams") ||
               n.Contains("signal") ||
               n.Contains("skype") ||
               n.Contains("phone link") ||
               n.Contains("your phone") ||
               n.Contains("messages") ||
               n.Contains("messenger") ||
               n.Contains("wechat") ||
               n.Contains("viber") ||
               n.Contains("chat");
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer?.Stop();
        if (_listener != null && _eventSubscribed)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }
    }
}
