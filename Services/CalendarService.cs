using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Windows.ApplicationModel.Appointments;
using Windows.Foundation.Metadata;

namespace DynamicIsland.Services;

public sealed record CalendarEventInfo(
    string Subject,
    DateTimeOffset StartTime,
    TimeSpan Duration,
    string Location,
    string? JoinUrl)
{
    public int MinutesUntilStart => (int)Math.Round((StartTime - DateTimeOffset.Now).TotalMinutes);

    public string TimeBadgeText
    {
        get
        {
            int mins = MinutesUntilStart;
            if (mins <= 0) return $"Now · {Subject}";
            if (mins < 60) return $"In {mins}m · {Subject}";
            return $"{StartTime:HH:mm} · {Subject}";
        }
    }
}

/// <summary>
/// Monitors upcoming Windows calendar appointments via WinRT <see cref="AppointmentManager"/>
/// and extracts one-click video conference URLs (Zoom, Microsoft Teams, Google Meet, Webex).
/// </summary>
internal sealed class CalendarService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private static readonly Regex MeetingUrlRegex = new(
        @"https:\/\/(?:[\w-]+\.)?(?:zoom\.us\/j\/[^\s""'<>]+|teams\.microsoft\.com\/l\/meetup-join\/[^\s""'<>]+|teams\.live\.com\/meet\/[^\s""'<>]+|meet\.google\.com\/[a-z0-9-]+|[\w-]+\.webex\.com\/(?:meet|join)\/[^\s""'<>]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public CalendarEventInfo? NextEvent { get; private set; }

    public event Action<CalendarEventInfo?>? Updated;

    public CalendarService(Dispatcher ui)
    {
        _ui = ui;
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => _ = RefreshAsync(), _ui);
    }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.ApplicationModel.Appointments.AppointmentManager"))
            {
                return;
            }

            var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
            if (store == null) return;

            var now = DateTimeOffset.Now;
            var options = new FindAppointmentsOptions { MaxCount = 10, IncludeHidden = false };
            var appointments = await store.FindAppointmentsAsync(now.AddMinutes(-5), TimeSpan.FromHours(8), options);

            CalendarEventInfo? chosen = null;
            foreach (var appt in appointments.OrderBy(a => a.StartTime))
            {
                if (appt.AllDay) continue;
                var end = appt.StartTime + appt.Duration;
                if (end < now) continue;

                string? joinUrl = ExtractMeetingUrl(appt.Uri?.ToString())
                                  ?? ExtractMeetingUrl(appt.Location)
                                  ?? ExtractMeetingUrl(appt.Details);

                chosen = new CalendarEventInfo(
                    string.IsNullOrWhiteSpace(appt.Subject) ? "Calendar Event" : appt.Subject.Trim(),
                    appt.StartTime,
                    appt.Duration,
                    appt.Location ?? "",
                    joinUrl);
                break;
            }

            NextEvent = chosen;
            _ = _ui.InvokeAsync(() => Updated?.Invoke(chosen));
        }
        catch (Exception ex)
        {
            // AppointmentStore may be restricted on some unpackaged setups; fail silently with debug log
            App.Log($"[CalendarService] Poll skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a Zoom, Microsoft Teams, Google Meet, or Webex join link from calendar text.
    /// </summary>
    public static string? ExtractMeetingUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = MeetingUrlRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    public static bool LaunchEventOrCalendar(CalendarEventInfo? evt)
    {
        try
        {
            if (evt != null && !string.IsNullOrWhiteSpace(evt.JoinUrl))
            {
                Process.Start(new ProcessStartInfo(evt.JoinUrl) { UseShellExecute = true });
                return true;
            }

            Process.Start(new ProcessStartInfo("outlookcal:") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[CalendarService] Launch failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }
}
