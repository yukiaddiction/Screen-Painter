using System;
using Screen_Painter.Services.Scheduling;

namespace ScreenPainter.Tests;

public class SchedulePolicyTests
{
    [Theory]
    [InlineData(9, 0, true)]   // start boundary
    [InlineData(12, 0, true)]  // inside
    [InlineData(17, 0, true)]  // end boundary
    [InlineData(8, 59, false)] // just before
    [InlineData(17, 1, false)] // just after
    [InlineData(3, 0, false)]  // well outside
    public void IsTimeWithinSchedule_NormalWindow(int hour, int minute, bool expected)
    {
        var start = new TimeSpan(9, 0, 0);
        var end = new TimeSpan(17, 0, 0);

        var result = SchedulePolicy.IsTimeWithinSchedule(new TimeSpan(hour, minute, 0), start, end);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(23, 0, true)]  // after start, before midnight
    [InlineData(3, 0, true)]   // after midnight, before end
    [InlineData(6, 0, true)]   // end boundary
    [InlineData(22, 0, true)]  // start boundary
    [InlineData(12, 0, false)] // midday, outside
    [InlineData(7, 0, false)]  // just after end
    public void IsTimeWithinSchedule_OvernightWindow_WrapsMidnight(int hour, int minute, bool expected)
    {
        var start = new TimeSpan(22, 0, 0);
        var end = new TimeSpan(6, 0, 0);

        var result = SchedulePolicy.IsTimeWithinSchedule(new TimeSpan(hour, minute, 0), start, end);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldRotateTimer_NullLast_ReturnsTrue()
    {
        Assert.True(SchedulePolicy.ShouldRotateTimer(null, 15, DateTime.Now));
    }

    [Fact]
    public void ShouldRotateTimer_BeforeInterval_ReturnsFalse()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        Assert.False(SchedulePolicy.ShouldRotateTimer(now, 15, now.AddMinutes(14)));
    }

    [Fact]
    public void ShouldRotateTimer_AtInterval_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        Assert.True(SchedulePolicy.ShouldRotateTimer(now, 15, now.AddMinutes(15)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ShouldRotateTimer_NonPositiveInterval_ClampsToOneMinute(int interval)
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        Assert.False(SchedulePolicy.ShouldRotateTimer(now, interval, now.AddSeconds(59)));
        Assert.True(SchedulePolicy.ShouldRotateTimer(now, interval, now.AddMinutes(1)));
    }
}
