using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screen_Painter.Models;

namespace Screen_Painter.Services;

public interface IFramingOverrideService
{
    Task<ImageFramingConfig?> GetOverrideAsync(string collectionId, string imageKey);
    Task SetOverrideAsync(string collectionId, string imageKey, ImageFramingConfig config);
    Task RemoveOverrideAsync(string collectionId, string imageKey);
    Task RemoveAllForCollectionAsync(string collectionId);
    Task PruneAsync(string collectionId, ISet<string> validKeys);
    Task<HashSet<string>> GetOverrideKeysAsync(string collectionId);
    Task<ImageFramingConfig> ResolveFramingAsync(WallpaperCollection collection, string? imagePath);
}

public class FramingOverrideService : JsonFileRepository, IFramingOverrideService
{
    private List<ImageFramingOverride>? _cache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public FramingOverrideService(ILoggerFactory loggerFactory)
        : base("framing_overrides.json", loggerFactory)
    {
    }

    private async Task<List<ImageFramingOverride>> GetAllAsync()
    {
        if (_cache != null)
            return _cache;

        await _cacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _cache ??= await ReadAsync<ImageFramingOverride>().ConfigureAwait(false);
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<ImageFramingConfig?> GetOverrideAsync(string collectionId, string imageKey)
    {
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(imageKey))
            return null;

        var all = await GetAllAsync().ConfigureAwait(false);
        return all.FirstOrDefault(o =>
            string.Equals(o.CollectionId, collectionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(o.ImageKey, imageKey, StringComparison.OrdinalIgnoreCase))?.Config;
    }

    public async Task SetOverrideAsync(string collectionId, string imageKey, ImageFramingConfig config)
    {
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(imageKey))
            return;

        await ReadModifyWriteAsync<ImageFramingOverride>(items =>
        {
            items.RemoveAll(o =>
                string.Equals(o.CollectionId, collectionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.ImageKey, imageKey, StringComparison.OrdinalIgnoreCase));
            items.Add(new ImageFramingOverride
            {
                CollectionId = collectionId,
                ImageKey = imageKey,
                Config = config
            });
            return items;
        }).ConfigureAwait(false);

        InvalidateCache();
    }

    public async Task RemoveOverrideAsync(string collectionId, string imageKey)
    {
        if (string.IsNullOrEmpty(collectionId) || string.IsNullOrEmpty(imageKey))
            return;

        await ReadModifyWriteAsync<ImageFramingOverride>(items =>
        {
            items.RemoveAll(o =>
                string.Equals(o.CollectionId, collectionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.ImageKey, imageKey, StringComparison.OrdinalIgnoreCase));
            return items;
        }).ConfigureAwait(false);

        InvalidateCache();
    }

    public async Task RemoveAllForCollectionAsync(string collectionId)
    {
        if (string.IsNullOrEmpty(collectionId))
            return;

        await ReadModifyWriteAsync<ImageFramingOverride>(items =>
        {
            items.RemoveAll(o => string.Equals(o.CollectionId, collectionId, StringComparison.OrdinalIgnoreCase));
            return items;
        }).ConfigureAwait(false);

        InvalidateCache();
    }

    public async Task PruneAsync(string collectionId, ISet<string> validKeys)
    {
        if (string.IsNullOrEmpty(collectionId) || validKeys == null)
            return;

        await ReadModifyWriteAsync<ImageFramingOverride>(items =>
        {
            items.RemoveAll(o =>
                string.Equals(o.CollectionId, collectionId, StringComparison.OrdinalIgnoreCase) &&
                !validKeys.Contains(o.ImageKey));
            return items;
        }).ConfigureAwait(false);

        InvalidateCache();
    }

    public async Task<HashSet<string>> GetOverrideKeysAsync(string collectionId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(collectionId))
            return result;

        var all = await GetAllAsync().ConfigureAwait(false);
        foreach (var item in all)
        {
            if (string.Equals(item.CollectionId, collectionId, StringComparison.OrdinalIgnoreCase))
                result.Add(item.ImageKey);
        }

        return result;
    }

    public async Task<ImageFramingConfig> ResolveFramingAsync(WallpaperCollection collection, string? imagePath)
    {
        if (!string.IsNullOrEmpty(imagePath))
        {
            var key = Imaging.ImageKey.ForPath(imagePath);
            var overrideConfig = await GetOverrideAsync(collection.Id, key).ConfigureAwait(false);
            if (overrideConfig != null)
                return overrideConfig;
        }

        return collection.FramingConfig ?? new ImageFramingConfig();
    }

    private void InvalidateCache()
    {
        _cache = null;
    }
}
