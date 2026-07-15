using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screen_Painter.Models;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Storage;
using Screen_Painter.Services.Wallpaper;

namespace Screen_Painter.Services;

public interface IWallpaperRotationService
{
    Task RotateCollectionWallpaperAsync(
        WallpaperCollection collection,
        TargetScreen target,
        bool fastApply = false);
}

public class WallpaperRotationService : IWallpaperRotationService
{
    private readonly IWallpaperService _wallpaperService;
    private readonly ICacheManager _cacheManager;
    private readonly IStorageProviderResolver _providerResolver;
    private readonly IFramingOverrideService _framingOverrides;
    private readonly ILogger<WallpaperRotationService> _logger;

    public WallpaperRotationService(
        IWallpaperService wallpaperService,
        ICacheManager cacheManager,
        IStorageProviderResolver providerResolver,
        IFramingOverrideService framingOverrides,
        ILogger<WallpaperRotationService> logger)
    {
        _wallpaperService = wallpaperService;
        _cacheManager = cacheManager;
        _providerResolver = providerResolver;
        _framingOverrides = framingOverrides;
        _logger = logger;
    }

    public async Task RotateCollectionWallpaperAsync(
        WallpaperCollection collection,
        TargetScreen target,
        bool fastApply = false)
    {
        string? nextImagePath = null;
        bool isCached = false;

        nextImagePath = await _cacheManager.PopNextCachedImageAsync(collection);
        if (!string.IsNullOrEmpty(nextImagePath))
        {
            isCached = true;
        }

        if (string.IsNullOrEmpty(nextImagePath) && collection.Folders != null)
        {
            var localProvider = _providerResolver.ResolveLocal();
            if (localProvider != null)
            {
                foreach (var folder in collection.Folders)
                {
                    if (folder.Type != StorageType.Local) continue;
                    var files = await localProvider.ListImageIdentifiersAsync(folder);
                    if (files != null && files.Any())
                    {
                        nextImagePath = files[Random.Shared.Next(files.Count)];
                        break;
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(nextImagePath))
        {
            var source = isCached ? "cache" : "local";
            _logger.LogInformation("Wallpaper rotation — collection: {Name}, target: {Target}, source: {Source}, image: {Path}",
                collection.Name, target, source, System.IO.Path.GetFileName(nextImagePath));

            var framing = await _framingOverrides.ResolveFramingAsync(collection, nextImagePath);
            var success = await _wallpaperService.ApplyWallpaperAsync(nextImagePath, target, framing, fastApply);

            if (success && isCached)
            {
                await _cacheManager.DeleteCachedImageAsync(collection, nextImagePath);
            }
            else if (!success)
            {
                _logger.LogWarning("Wallpaper apply failed — collection: {Name}, target: {Target}", collection.Name, target);
            }
        }
        else
        {
            _logger.LogWarning("No image found for rotation — collection: {Name}, target: {Target}", collection.Name, target);
        }
    }
}
