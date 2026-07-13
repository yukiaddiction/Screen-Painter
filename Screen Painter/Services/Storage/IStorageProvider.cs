using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Storage;

public interface IStorageProvider
{
    StorageType SupportedType { get; }
    Task<List<string>> ListImageIdentifiersAsync(FolderSource folderSource);
    Task<List<string>> ListSubfoldersAsync(FolderSource folderSource, string currentPath);
    Task<Stream?> DownloadImageStreamAsync(FolderSource folderSource, string imageIdentifier);
}
