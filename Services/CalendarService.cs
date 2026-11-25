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
    private readonly DebouncedJsonStore<List<IslandCustomEvent>> _eventsStore;
    private Choreographer? _choreographer;
    private bool _started;
    private bool _disposed;

    public CalendarEventInfo? NextEvent { get; private set; }

    public event Action<CalendarEventInfo?>? Updated;

    public CalendarService(Dispatcher ui, string? eventsFilePath = null)
    {
        _ui = ui;
        _eventsFilePath = eventsFilePath ?? AppPaths.EventsFile;
        _eventsStore = new DebouncedJsonStore<List<IslandCustomEvent>>(_eventsFilePath, () =>
        {
            lock (_eventsLock) return new List<IslandCustomEvent>(_customEvents);
        });
        LoadCustomEvents();
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => RefreshAsync().FireAndForget("Calendar.Poll"), _ui);
    }

    public void AttachChoreographer(Choreographer choreographer)
    {
        if (_disposed || choreographer == null) return;
        _choreographer = choreographer;
        if (_started)
        {
            _timer.Stop();
            _choreographer.Register("calendar_refresh", () => RefreshAsync().FireAndForget("Calendar.Chore"), (int)PollInterval.TotalSeconds, PowerSensitivity.Relaxed);
        }
    }

    public void Start()
    {
        if (_disposed) return;
        _started = true;
        if (_choreographer != null)
        {
            _timer.Stop();
            _choreographer.Register("calendar_refresh", () => RefreshAsync().FireAndForget("Calendar.Chore"), (int)PollInterval.TotalSeconds, PowerSensitivity.Relaxed);
        }
        else
        {
            _timer.Start();
        }
        RefreshAsync().FireAndForget("Calendar.Start");
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
        }

        _eventsStore.ScheduleSave();
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
            }
        }

        if (removed)
        {
            _eventsStore.ScheduleSave();
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

            // 2. Gather WinRT system appointments (isolated so WinRT unavailability does not skip custom events)
            try
            {
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
            }
            catch (Exception ex)
            {
                App.Log($"[CalendarService] WinRT AppointmentManager unavailable: {ex.Message}");
            }

            var chosen = candidates.OrderBy(c => c.StartTime).FirstOrDefault();
            NextEvent = chosen;
            _ = _ui.InvokeAsync(() => Updated?.Invoke(chosen));
        }
        catch (Exception ex)
        {
            App.Log($"[CalendarService] Refresh error: {ex.Message}");
        }
    }

    private static readonly HashSet<string> AllowedMeetingSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "msteams", "zoommtg", "slack", "webex"
    };

    private static bool TryValidateMeetingUri(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (!AllowedMeetingSchemes.Contains(parsed.Scheme)) return false;
        uri = parsed;
        return true;
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
        if (!TryValidateMeetingUri(url, out var parsed) || parsed == null)
        {
            App.Log("[CalendarService] Rejected unsafe or invalid meeting URL.");
            return false;
        }
        return SafeProcessLauncher.TryOpenUrl(parsed.AbsoluteUri);
    }

    public static bool LaunchEventOrCalendar(CalendarEventInfo? evt)
    {
        if (evt != null && !string.IsNullOrWhiteSpace(evt.JoinUrl))
        {
            return LaunchMeeting(evt.JoinUrl);
        }

        return SafeProcessLauncher.TryOpenShellUri("outlookcal:");
    }

    public void Dispose()
    {
        _disposed = true;
        _choreographer?.Unregister("calendar_refresh");
        _timer.Stop();
        _eventsStore.Dispose();
    }
}
