using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Screen_Painter.Models;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Storage;
using Screen_Painter.Services.Wallpaper;

namespace Screen_Painter.Services;

public static class WallpaperRotationHelper
{
    public static async Task RotateCollectionWallpaperAsync(
        WallpaperCollection collection,
        IWallpaperService wallpaperService,
        ICacheManager cacheManager,
        IEnumerable<IStorageProvider> storageProviders,
        TargetScreen target)
    {
        string? nextImagePath = null;
        bool isCached = false;

        nextImagePath = await cacheManager.PopNextCachedImageAsync(collection);
        if (!string.IsNullOrEmpty(nextImagePath))
        {
            isCached = true;
        }

        if (string.IsNullOrEmpty(nextImagePath) && collection.Folders != null)
        {
            var localFolder = collection.Folders.FirstOrDefault(f => f.Type == StorageType.Local);
            if (localFolder != null)
            {
                var provider = storageProviders.FirstOrDefault(p => p.SupportedType == StorageType.Local);
                if (provider != null)
                {
                    var files = await provider.ListImageIdentifiersAsync(localFolder);
                    if (files != null && files.Any())
                    {
                        nextImagePath = files[Random.Shared.Next(files.Count)];
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(nextImagePath))
        {
            var success = await wallpaperService.ApplyWallpaperAsync(nextImagePath, target, collection.FramingConfig);

            if (success && isCached)
            {
                await cacheManager.DeleteCachedImageAsync(collection, nextImagePath);
            }
        }
    }
}
