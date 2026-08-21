using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Screen_Painter.Services.Storage;

public interface IGalleryManifestStore
{
    Task<GalleryManifest?> LoadAsync(string collectionId);
    bool IsFresh(GalleryManifest manifest);
    Task SaveAsync(string collectionId, GalleryManifest manifest);
    void Delete(string collectionId);
}

public class GalleryManifestStore : IGalleryManifestStore
{
    private readonly ILogger<GalleryManifestStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public GalleryManifestStore(ILogger<GalleryManifestStore> logger)
    {
        _logger = logger;
    }

    private static string GetManifestPath(string collectionId)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "gallery_manifests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{collectionId}.json");
    }

    public async Task<GalleryManifest?> LoadAsync(string collectionId)
    {
        try
        {
            var path = GetManifestPath(collectionId);
            if (!File.Exists(path))
                return null;

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GalleryManifest>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load gallery manifest for {Id}", collectionId);
            return null;
        }
    }
    public bool IsFresh(GalleryManifest manifest)
    {
        var age = DateTime.UtcNow - manifest.SavedUtc;
        return age <= TimeSpan.FromMinutes(AppConstants.GalleryManifestTtlMinutes);
    }

    public async Task SaveAsync(string collectionId, GalleryManifest manifest)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = GetManifestPath(collectionId);
            var json = JsonSerializer.Serialize(manifest, JsonOptions);

            // Atomic write: write to a temp file then replace, so a crash or power
            // loss mid-write can never corrupt the existing manifest.
            var tmpPath = path + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json).ConfigureAwait(false);
            File.Move(tmpPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save gallery manifest for {Id}", collectionId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Delete(string collectionId)
    {
        try
        {
            var path = GetManifestPath(collectionId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete gallery manifest for {Id}", collectionId);
        }
    }
}
