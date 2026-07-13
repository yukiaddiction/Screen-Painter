using System.Collections.Generic;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Cache;

public interface ICacheManager
{
    Task PreCacheCollectionAsync(WallpaperCollection collection, int targetCacheCount = 10);
    Task<string?> PopNextCachedImageAsync(WallpaperCollection collection);
    Task RefillCacheQueueAsync(WallpaperCollection collection, int targetCacheCount = 10);
    Task<List<string>> GetCachedImagesAsync(string collectionId);
    Task DeleteCachedImageAsync(WallpaperCollection collection, string filePath);
}
