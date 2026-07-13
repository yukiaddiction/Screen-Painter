using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Scheduling;

public class CollectionScheduler : JsonFileRepository, ICollectionScheduler
{
    private List<WallpaperCollection>? _cachedAll;
    private DateTime _cacheTimestamp = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly object _cacheLock = new();

    public CollectionScheduler(ILoggerFactory loggerFactory) : base("collections.json", loggerFactory)
    {
    }

    public async Task<List<WallpaperCollection>> GetAllCollectionsAsync()
    {
        lock (_cacheLock)
        {
            if (_cachedAll != null && DateTime.UtcNow - _cacheTimestamp < CacheTtl)
                return _cachedAll.ToList();
        }

        var result = await ReadAsync<WallpaperCollection>();
        result.RemoveAll(IsLegacyDefaultItem);
        foreach (var collection in result)
        {
            collection.MigrateTriggerIfNeeded();
        }

        lock (_cacheLock)
        {
            _cachedAll = result;
            _cacheTimestamp = DateTime.UtcNow;
        }

        return result.ToList();
    }

    public async Task<WallpaperCollection?> GetCollectionByIdAsync(string id)
    {
        var all = await GetAllCollectionsAsync();
        return all.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public Task SaveCollectionAsync(WallpaperCollection collection)
    {
        return ReadModifyWriteAsync<WallpaperCollection>(collections =>
        {
            collections.RemoveAll(IsLegacyDefaultItem);

            var index = collections.FindIndex(c => string.Equals(c.Id, collection.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                collections[index] = collection;
            else
                collections.Add(collection);

            InvalidateCache(collections);
            return collections;
        });
    }

    public Task DeleteCollectionAsync(string collectionId)
    {
        return ReadModifyWriteAsync<WallpaperCollection>(collections =>
        {
            collections.RemoveAll(c => string.Equals(c.Id, collectionId, StringComparison.OrdinalIgnoreCase));
            InvalidateCache(collections);
            return collections;
        });
    }

    public async Task<WallpaperCollection?> GetActiveCollectionForTargetAsync(TargetScreen target)
    {
        var collections = await GetAllCollectionsAsync();
        var now = DateTime.Now;
        var currentTime = now.TimeOfDay;
        var currentDay = now.DayOfWeek;

        return collections.FirstOrDefault(c =>
        {
            if (!c.IsEnabled) return false;
            if (c.Target != TargetScreen.Both && c.Target != target) return false;
            if (!c.IsScheduleEnabled) return true;

            bool dayMatches = c.ScheduleDays == null || c.ScheduleDays.Count == 0 || c.ScheduleDays.Contains(currentDay);
            return dayMatches && IsTimeWithinSchedule(currentTime, c.ScheduleStartTime, c.ScheduleEndTime);
        });
    }

    internal static bool IsTimeWithinSchedule(TimeSpan current, TimeSpan start, TimeSpan end)
    {
        return SchedulePolicy.IsTimeWithinSchedule(current, start, end);
    }

    private void InvalidateCache(List<WallpaperCollection> updated)
    {
        lock (_cacheLock)
        {
            _cachedAll = updated;
            _cacheTimestamp = DateTime.UtcNow;
        }
    }

    private static bool IsLegacyDefaultItem(WallpaperCollection c)
    {
        return string.IsNullOrEmpty(c.Id) || c.Id.StartsWith("default-", StringComparison.OrdinalIgnoreCase);
    }
}
