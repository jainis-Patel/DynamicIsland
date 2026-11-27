using DynamicIsland.Services;

namespace DynamicIsland.Core;

/// <summary>
/// Automatically links Pomodoro / Focus countdown sessions to the <c>focus</c> Context Profile
/// when <see cref="AppSettings.LinkPomodoroToFocusProfile"/> is enabled, and tracks daily
/// completed focus minutes.
/// </summary>
internal sealed class PomodoroFocusCoordinator : IDisposable
{
    private readonly CountdownTimerService _timer;
    private readonly SettingsService _settings;
    private readonly ContextProfileService _profiles;
    private string? _prePomodoroProfileId;
    private int _activeSessionMinutes;
    private bool _wasRunning;
    private bool _disposed;

    public PomodoroFocusCoordinator(
        CountdownTimerService timer,
        SettingsService settings,
        ContextProfileService profiles)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

        _timer.StateChanged += OnTimerStateChanged;
        _timer.Completed += OnTimerCompleted;
    }

    private void OnTimerStateChanged()
    {
        if (_disposed) return;

        bool isRunning = _timer.IsRunning;
        bool isFocusLabel = _timer.Label.Contains("Pomodoro", StringComparison.OrdinalIgnoreCase) ||
                            _timer.Label.Contains("Focus", StringComparison.OrdinalIgnoreCase);

        if (!_wasRunning && isRunning && isFocusLabel)
        {
            _activeSessionMinutes = Math.Max(1, (int)Math.Round(_timer.TargetDuration.TotalMinutes));
            if (_settings.Current.LinkPomodoroToFocusProfile &&
                !string.Equals(_settings.Current.ActiveProfileId, "focus", StringComparison.OrdinalIgnoreCase))
            {
                _prePomodoroProfileId = _settings.Current.ActiveProfileId ?? "default";
                _profiles.ApplyProfile("focus");
            }
        }
        else if (!isRunning && _timer.TargetDuration == TimeSpan.Zero)
        {
            RestorePreviousProfileIfAny();
        }

        _wasRunning = isRunning;
    }

    private void OnTimerCompleted(string label)
    {
        if (_disposed) return;

        bool isFocusLabel = label.Contains("Pomodoro", StringComparison.OrdinalIgnoreCase) ||
                            label.Contains("Focus", StringComparison.OrdinalIgnoreCase);
        if (isFocusLabel)
        {
            int mins = _activeSessionMinutes > 0 ? _activeSessionMinutes : 25;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            _settings.Update(s =>
            {
                if (!string.Equals(s.LastFocusDateIso, today, StringComparison.Ordinal))
                {
                    s.LastFocusDateIso = today;
                    s.CompletedFocusMinutesToday = 0;
                }
                s.CompletedFocusMinutesToday += mins;
            }, nameof(AppSettings.CompletedFocusMinutesToday));
        }

        _wasRunning = false;
        _activeSessionMinutes = 0;
        RestorePreviousProfileIfAny();
    }

    private void RestorePreviousProfileIfAny()
    {
        if (_prePomodoroProfileId == null) return;
        string restoreId = _prePomodoroProfileId;
        _prePomodoroProfileId = null;
        _profiles.ApplyProfile(restoreId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.StateChanged -= OnTimerStateChanged;
        _timer.Completed -= OnTimerCompleted;
    }
}
