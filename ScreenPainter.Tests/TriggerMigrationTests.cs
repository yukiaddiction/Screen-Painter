using Screen_Painter.Models;

namespace ScreenPainter.Tests;

public class TriggerMigrationTests
{
    [Fact]
    public void Migrate_LegacyTimer_EnablesTimerOnly()
    {
        var collection = new WallpaperCollection { Trigger = TriggerType.Timer };

        collection.MigrateTriggerIfNeeded();

        Assert.True(collection.IsTimerEnabled);
        Assert.False(collection.IsScreenAwakeEnabled);
        Assert.True(collection.TriggersMigrated);
    }

    [Fact]
    public void Migrate_LegacyScreenAwake_EnablesScreenAwakeOnly()
    {
        var collection = new WallpaperCollection { Trigger = TriggerType.ScreenAwake };

        collection.MigrateTriggerIfNeeded();

        Assert.False(collection.IsTimerEnabled);
        Assert.True(collection.IsScreenAwakeEnabled);
        Assert.True(collection.TriggersMigrated);
    }

    [Fact]
    public void Migrate_WhenFlagsAlreadySet_LeavesThemUntouched()
    {
        var collection = new WallpaperCollection
        {
            Trigger = TriggerType.Timer,
            IsTimerEnabled = true,
            IsScreenAwakeEnabled = true
        };

        collection.MigrateTriggerIfNeeded();

        Assert.True(collection.IsTimerEnabled);
        Assert.True(collection.IsScreenAwakeEnabled);
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var collection = new WallpaperCollection { Trigger = TriggerType.ScreenAwake };

        collection.MigrateTriggerIfNeeded();
        // Flip a flag off, then migrate again — should NOT re-derive because already migrated.
        collection.IsScreenAwakeEnabled = false;
        collection.MigrateTriggerIfNeeded();

        Assert.False(collection.IsScreenAwakeEnabled);
        Assert.False(collection.IsTimerEnabled);
    }

    [Fact]
    public void Migrate_AlreadyMigratedWithNoFlags_DoesNotReenable()
    {
        var collection = new WallpaperCollection
        {
            Trigger = TriggerType.Timer,
            TriggersMigrated = true,
            IsTimerEnabled = false,
            IsScreenAwakeEnabled = false
        };

        collection.MigrateTriggerIfNeeded();

        Assert.False(collection.IsTimerEnabled);
        Assert.False(collection.IsScreenAwakeEnabled);
    }
}
