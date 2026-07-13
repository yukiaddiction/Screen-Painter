using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Screen_Painter.Models;
using Screen_Painter.Services.Storage;

namespace Screen_Painter.Services.Cache;

public class CloudCacheManager : ICacheManager
{
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly ILogger _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _refillsInProgress = new();
    private DateTime _lastCacheSizeCheck = DateTime.MinValue;
    private static readonly TimeSpan CacheSizeCheckInterval = TimeSpan.FromMinutes(1);

    public CloudCacheManager(IEnumerable<IStorageProvider> storageProviders, ILogger<CloudCacheManager> logger)
    {
        _storageProviders = storageProviders;
        _logger = logger;
    }

    private string GetCollectionCacheDir(string collectionId)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "collection_cache", collectionId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public Task<List<string>> GetCachedImagesAsync(string collectionId)
    {
        var cacheDir = GetCollectionCacheDir(collectionId);
        if (Directory.Exists(cacheDir))
        {
            var files = Directory.GetFiles(cacheDir).ToList();
            return Task.FromResult(files);
        }
        return Task.FromResult(new List<string>());
    }

    public async Task PreCacheCollectionAsync(WallpaperCollection collection, int targetCacheCount = 10)
    {
        var cacheDir = GetCollectionCacheDir(collection.Id);
        var existingFiles = Directory.GetFiles(cacheDir);

        if (existingFiles.Length >= targetCacheCount)
            return;

        var needed = targetCacheCount - existingFiles.Length;
        _logger.LogInformation("Cache prefill — collection: {Name}, downloading: {Count} images", collection.Name, needed);
        await DownloadRandomCloudImagesAsync(collection, needed);
    }

    public async Task<string?> PopNextCachedImageAsync(WallpaperCollection collection)
    {
        var cacheDir = GetCollectionCacheDir(collection.Id);
        var allCachedFiles = Directory.GetFiles(cacheDir);
        if (allCachedFiles.Length == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await PreCacheCollectionAsync(collection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background pre-cache failed for {Id}", collection.Id);
                }
            });

            var localFolder = collection.Folders?.FirstOrDefault(f => f.Type == StorageType.Local);
            if (localFolder != null)
            {
                var provider = _storageProviders.FirstOrDefault(p => p.SupportedType == StorageType.Local);
                if (provider != null)
                {
                    var files = await provider.ListImageIdentifiersAsync(localFolder);
                    if (files != null && files.Any())
                    {
                        return files[Random.Shared.Next(files.Count)];
                    }
                }
            }

            return null;
        }

        FileInfo? oldestFile = null;
        foreach (var path in allCachedFiles)
        {
            var fi = new FileInfo(path);
            if (oldestFile == null || fi.CreationTime < oldestFile.CreationTime)
                oldestFile = fi;
        }
        return oldestFile?.FullName ?? allCachedFiles[0];
    }

    public Task RefillCacheQueueAsync(WallpaperCollection collection, int targetCacheCount = 10)
    {
        var cacheDir = GetCollectionCacheDir(collection.Id);
        var currentCount = Directory.GetFiles(cacheDir).Length;

        if (currentCount < targetCacheCount)
        {
            return DownloadRandomCloudImagesAsync(collection, targetCacheCount - currentCount);
        }

        return Task.CompletedTask;
    }

    public Task DeleteCachedImageAsync(WallpaperCollection collection, string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Cache image deleted — path: {Path}", System.IO.Path.GetFileName(filePath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cached image {Path}", filePath);
        }

        if (_refillsInProgress.TryAdd(collection.Id, true))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefillCacheQueueAsync(collection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background cache refill failed for collection {Id}", collection.Id);
                }
                finally
                {
                    _refillsInProgress.TryRemove(collection.Id, out _);
                }
            });
        }

        return Task.CompletedTask;
    }

    private async Task DownloadRandomCloudImagesAsync(WallpaperCollection collection, int count)
    {
        var cloudFolders = collection.Folders.Where(f => f.IsActive && f.Type != StorageType.Local).ToList();
        if (!cloudFolders.Any())
            return;

        var folderImages = new Dictionary<FolderSource, List<string>>();
        foreach (var folder in cloudFolders)
        {
            var provider = _storageProviders.FirstOrDefault(p => p.SupportedType == folder.Type);
            if (provider != null)
            {
                try
                {
                    var ids = await provider.ListImageIdentifiersAsync(folder).ConfigureAwait(false);
                    if (ids != null && ids.Any())
                    {
                        folderImages[folder] = ids;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to list files for folder {Name}", folder.Name);
                }
            }
        }

        if (!folderImages.Any())
            return;

        var cacheDir = GetCollectionCacheDir(collection.Id);
        var allCloudFoldersWithImages = folderImages.Keys.ToList();

        for (int i = 0; i < count; i++)
        {
            var folder = allCloudFoldersWithImages[Random.Shared.Next(allCloudFoldersWithImages.Count)];
            var identifiers = folderImages[folder];
            var provider = _storageProviders.FirstOrDefault(p => p.SupportedType == folder.Type);

            if (provider == null || !identifiers.Any()) continue;

            var chosenId = identifiers[Random.Shared.Next(identifiers.Count)];
            try
            {
                using var stream = await provider.DownloadImageStreamAsync(folder, chosenId);
                if (stream == null) continue;

                var extension = Path.GetExtension(chosenId);
                if (string.IsNullOrEmpty(extension))
                    extension = ".jpg";

                var localFileName = $"{Guid.NewGuid()}{extension}";
                var targetPath = Path.Combine(cacheDir, localFileName);

                using var localStream = File.Create(targetPath);
                await stream.CopyToAsync(localStream);

                EnforceCacheSizeLimit(cacheDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download image {Id} from WebDAV", chosenId);
            }
        }
    }

    private void EnforceCacheSizeLimit(string cacheDir)
    {
        var now = DateTime.UtcNow;
        if (now - _lastCacheSizeCheck < CacheSizeCheckInterval)
            return;
        _lastCacheSizeCheck = now;

        try
        {
            long totalSize = 0;
            var allFiles = new List<FileInfo>();
            var rootCache = Path.Combine(FileSystem.CacheDirectory, "collection_cache");

            if (Directory.Exists(rootCache))
            {
                foreach (var dir in Directory.GetDirectories(rootCache))
                {
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        var fi = new FileInfo(file);
                        totalSize += fi.Length;
                        allFiles.Add(fi);
                    }
                }
            }

            if (totalSize <= AppConstants.MaxCacheSizeBytes)
                return;

            allFiles.Sort((a, b) => a.CreationTime.CompareTo(b.CreationTime));
            int evicted = 0;
            foreach (var file in allFiles)
            {
                totalSize -= file.Length;
                file.Delete();
                evicted++;
                if (totalSize <= AppConstants.MaxCacheSizeBytes)
                    break;
            }

            _logger.LogInformation("Cache eviction: freed {Count} files ({Size}MB) to stay under limit",
                evicted, (AppConstants.MaxCacheSizeBytes - totalSize) / (1024 * 1024));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache size enforcement failed: {Message}", ex.Message);
        }
    }
}
