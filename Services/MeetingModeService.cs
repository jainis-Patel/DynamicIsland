using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Services;

/// <summary>
/// Autonomous Meeting & Call coordinator mirroring macOS Droppy:
/// 1. Surfaces a 1-click meeting join Live Activity 2 minutes before any scheduled Zoom, Teams, Meet, or Webex call.
/// 2. Displays live active call state (duration, camera/mic status) with one-click mic mute toggle.
/// 3. Zero-idle-CPU: pure event-driven listeners on CalendarService and PrivacyIndicatorService with no continuous polling.
/// </summary>
internal sealed class MeetingModeService : IDisposable
{
    public const string MeetingJoinActivityId = "meeting_join";
    public const string MeetingActiveActivityId = "meeting_active";

    private readonly Dispatcher _ui;
    private readonly CalendarService _calendar;
    private readonly PrivacyIndicatorService _privacy;
    private readonly LiveActivityHub _hub;
    private readonly DispatcherTimer _countdownTimer;
    private bool _disposed;

    public MeetingModeService(
        Dispatcher ui,
        CalendarService calendar,
        PrivacyIndicatorService privacy,
        LiveActivityHub hub)
    {
        _ui = ui;
        _calendar = calendar;
        _privacy = privacy;
        _hub = hub;

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _countdownTimer.Tick += (_, _) => CheckUpcomingMeeting();
    }

    public void Start()
    {
        if (_disposed) return;
        _calendar.Updated += OnCalendarUpdated;
        _privacy.StateChanged += OnPrivacyStateChanged;
        CheckUpcomingMeeting();
        UpdateActiveCallActivity();
    }

    private void OnCalendarUpdated(CalendarEventInfo? ev)
    {
        if (_disposed) return;
        CheckUpcomingMeeting();
    }

    private void OnPrivacyStateChanged()
    {
        if (_disposed) return;
        UpdateActiveCallActivity();
        CheckUpcomingMeeting();
    }

    public static bool ShouldShowMeetingJoin(int minutesUntilStart, string? joinUrl, bool isCallActive)
    {
        if (isCallActive) return false;
        if (string.IsNullOrWhiteSpace(joinUrl)) return false;
        // Show 2 minutes before, up to 15 minutes past start
        return minutesUntilStart <= 2 && minutesUntilStart >= -15;
    }

    public static string FormatMeetingSubtitle(int minutesUntilStart)
    {
        if (minutesUntilStart < 0) return "Meeting underway \u00B7 Click to join";
        if (minutesUntilStart == 0) return "Starting now \u00B7 Click to join";
        if (minutesUntilStart == 1) return "Starts in 1m \u00B7 Click to join";
        return $"Starts in {minutesUntilStart}m \u00B7 Click to join";
    }

    public void CheckUpcomingMeeting()
    {
        if (_disposed) return;
        var ev = _calendar.NextEvent;
        bool isCallActive = !string.IsNullOrWhiteSpace(_privacy.ActiveCallApp);

        if (ev != null && !string.IsNullOrWhiteSpace(ev.JoinUrl))
        {
            int mins = ev.MinutesUntilStart;

            // Manage countdown timer: active only when within 15 minutes of meeting
            if (mins <= 15 && mins >= -15)
            {
                if (!_countdownTimer.IsEnabled) _countdownTimer.Start();
            }
            else
            {
                if (_countdownTimer.IsEnabled) _countdownTimer.Stop();
            }

            if (ShouldShowMeetingJoin(mins, ev.JoinUrl, isCallActive))
            {
                _hub.Publish(BuildUpcomingMeetingActivity(ev, mins, _hub, _ui));
                return;
            }
        }
        else
        {
            if (_countdownTimer.IsEnabled) _countdownTimer.Stop();
        }

        // If not matching conditions, ensure join activity is cleaned up
        _hub.Complete(MeetingJoinActivityId);
    }

    internal static LiveActivityItem BuildUpcomingMeetingActivity(
        CalendarEventInfo ev,
        int mins,
        LiveActivityHub? hub,
        Dispatcher ui)
    {
        var joinAction = new LiveActivityAction("Join", "\uE717", () =>
        {
            CalendarService.LaunchMeeting(ev.JoinUrl);
            hub?.Complete(MeetingJoinActivityId);
        });

        var mirrorAction = new LiveActivityAction("Mirror", "\uE714", () =>
        {
            _ = ui.InvokeAsync(() => IslandWindow.Instance?.ExpandAndSwitchView(ExpandedView.Mirror));
        });

        return new LiveActivityItem(
            Id: MeetingJoinActivityId,
            Source: "Meeting",
            Title: ev.Subject,
            Subtitle: FormatMeetingSubtitle(mins),
            Glyph: "\uE717",
            AccentColor: Color.FromRgb(0x30, 0xD1, 0x58), // Emerald green
            Priority: 90,
            TargetViewKey: "calendar",
            Actions: new[] { joinAction, mirrorAction });
    }

    private void UpdateActiveCallActivity()
    {
        if (_disposed) return;
        string? app = _privacy.ActiveCallApp;

        if (!string.IsNullOrWhiteSpace(app))
        {
            var callSpan = _privacy.CallStartTime.HasValue
                ? DateTimeOffset.Now - _privacy.CallStartTime.Value
                : TimeSpan.Zero;
            string compactText = PrivacyIndicatorService.FormatCompactCallText(app, callSpan);

            var muteAction = new LiveActivityAction(
                Label: _privacy.IsMicrophoneMuted ? "Unmute" : "Mute",
                Glyph: _privacy.IsMicrophoneMuted ? "\uE74F" : "\uE720",
                Callback: () => _privacy.ToggleMicrophoneMute());

            _hub.Publish(new LiveActivityItem(
                Id: MeetingActiveActivityId,
                Source: "Call",
                Title: $"{app} Call",
                Subtitle: compactText,
                Glyph: "\uE717",
                AccentColor: _privacy.IsMicrophoneMuted ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x30, 0xD1, 0x58),
                Priority: 100,
                TargetViewKey: "media",
                Actions: new[] { muteAction }));
        }
        else
        {
            _hub.Complete(MeetingActiveActivityId);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _countdownTimer.Stop();
        _calendar.Updated -= OnCalendarUpdated;
        _privacy.StateChanged -= OnPrivacyStateChanged;
        _hub.Complete(MeetingJoinActivityId);
        _hub.Complete(MeetingActiveActivityId);
    }
}
