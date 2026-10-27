using System.Windows.Media;
using System.Windows.Threading;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIsland.Core;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Listens to incoming Windows Action Center notifications (e.g. Discord, Slack, WhatsApp, Outlook)
/// using Windows 10/11 UserNotificationListener and converts them into Dynamic Island toasts.
/// Respects Do Not Disturb, Windows Focus Assist / Quiet Time, self-notification filtering, and per-app mute lists.
/// </summary>
/// <remarks>
/// <see cref="UserNotificationListener.NotificationChanged"/> is only raised for packaged (MSIX) apps with
/// package identity. For this unpackaged Win32 app subscribing either throws ("Element not found") or the
/// event silently never fires, so new notifications are detected by polling
/// <see cref="UserNotificationListener.GetNotificationsAsync"/> and diffing notification IDs.
/// </remarks>
internal sealed class NotificationListenerService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly Dispatcher _ui;
    private readonly Func<AppSettings>? _getSettings;
    private readonly HashSet<uint> _seenIds = new();
    private UserNotificationListener? _listener;
    private DispatcherTimer? _pollTimer;
    private bool _eventSubscribed;
    private bool _polling;
    private bool _disposed;

    public event Action<ToastContent>? NotificationReceived;

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

            App.Log($"[NotificationListener] Showing notification from '{appName}'.");
            NotificationReceived?.Invoke(new ToastContent(
                glyph,
                string.IsNullOrWhiteSpace(title) ? appName : $"{appName} · {title}",
                body,
                accent,
                TimeSpan.FromSeconds(4),
                TargetView: null,
                Priority: ToastPriority.Notification));

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
