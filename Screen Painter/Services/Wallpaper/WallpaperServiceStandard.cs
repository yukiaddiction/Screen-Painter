using System;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Wallpaper;

public class WallpaperServiceStandard : IWallpaperService
{
    public Task<bool> ApplyWallpaperAsync(string imagePath, TargetScreen targetScreen, ImageFramingConfig framingConfig, bool skipPostApplyDelay = false)
    {
        throw new PlatformNotSupportedException(
            "Wallpaper rotation is only supported on Android. " +
            "The standard platform does not provide a wallpaper management API.");
    }
}
