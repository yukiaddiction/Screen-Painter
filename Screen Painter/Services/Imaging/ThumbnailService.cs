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

namespace Screen_Painter.Services.Imaging;

public interface IThumbnailService
{
    string? GetExistingThumbnailPath(string identifier);
    void InvalidateThumbnail(string identifier);
    (int Width, int Height)? GetImageDimensions(string path);
    Task<string?> GetOrCreateThumbnailAsync(FolderSource folder, string identifier, CancellationToken ct = default);
    Task<string?> GetOrCreatePreviewAsync(FolderSource folder, string identifier, CancellationToken ct = default);
}

public class ThumbnailService : IThumbnailService
{
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly SemaphoreSlim _jobLimiter;
    private readonly Dictionary<string, DateTime> _lastSizeChecks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SizeCheckInterval = TimeSpan.FromMinutes(1);
    private readonly object _sizeCheckLock = new();

    private const string ThumbsDirName = "thumbs";
    private const string PreviewsDirName = "previews";

    public ThumbnailService(IEnumerable<IStorageProvider> storageProviders, ILogger<ThumbnailService> logger)
    {
        _storageProviders = storageProviders;
        _logger = logger;
        var maxJobs = Math.Max(1, AppConstants.GalleryMaxParallelThumbnailJobs);
        _jobLimiter = new SemaphoreSlim(maxJobs, maxJobs);
    }

    private static string GetCacheDir(string dirName)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, dirName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string GetCachePath(string dirName, string identifier)
    {
        return Path.Combine(GetCacheDir(dirName), $"{ImageKey.ForPath(identifier)}.jpg");
    }

    public string? GetExistingThumbnailPath(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;

        var path = GetCachePath(ThumbsDirName, identifier);
        return File.Exists(path) ? path : null;
    }

    public void InvalidateThumbnail(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return;

        try
        {
            var thumbPath = GetCachePath(ThumbsDirName, identifier);
            if (File.Exists(thumbPath))
                File.Delete(thumbPath);

            var previewPath = GetCachePath(PreviewsDirName, identifier);
            if (File.Exists(previewPath))
                File.Delete(previewPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate thumbnail for {Id}", identifier);
        }
    }

    public (int Width, int Height)? GetImageDimensions(string path)
    {
#if ANDROID
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            var boundsOptions = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeFile(path, boundsOptions);

            if (boundsOptions.OutWidth > 0 && boundsOptions.OutHeight > 0)
                return (boundsOptions.OutWidth, boundsOptions.OutHeight);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read image dimensions for {Path}", path);
        }

        return null;
#else
        return null;
#endif
    }

    public Task<string?> GetOrCreateThumbnailAsync(FolderSource folder, string identifier, CancellationToken ct = default)
    {
        return GetOrCreateScaledAsync(
            folder,
            identifier,
            ThumbsDirName,
            Math.Max(64, AppConstants.GalleryThumbnailMaxPixels),
            AppConstants.GalleryMaxThumbCacheSizeBytes,
            ct);
    }

    public Task<string?> GetOrCreatePreviewAsync(FolderSource folder, string identifier, CancellationToken ct = default)
    {
        // Local originals are already on disk — use them directly at full resolution, zero copies.
        if (folder.Type == StorageType.Local && File.Exists(identifier))
        {
            return Task.FromResult<string?>(identifier);
        }

        return GetOrCreateScaledAsync(
            folder,
            identifier,
            PreviewsDirName,
            Math.Max(256, AppConstants.GalleryPreviewMaxPixels),
            AppConstants.GalleryMaxPreviewCacheSizeBytes,
            ct);
    }

    private async Task<string?> GetOrCreateScaledAsync(
        FolderSource folder,
        string identifier,
        string dirName,
        int maxPixels,
        long cacheLimitBytes,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;

        var destPath = GetCachePath(dirName, identifier);
        if (File.Exists(destPath))
            return destPath;

        await _jobLimiter.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(destPath))
                return destPath;

            var provider = _storageProviders.FirstOrDefault(p => p.SupportedType == folder.Type);
            if (provider == null)
                return null;

            var tmpPath = destPath + ".tmp";
            try
            {
                using (var sourceStream = await provider.DownloadImageStreamAsync(folder, identifier).ConfigureAwait(false))
                {
                    if (sourceStream == null)
                        return null;

                    ct.ThrowIfCancellationRequested();

                    using var tmpStream = File.Create(tmpPath);
                    await sourceStream.CopyToAsync(tmpStream, ct).ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();

                if (!GenerateScaledImage(tmpPath, destPath, maxPixels))
                    return null;

                ScheduleCacheLimitEnforcement(dirName, cacheLimitBytes);
                return destPath;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                    // Ignore temp cleanup failures
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scaled image generation failed for {Id}", identifier);
            return null;
        }
        finally
        {
            _jobLimiter.Release();
        }
    }

    private bool GenerateScaledImage(string sourcePath, string destPath, int maxPixels)
    {
#if ANDROID
        var boundsOptions = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeFile(sourcePath, boundsOptions);

        if (boundsOptions.OutWidth <= 0 || boundsOptions.OutHeight <= 0)
            return false;

        int sampleSize = 1;
        int halfWidth = boundsOptions.OutWidth / 2;
        int halfHeight = boundsOptions.OutHeight / 2;
        while (halfWidth / sampleSize >= maxPixels && halfHeight / sampleSize >= maxPixels)
        {
            sampleSize *= 2;
        }

        var decodeOptions = new Android.Graphics.BitmapFactory.Options { InSampleSize = sampleSize };
        var bitmap = Android.Graphics.BitmapFactory.DecodeFile(sourcePath, decodeOptions);
        if (bitmap == null)
            return false;

        try
        {
            using var output = File.Create(destPath);
            return bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 80, output);
        }
        finally
        {
            bitmap.Recycle();
            bitmap.Dispose();
        }
#else
        File.Copy(sourcePath, destPath, overwrite: true);
        return true;
#endif
    }

    private void ScheduleCacheLimitEnforcement(string dirName, long limitBytes)
    {
        lock (_sizeCheckLock)
        {
            var now = DateTime.UtcNow;
            if (_lastSizeChecks.TryGetValue(dirName, out var last) && now - last < SizeCheckInterval)
                return;
            _lastSizeChecks[dirName] = now;
        }

        // Run eviction on a background thread so the file enumeration/deletion never
        // blocks the thumbnail job pipeline (the _jobLimiter semaphore holder).
        _ = Task.Run(() => EnforceCacheLimit(dirName, limitBytes));
    }

    private void EnforceCacheLimit(string dirName, long limitBytes)
    {
        try
        {
            var cacheDir = GetCacheDir(dirName);
            long totalSize = 0;
            var allFiles = new List<FileInfo>();

            foreach (var file in Directory.GetFiles(cacheDir))
            {
                var fi = new FileInfo(file);
                totalSize += fi.Length;
                allFiles.Add(fi);
            }

            if (totalSize <= limitBytes)
                return;

            allFiles.Sort((a, b) => a.CreationTime.CompareTo(b.CreationTime));
            int evicted = 0;
            foreach (var file in allFiles)
            {
                try
                {
                    file.Delete();
                    totalSize -= file.Length;
                    evicted++;
                }
                catch (IOException)
                {
                    // File in use by a concurrent job — skip it this round.
                }
                if (totalSize <= limitBytes)
                    break;
            }

            _logger.LogInformation("{Dir} cache eviction: removed {Count} files to stay under limit", dirName, evicted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Dir} cache enforcement failed: {Message}", dirName, ex.Message);
        }
    }
}
