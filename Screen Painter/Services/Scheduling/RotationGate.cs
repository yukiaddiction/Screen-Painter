using System;
using System.Collections.Concurrent;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Scheduling;

/// <summary>
/// Platform-agnostic gate that guarantees a single logical rotation per collection
/// within a cooldown window, even when multiple triggers (timer + screen wake) fire
/// simultaneously from different threads. Time is injected so the logic is deterministic
/// and unit-testable.
/// </summary>
public class RotationGate
{
    private readonly ConcurrentDictionary<string, DateTime> _lastRotated = new();

    public int Count => _lastRotated.Count;

    /// <summary>
    /// Atomically claims a rotation window for the given collection. Returns true only if
    /// the caller wins the claim (i.e. no other thread rotated it within the cooldown).
    /// Uses ConcurrentDictionary CAS primitives so simultaneous timer + screen-wake events
    /// cannot both rotate the same collection.
    /// </summary>
    public bool TryBeginRotation(string collectionId, double cooldownSeconds, DateTime now)
    {
        while (true)
        {
            if (_lastRotated.TryGetValue(collectionId, out var last))
            {
                if ((now - last).TotalSeconds < cooldownSeconds)
                    return false;

                if (_lastRotated.TryUpdate(collectionId, now, last))
                    return true;
            }
            else if (_lastRotated.TryAdd(collectionId, now))
            {
                return true;
            }
            // Lost the race against another thread — retry with a fresh snapshot.
        }
    }

    /// <summary>
    /// Returns true when the collection's timer interval has elapsed since the last rotation
    /// (or it has never rotated).
    /// </summary>
    public bool ShouldRotateTimerCollection(WallpaperCollection collection, DateTime now)
    {
        DateTime? last = _lastRotated.TryGetValue(collection.Id, out var lastTime) ? lastTime : null;
        return SchedulePolicy.ShouldRotateTimer(last, collection.TimerIntervalMinutes, now);
    }

    public void MarkRotated(string collectionId, DateTime now) => _lastRotated[collectionId] = now;

    public bool TryGetLastRotated(string collectionId, out DateTime lastRotated)
        => _lastRotated.TryGetValue(collectionId, out lastRotated);
}
