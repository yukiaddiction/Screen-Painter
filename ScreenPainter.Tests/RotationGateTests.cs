using System;
using Screen_Painter.Models;
using Screen_Painter.Services.Scheduling;

namespace ScreenPainter.Tests;

public class RotationGateTests
{
    private const double Cooldown = 2.0;

    [Fact]
    public void FirstClaim_OnEmptyGate_Succeeds()
    {
        var gate = new RotationGate();
        var now = DateTime.Now;

        Assert.True(gate.TryBeginRotation("a", Cooldown, now));
    }

    [Fact]
    public void SimultaneousClaims_SameInstant_OnlyOneWins()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        bool first = gate.TryBeginRotation("collection-1", Cooldown, now);
        bool second = gate.TryBeginRotation("collection-1", Cooldown, now);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void ConcurrentClaims_ManyThreads_ExactlyOneWinsPerWindow()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        int winners = 0;

        System.Threading.Tasks.Parallel.For(0, 64, _ =>
        {
            if (gate.TryBeginRotation("shared", Cooldown, now))
                System.Threading.Interlocked.Increment(ref winners);
        });

        Assert.Equal(1, winners);
    }

    [Fact]
    public void SecondClaim_WithinCooldown_Fails()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        Assert.True(gate.TryBeginRotation("a", Cooldown, now));
        Assert.False(gate.TryBeginRotation("a", Cooldown, now.AddSeconds(1)));
    }

    [Fact]
    public void Claim_AfterCooldownElapsed_Succeeds()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        Assert.True(gate.TryBeginRotation("a", Cooldown, now));
        Assert.True(gate.TryBeginRotation("a", Cooldown, now.AddSeconds(3)));
    }

    [Fact]
    public void DifferentCollections_DoNotBlockEachOther()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        Assert.True(gate.TryBeginRotation("a", Cooldown, now));
        Assert.True(gate.TryBeginRotation("b", Cooldown, now));
    }

    [Fact]
    public void ShouldRotateTimerCollection_NoHistory_ReturnsTrue()
    {
        var gate = new RotationGate();
        var collection = new WallpaperCollection { TimerIntervalMinutes = 15 };

        Assert.True(gate.ShouldRotateTimerCollection(collection, DateTime.Now));
    }

    [Fact]
    public void ShouldRotateTimerCollection_BeforeInterval_ReturnsFalse()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var collection = new WallpaperCollection { Id = "c", TimerIntervalMinutes = 15 };
        gate.MarkRotated("c", now);

        Assert.False(gate.ShouldRotateTimerCollection(collection, now.AddMinutes(10)));
    }

    [Fact]
    public void ShouldRotateTimerCollection_AtOrAfterInterval_ReturnsTrue()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var collection = new WallpaperCollection { Id = "c", TimerIntervalMinutes = 15 };
        gate.MarkRotated("c", now);

        Assert.True(gate.ShouldRotateTimerCollection(collection, now.AddMinutes(15)));
    }

    [Fact]
    public void MarkRotated_ThenTryBegin_RespectsCooldown()
    {
        var gate = new RotationGate();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        gate.MarkRotated("c", now);

        Assert.False(gate.TryBeginRotation("c", Cooldown, now.AddSeconds(1)));
        Assert.True(gate.TryGetLastRotated("c", out var stamp));
        Assert.Equal(now, stamp);
    }
}
