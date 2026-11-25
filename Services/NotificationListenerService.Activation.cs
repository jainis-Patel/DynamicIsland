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

internal sealed partial class NotificationListenerService
{
    /// <summary>
    /// Activates the action that would occur when clicking a Windows notification banner:
    /// Brings the sending application to the foreground, opens URLs, or launches the app via AUMID.
    /// When <paramref name="preferApp"/> is true (or if the sender is a messaging app), URL interception
    /// is suppressed in favor of activating the messenger application directly.
    /// </summary>
    public static bool ActivateNotification(NotificationHistoryItem item, bool preferApp = false)
    {
        if (item == null) return false;

        // 1. If notification body contains a URL, open the URL in default browser ONLY IF
        // preferApp is false AND it is not a messaging app
        if (!preferApp && !item.IsMessagingApp && !string.IsNullOrWhiteSpace(item.Body))
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
        IntPtr hwnd = FindAppWindow(item.AppName, item.AppUserModelId, item.Title);
        if (hwnd != IntPtr.Zero)
        {
            return ForceForegroundWindow(hwnd);
        }

        // 3. Try launching via known protocol URI if applicable
        if (TryLaunchKnownAppProtocol(item.AppName, item.AppUserModelId))
        {
            return true;
        }

        // 4. Activate via Windows AUMID (shell:AppsFolder\<AppUserModelId>)
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

        // 5. Fallback: try launching by known application name
        return TryLaunchKnownApp(item.AppName);
    }

    /// <summary>
    /// Forcibly brings a target window to the foreground, bypassing Windows SetForegroundWindow
    /// restrictions by attaching thread input queues and unlocking foreground lock timeout.
    /// </summary>
    public static bool ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);

            IntPtr foregroundWnd = GetForegroundWindow();
            uint foregroundThread = foregroundWnd != IntPtr.Zero
                ? GetWindowThreadProcessId(foregroundWnd, out _)
                : 0;
            uint targetThread = GetWindowThreadProcessId(hWnd, out _);
            uint currentThread = (uint)GetCurrentThreadId();

            bool attachedFore = false;
            bool attachedCurrent = false;

            if (foregroundThread != 0 && foregroundThread != targetThread)
            {
                attachedFore = AttachThreadInput(foregroundThread, targetThread, true);
            }
            if (currentThread != 0 && currentThread != targetThread)
            {
                attachedCurrent = AttachThreadInput(currentThread, targetThread, true);
            }

            // Simulate pressing and releasing ALT key to reset foreground lock timeout
            keybd_event((byte)VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);

            if (attachedFore)
            {
                AttachThreadInput(foregroundThread, targetThread, false);
            }
            if (attachedCurrent)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[NotificationListener] ForceForegroundWindow error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds the top-level window handle for a target application by matching candidate process names,
    /// window titles, and AUMIDs using Win32 EnumWindows.
    /// </summary>
    public static IntPtr FindAppWindow(string appName, string aumid, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(appName) && string.IsNullOrWhiteSpace(aumid))
            return IntPtr.Zero;

        var candidates = GetCandidateProcessNames(appName, aumid);
        string cleanApp = (appName ?? "").Trim().ToLowerInvariant();
        string cleanTitle = (title ?? "").Trim().ToLowerInvariant();

        IntPtr bestHwnd = IntPtr.Zero;
        int bestScore = 0;

        EnumWindows((hWnd, lParam) =>
        {
            try
            {
                if (!IsWindowVisible(hWnd) && !IsIconic(hWnd))
                    return true;

                if (GetWindowRect(hWnd, out var rect))
                {
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;
                    if (w < 80 || h < 80) return true;
                }

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return true;

                string procName = "";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                }
                catch (Exception ex) { App.LogDebug("Notification.GetProcess", ex); }

                if (string.IsNullOrEmpty(procName)) return true;

                bool matchesProc = candidates.Any(c => string.Equals(c, procName, StringComparison.OrdinalIgnoreCase));
                if (!matchesProc) return true;

                int score = 10;
                var sb = new StringBuilder(256);
                int len = GetWindowText(hWnd, sb, 256);
                if (len > 0)
                {
                    string winTitle = sb.ToString().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(cleanTitle) && winTitle.Contains(cleanTitle))
                    {
                        score += 50;
                    }
                    if (!string.IsNullOrEmpty(cleanApp) && winTitle.Contains(cleanApp))
                    {
                        score += 20;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestHwnd = hWnd;
                }
            }
            catch (Exception ex) { App.LogDebug("Notification.EnumWindow", ex); }

            return true;
        }, IntPtr.Zero);

        return bestHwnd;
    }

    internal static List<string> GetCandidateProcessNames(string appName, string aumid)
    {
        var list = new List<string>();
        string combined = $"{appName} {aumid}".ToLowerInvariant();

        if (combined.Contains("whatsapp"))
        {
            list.Add("WhatsApp");
            list.Add("WhatsApp.exe");
        }
        if (combined.Contains("discord"))
        {
            list.Add("Discord");
            list.Add("Discord.exe");
        }
        if (combined.Contains("telegram"))
        {
            list.Add("Telegram");
            list.Add("Telegram.exe");
        }
        if (combined.Contains("slack"))
        {
            list.Add("slack");
            list.Add("Slack");
        }
        if (combined.Contains("phone link") || combined.Contains("yourphone") || combined.Contains("your phone") || combined.Contains("phoneexperiencehost"))
        {
            list.Add("PhoneExperienceHost");
            list.Add("YourPhone");
        }
        if (combined.Contains("teams"))
        {
            list.Add("ms-teams");
            list.Add("Teams");
        }
        if (combined.Contains("signal"))
        {
            list.Add("Signal");
        }
        if (combined.Contains("skype"))
        {
            list.Add("Skype");
        }
        if (combined.Contains("messenger"))
        {
            list.Add("Messenger");
        }
        if (combined.Contains("wechat"))
        {
            list.Add("WeChat");
        }
        if (combined.Contains("viber"))
        {
            list.Add("Viber");
        }
        if (combined.Contains("outlook") || combined.Contains("mail"))
        {
            list.Add("OUTLOOK");
            list.Add("olk");
            list.Add("HxOutlook");
            list.Add("Thunderbird");
        }
        if (combined.Contains("visual studio code") || combined.Contains("code"))
        {
            list.Add("code");
        }
        if (combined.Contains("chatgpt"))
        {
            list.Add("ChatGPT");
        }
        if (combined.Contains("spotify"))
        {
            list.Add("Spotify");
        }
        if (combined.Contains("terminal"))
        {
            list.Add("WindowsTerminal");
        }

        string clean = (appName ?? "").Trim().Replace(" ", "");
        if (!string.IsNullOrEmpty(clean) && !list.Contains(clean, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(clean);
        }

        return list;
    }

    private static bool TryLaunchKnownAppProtocol(string appName, string aumid)
    {
        string combined = $"{appName} {aumid}".ToLowerInvariant();
        try
        {
            if (combined.Contains("whatsapp"))
            {
                Process.Start(new ProcessStartInfo("whatsapp:") { UseShellExecute = true });
                return true;
            }
            if (combined.Contains("telegram"))
            {
                Process.Start(new ProcessStartInfo("tg:") { UseShellExecute = true });
                return true;
            }
            if (combined.Contains("discord"))
            {
                Process.Start(new ProcessStartInfo("discord:") { UseShellExecute = true });
                return true;
            }
            if (combined.Contains("teams"))
            {
                Process.Start(new ProcessStartInfo("msteams:") { UseShellExecute = true });
                return true;
            }
            if (combined.Contains("phone link") || combined.Contains("yourphone") || combined.Contains("phoneexperiencehost"))
            {
                Process.Start(new ProcessStartInfo("ms-chat:") { UseShellExecute = true });
                return true;
            }
            if (combined.Contains("skype"))
            {
                Process.Start(new ProcessStartInfo("skype:") { UseShellExecute = true });
                return true;
            }
            if (combined.Contains("spotify"))
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
                return true;
            }
        }
        catch (Exception ex) { App.LogDebug("Notification.LaunchKnownUri", ex); }

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
        catch (Exception ex) { App.LogDebug("Notification.LaunchKnownApp", ex); }

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
        catch (Exception ex) { App.LogDebug("Notification.SafeAumid", ex); }
        return "";
    }
}
