using System;

namespace Screen_Painter.Services.Scheduling;

/// <summary>
/// Pure, platform-agnostic scheduling rules. No MAUI / Android dependencies so the logic
/// can be unit-tested deterministically.
/// </summary>
public static class SchedulePolicy
{
    /// <summary>
    /// Returns true when <paramref name="current"/> falls inside the [start, end] window.
    /// Supports overnight windows that wrap past midnight (e.g. 22:00–06:00).
    /// </summary>
    public static bool IsTimeWithinSchedule(TimeSpan current, TimeSpan start, TimeSpan end)
    {
        return start <= end ? current >= start && current <= end : current >= start || current <= end;
    }

    /// <summary>
    /// Returns true when the timer interval has elapsed since <paramref name="last"/>,
    /// or when there is no prior rotation. Interval is clamped to a minimum of 1 minute.
    /// </summary>
    public static bool ShouldRotateTimer(DateTime? last, int intervalMinutes, DateTime now)
    {
        if (last is null)
            return true;

        int interval = intervalMinutes < 1 ? 1 : intervalMinutes;
        return now - last.Value >= TimeSpan.FromMinutes(interval);
    }
}
