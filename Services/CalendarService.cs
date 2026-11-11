using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using DynamicIsland.Core;
using Windows.ApplicationModel.Appointments;
using Windows.Foundation.Metadata;

namespace DynamicIsland.Services;

public sealed record CalendarEventInfo(
    string Subject,
    DateTimeOffset StartTime,
    TimeSpan Duration,
    string Location,
    string? JoinUrl,
    string ColorTag = "#FF9F0A")
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

public sealed class IslandCustomEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Subject { get; set; } = "";
    public DateTime StartTime { get; set; } = DateTime.Now;
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(30);
    public string? Location { get; set; }
    public string? JoinUrl { get; set; }
    public string ColorTag { get; set; } = "#FF9F0A";
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
    private readonly string _eventsFilePath;
    private readonly object _eventsLock = new();
    private readonly List<IslandCustomEvent> _customEvents = new();
    private bool _disposed;

    public CalendarEventInfo? NextEvent { get; private set; }

    public event Action<CalendarEventInfo?>? Updated;

    public CalendarService(Dispatcher ui, string? eventsFilePath = null)
    {
        _ui = ui;
        _eventsFilePath = eventsFilePath ?? AppPaths.EventsFile;
        LoadCustomEvents();
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => _ = RefreshAsync(), _ui);
    }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        _ = RefreshAsync();
    }

    public IReadOnlyList<IslandCustomEvent> GetEventsForDate(DateTime date)
    {
        lock (_eventsLock)
        {
            return _customEvents.Where(e => e.StartTime.Date == date.Date)
                                .OrderBy(e => e.StartTime)
                                .ToList();
        }
    }

    public HashSet<int> GetDaysWithEvents(int year, int month)
    {
        lock (_eventsLock)
        {
            var set = new HashSet<int>();
            foreach (var e in _customEvents)
            {
                if (e.StartTime.Year == year && e.StartTime.Month == month)
                {
                    set.Add(e.StartTime.Day);
                }
            }
            return set;
        }
    }

    public IslandCustomEvent AddEvent(string subject, DateTime startTime, TimeSpan duration, string? location = null, string? joinUrl = null, string colorTag = "#FF9F0A")
    {
        var evt = new IslandCustomEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Subject = string.IsNullOrWhiteSpace(subject) ? "Event" : subject.Trim(),
            StartTime = startTime,
            Duration = duration > TimeSpan.Zero ? duration : TimeSpan.FromMinutes(30),
            Location = location?.Trim(),
            JoinUrl = joinUrl?.Trim() ?? ExtractMeetingUrl(location),
            ColorTag = colorTag
        };

        lock (_eventsLock)
        {
            _customEvents.Add(evt);
            SaveCustomEvents();
        }

        _ = RefreshAsync();
        return evt;
    }

    public bool DeleteEvent(string id)
    {
        bool removed = false;
        lock (_eventsLock)
        {
            int idx = _customEvents.FindIndex(e => e.Id == id);
            if (idx >= 0)
            {
                _customEvents.RemoveAt(idx);
                removed = true;
                SaveCustomEvents();
            }
        }

        if (removed)
        {
            _ = RefreshAsync();
        }
        return removed;
    }

    private void LoadCustomEvents()
    {
        lock (_eventsLock)
        {
            _customEvents.Clear();
            var loaded = Core.AtomicJsonStore.Load<List<IslandCustomEvent>>(_eventsFilePath, () => new List<IslandCustomEvent>());
            if (loaded != null && loaded.Count > 0)
            {
                _customEvents.AddRange(loaded);
            }
        }
    }

    private void SaveCustomEvents()
    {
        List<IslandCustomEvent> snapshot;
        lock (_eventsLock)
        {
            snapshot = new List<IslandCustomEvent>(_customEvents);
        }
        Core.AtomicJsonStore.Save(_eventsFilePath, snapshot, _eventsLock);
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        try
        {
            var now = DateTimeOffset.Now;
            var candidates = new List<CalendarEventInfo>();

            // 1. Gather local custom events
            lock (_eventsLock)
            {
                foreach (var ce in _customEvents)
                {
                    var end = ce.StartTime + ce.Duration;
                    if (end >= now.LocalDateTime)
                    {
                        candidates.Add(new CalendarEventInfo(
                            ce.Subject,
                            new DateTimeOffset(ce.StartTime),
                            ce.Duration,
                            ce.Location ?? "",
                            ce.JoinUrl,
                            ce.ColorTag));
                    }
                }
            }

            // 2. Gather WinRT system appointments
            if (ApiInformation.IsTypePresent("Windows.ApplicationModel.Appointments.AppointmentManager"))
            {
                var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
                if (store != null)
                {
                    var options = new FindAppointmentsOptions { MaxCount = 10, IncludeHidden = false };
                    var appointments = await store.FindAppointmentsAsync(now.AddMinutes(-5), TimeSpan.FromHours(12), options);

                    foreach (var appt in appointments)
                    {
                        if (appt.AllDay) continue;
                        var end = appt.StartTime + appt.Duration;
                        if (end < now) continue;

                        string? joinUrl = ExtractMeetingUrl(appt.Uri?.ToString())
                                          ?? ExtractMeetingUrl(appt.Location)
                                          ?? ExtractMeetingUrl(appt.Details);

                        candidates.Add(new CalendarEventInfo(
                            string.IsNullOrWhiteSpace(appt.Subject) ? "Calendar Event" : appt.Subject.Trim(),
                            appt.StartTime,
                            appt.Duration,
                            appt.Location ?? "",
                            joinUrl));
                    }
                }
            }

            var chosen = candidates.OrderBy(c => c.StartTime).FirstOrDefault();
            NextEvent = chosen;
            _ = _ui.InvokeAsync(() => Updated?.Invoke(chosen));

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

    public static bool LaunchMeeting(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[CalendarService] LaunchMeeting failed: {ex.Message}");
            return false;
        }
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
