using System.Collections.Generic;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Scheduling;

public interface ICollectionScheduler
{
    Task<List<WallpaperCollection>> GetAllCollectionsAsync();
    Task<WallpaperCollection?> GetCollectionByIdAsync(string id);
    Task SaveCollectionAsync(WallpaperCollection collection);
    Task DeleteCollectionAsync(string collectionId);
    Task<WallpaperCollection?> GetActiveCollectionForTargetAsync(TargetScreen target);
}
