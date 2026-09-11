using System.Collections.Generic;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Cache;

/// <summary>
/// A cached wallpaper file together with the stable identity of the image it came from.
///
/// <para>
/// <see cref="FilePath"/> is a throwaway local copy: cloud images are deleted from the cache
/// directory after every successful apply and re-downloaded later under the same name. Anything
/// that must persist across applies — such as cached face detections — has to key off
/// <see cref="DetectionKey"/> instead.
/// </para>
/// </summary>
public class CachedImage
{
    public CachedImage(string filePath, string detectionKey)
    {
        FilePath = filePath;
        DetectionKey = detectionKey;
    }

    /// <summary>Local file path of the cached copy, safe to decode from.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Stable identity: the remote identifier for cloud images, the file path for local ones.
    /// </summary>
    public string DetectionKey { get; }
}

public interface ICacheManager
{
    Task PreCacheCollectionAsync(WallpaperCollection collection, int targetCacheCount = 10);
    Task<string?> PopNextCachedImageAsync(WallpaperCollection collection);

    /// <summary>
    /// Same selection as <see cref="PopNextCachedImageAsync"/>, but also reports the stable
    /// detection key so a caller can cache derived analysis across delete-and-redownload cycles.
    /// </summary>
    Task<CachedImage?> PopNextCachedImageInfoAsync(WallpaperCollection collection);

    Task RefillCacheQueueAsync(WallpaperCollection collection, int targetCacheCount = 10);
    Task<List<string>> GetCachedImagesAsync(string collectionId);
    Task DeleteCachedImageAsync(WallpaperCollection collection, string filePath);
}
