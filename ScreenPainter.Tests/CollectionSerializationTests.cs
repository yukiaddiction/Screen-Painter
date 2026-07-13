using System;
using System.Collections.Generic;
using System.Text.Json;
using Screen_Painter.Models;

namespace ScreenPainter.Tests;

public class CollectionSerializationTests
{
    private static WallpaperCollection SampleCollection() => new()
    {
        Id = "col-123",
        Name = "Landscapes",
        IsEnabled = true,
        Target = TargetScreen.Lock,
        IsTimerEnabled = true,
        IsScreenAwakeEnabled = true,
        TriggersMigrated = true,
        TimerIntervalMinutes = 30,
        IsScheduleEnabled = true,
        ScheduleStartTime = new TimeSpan(9, 0, 0),
        ScheduleEndTime = new TimeSpan(17, 30, 0),
        ScheduleDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
        Folders = new List<FolderSource>
        {
            new() { Id = "f1", Name = "Pics", PathOrUrl = "/storage/pics", Type = StorageType.Local }
        }
    };

    [Fact]
    public void RoundTrip_PreservesPersistedFields()
    {
        var original = SampleCollection();

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<WallpaperCollection>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.IsEnabled, restored.IsEnabled);
        Assert.Equal(original.Target, restored.Target);
        Assert.Equal(original.IsTimerEnabled, restored.IsTimerEnabled);
        Assert.Equal(original.IsScreenAwakeEnabled, restored.IsScreenAwakeEnabled);
        Assert.Equal(original.TriggersMigrated, restored.TriggersMigrated);
        Assert.Equal(original.TimerIntervalMinutes, restored.TimerIntervalMinutes);
        Assert.Equal(original.IsScheduleEnabled, restored.IsScheduleEnabled);
        Assert.Equal(original.ScheduleStartTime, restored.ScheduleStartTime);
        Assert.Equal(original.ScheduleEndTime, restored.ScheduleEndTime);
        Assert.Equal(original.ScheduleDays, restored.ScheduleDays);
        Assert.Single(restored.Folders);
        Assert.Equal("f1", restored.Folders[0].Id);
        Assert.Equal(StorageType.Local, restored.Folders[0].Type);
    }

    [Fact]
    public void TransientViewState_IsNotSerialized()
    {
        var collection = SampleCollection();
        collection.PreviewImagePaths = new List<string> { "/a.jpg" };
        collection.PreviewImagePath = "/a.jpg";
        collection.IsPreviewLoading = true;

        var json = JsonSerializer.Serialize(collection);

        Assert.DoesNotContain("PreviewImagePaths", json);
        Assert.DoesNotContain("PreviewImagePath", json);
        Assert.DoesNotContain("IsPreviewLoading", json);
        Assert.DoesNotContain("previewImage", json);
    }

    [Fact]
    public void LegacyJson_WithTriggerButNoNewFlags_MigratesOnDemand()
    {
        // Simulates a pre-dual-trigger collections.json entry.
        const string legacyJson = """
        {
            "id": "old-1",
            "name": "Old Collection",
            "isEnabled": true,
            "target": 2,
            "trigger": 1,
            "timerIntervalMinutes": 15
        }
        """;

        var restored = JsonSerializer.Deserialize<WallpaperCollection>(legacyJson);
        Assert.NotNull(restored);

        // Before migration the new flags default to false.
        Assert.False(restored!.IsTimerEnabled);
        Assert.False(restored.IsScreenAwakeEnabled);
        Assert.False(restored.TriggersMigrated);

        restored.MigrateTriggerIfNeeded();

        // trigger=1 => ScreenAwake
        Assert.Equal(TriggerType.ScreenAwake, restored.Trigger);
        Assert.True(restored.IsScreenAwakeEnabled);
        Assert.False(restored.IsTimerEnabled);
        Assert.True(restored.TriggersMigrated);
    }

    [Fact]
    public void DefaultCollection_RoundTrips()
    {
        var json = JsonSerializer.Serialize(new WallpaperCollection());
        var restored = JsonSerializer.Deserialize<WallpaperCollection>(json);

        Assert.NotNull(restored);
        Assert.False(string.IsNullOrEmpty(restored!.Id));
    }
}
