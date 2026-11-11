using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

public partial class IslandWindow
{
    // =================================================================
    //  Live Weather & Upcoming Calendar Agenda Glance
    // =================================================================

    private void OnWeatherUpdated(WeatherSnapshot snap)
    {
        if (ClockWeatherBadge == null) return;
        if (snap.IsAvailable)
        {
            ClockWeatherGlyph.Text = snap.Glyph;
            ClockWeatherText.Text = snap.BadgeText;
            ClockWeatherBadge.ToolTip = $"{snap.LocationName} \u00B7 {snap.Condition} \u00B7 {snap.SummaryLine}";
            ClockWeatherBadge.Visibility = Visibility.Visible;
        }
    }

    internal async void ClockWeatherBadge_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await _weatherService.RefreshAsync();
        var snap = _weatherService.Current;
        if (snap.IsAvailable)
        {
            _machine.PushToast(new ToastContent(
                snap.Glyph,
                $"{snap.LocationName} \u00B7 {snap.TempC}\u00B0C",
                $"{snap.Condition} \u00B7 {snap.SummaryLine}",
                ThemeBrushes.AmberColor,
                TimeSpan.FromSeconds(2.5),
                TargetView: null,
                Priority: ToastPriority.Feedback,
                Category: "weather"));
        }
    }

    private void OnCalendarUpdated(CalendarEventInfo? evt)
    {
        if (ClockCalendarBadge == null) return;
        if (evt != null)
        {
            ClockCalendarText.Text = evt.TimeBadgeText;
            ClockCalendarBadge.ToolTip = string.IsNullOrWhiteSpace(evt.Location)
                ? evt.Subject
                : $"{evt.Subject} \u00B7 {evt.Location}";
            ClockCalendarBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ClockCalendarBadge.Visibility = Visibility.Collapsed;
        }
    }

    internal void ClockCalendarBadge_Click(object sender, MouseButtonEventArgs e)
    {
        CalendarService.LaunchEventOrCalendar(_calendarService.NextEvent);
        e.Handled = true;
    }

}
