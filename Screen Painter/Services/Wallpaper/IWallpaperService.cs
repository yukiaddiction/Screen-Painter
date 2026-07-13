using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Wallpaper;

public interface IWallpaperService
{
    Task<bool> ApplyWallpaperAsync(string imagePath, TargetScreen targetScreen, ImageFramingConfig framingConfig);
}
