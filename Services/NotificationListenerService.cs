using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Listens to incoming Windows Action Center notifications (e.g. Discord, Slack, WhatsApp, Outlook)
/// using Windows 10/11 UserNotificationListener and converts them into Dynamic Island toasts.
/// </summary>
internal sealed class NotificationListenerService : IDisposable
{
    private readonly Dispatcher _ui;
    private UserNotificationListener? _listener;
    private bool _initialized;
    private bool _accessGranted;

    public event Action<ToastContent>? NotificationReceived;

    public NotificationListenerService(Dispatcher ui)
    {
        _ui = ui;
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
            {
                Debug.WriteLine("[NotificationListener] UserNotificationListener API not present on this Windows build.");
                return;
            }

            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            _accessGranted = access == UserNotificationListenerAccessStatus.Allowed;
            Debug.WriteLine($"[NotificationListener] Access status: {access}");

            if (_accessGranted)
            {
                _listener.NotificationChanged += OnNotificationChanged;
                _initialized = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NotificationListener] Init failed: {ex.Message}");
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added) return;

        try
        {
            var notif = sender.GetNotification(args.UserNotificationId);
            if (notif == null) return;

            string appName = notif.AppInfo?.DisplayInfo?.DisplayName ?? "Notification";
            string title = "";
            string body = "";

            var binding = notif.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var textElements = binding.GetTextElements();
                var list = textElements?.ToList();
                if (list != null && list.Count > 0)
                {
                    title = list[0].Text;
                    if (list.Count > 1) body = list[1].Text;
                }
            }

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                return;

            string glyph = GlyphForAppName(appName);
            Color accent = AccentForAppName(appName);

            _ui.InvokeAsync(() =>
            {
                NotificationReceived?.Invoke(new ToastContent(
                    glyph,
                    string.IsNullOrWhiteSpace(title) ? appName : $"{appName} · {title}",
                    body,
                    accent,
                    TimeSpan.FromSeconds(3.5)));
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NotificationListener] Error reading notification: {ex.Message}");
        }
    }

    private static string GlyphForAppName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("mail") || n.Contains("outlook")) return "\uE715";
        if (n.Contains("chat") || n.Contains("message") || n.Contains("whatsapp") || n.Contains("telegram") || n.Contains("slack") || n.Contains("discord")) return "\uE8BD";
        if (n.Contains("teams") || n.Contains("zoom") || n.Contains("meet") || n.Contains("call")) return "\uE717";
        if (n.Contains("update") || n.Contains("windows")) return "\uE777";
        return "\uE7E7"; // Generic notification star/alert
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
        if (_listener != null && _initialized)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }
    }
}
