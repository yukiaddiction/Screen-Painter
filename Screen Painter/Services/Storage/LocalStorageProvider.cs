using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Storage;

public class LocalStorageProvider : IStorageProvider
{

    public StorageType SupportedType => StorageType.Local;

    public static string NormalizePath(string rawPath)
    {
        if (string.IsNullOrEmpty(rawPath))
            return string.Empty;

        if (rawPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var unescaped = Uri.UnescapeDataString(rawPath);
                
                // Handle primary storage: content://com.android.externalstorage.documents/tree/primary%3APictures -> /storage/emulated/0/Pictures
                int primaryIndex = unescaped.IndexOf("primary:", StringComparison.OrdinalIgnoreCase);
                if (primaryIndex >= 0)
                {
                    var relativePath = unescaped.Substring(primaryIndex + 8).TrimStart('/');
                    int documentIndex = relativePath.IndexOf("/document/primary:", StringComparison.OrdinalIgnoreCase);
                    if (documentIndex >= 0)
                    {
                        relativePath = relativePath.Substring(documentIndex + 18).TrimStart('/');
                    }
                    return Path.Combine("/storage/emulated/0", relativePath);
                }

                // Handle SD card / secondary storage
                int colonIndex = unescaped.IndexOf(":", StringComparison.OrdinalIgnoreCase);
                if (colonIndex > 0)
                {
                    int lastSlashBeforeColon = unescaped.LastIndexOf('/', colonIndex);
                    if (lastSlashBeforeColon >= 0)
                    {
                        var storageId = unescaped.Substring(lastSlashBeforeColon + 1, colonIndex - lastSlashBeforeColon - 1);
                        var subPath = unescaped.Substring(colonIndex + 1).TrimStart('/');
                        if (!string.IsNullOrEmpty(storageId) && storageId != "primary")
                        {
                            return Path.Combine("/storage", storageId, subPath);
                        }
                    }
                }
            }
            catch
            {
                // Fallback to raw path if parsing fails
            }
        }

        return rawPath;
    }

    public async Task<List<string>> ListImageIdentifiersAsync(FolderSource folderSource)
    {
        if (string.IsNullOrEmpty(folderSource.PathOrUrl))
            return new List<string>();

        var normalizedPath = NormalizePath(folderSource.PathOrUrl);

        return await Task.Run(() =>
        {
            var result = new List<string>();

            // Method 1: Standard File System Enumeration
            if (Directory.Exists(normalizedPath))
            {
                try
                {
                    var files = SafeEnumerateFilesRecursive(normalizedPath);
                    result.AddRange(files);
                }
                catch
                {
                    // Ignore access errors
                }
            }

            // Method 2: Android MediaStore / ContentResolver Query Fallback
#if ANDROID
            if (!result.Any())
            {
                try
                {
                    QueryAndroidMediaStore(normalizedPath, folderSource.PathOrUrl, result);
                }
                catch
                {
                    // Suppress
                }
            }
#endif

            return result;
        });
    }

#if ANDROID
    private static void QueryAndroidMediaStore(string targetPath, string contentUriString, List<string> resultList)
    {
        try
        {
            var context = Android.App.Application.Context;
            var projection = new[] { Android.Provider.MediaStore.Images.Media.InterfaceConsts.Data };
            
            var externalUri = Android.Provider.MediaStore.Images.Media.ExternalContentUri;
            if (externalUri == null) return;

            using var cursor = context.ContentResolver?.Query(
                externalUri,
                projection,
                null,
                null,
                null);

            if (cursor != null)
            {
                int dataColumn = cursor.GetColumnIndex(Android.Provider.MediaStore.Images.Media.InterfaceConsts.Data);
                if (dataColumn >= 0)
                {
                    while (cursor.MoveToNext())
                    {
                        var filePath = cursor.GetString(dataColumn);
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            // Match file under target directory or enumerate all valid images if direct dir match fails
                            if (string.IsNullOrEmpty(targetPath) || filePath.StartsWith(targetPath, StringComparison.OrdinalIgnoreCase))
                            {
                                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                                if (ImageExtensions.Valid.Contains(ext) && !resultList.Contains(filePath))
                                {
                                    resultList.Add(filePath);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore query errors
        }
    }
#endif

    private static List<string> SafeEnumerateFilesRecursive(string rootDir)
    {
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(rootDir);

        while (stack.Count > 0)
        {
            var currentDir = stack.Pop();
            try
            {
                var files = Directory.EnumerateFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => ImageExtensions.Valid.Contains(Path.GetExtension(f).ToLowerInvariant()));
                result.AddRange(files);

                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    stack.Push(subDir);
                }
            }
            catch
            {
                // Skip restricted directories gracefully
            }
        }

        return result;
    }

    public Task<List<string>> ListSubfoldersAsync(FolderSource folderSource, string currentPath)
    {
        var result = new List<string>();
        var targetDir = string.IsNullOrEmpty(currentPath) ? NormalizePath(folderSource.PathOrUrl) : NormalizePath(currentPath);

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            return Task.FromResult(result);

        try
        {
            var dirs = Directory.GetDirectories(targetDir).ToList();
            result.AddRange(dirs);
        }
        catch
        {
            // Ignore
        }

        return Task.FromResult(result);
    }

    public Task<Stream?> DownloadImageStreamAsync(FolderSource folderSource, string imageIdentifier)
    {
        if (File.Exists(imageIdentifier))
        {
            return Task.FromResult<Stream?>(File.OpenRead(imageIdentifier));
        }

#if ANDROID
        if (imageIdentifier.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var context = Android.App.Application.Context;
                var uri = Android.Net.Uri.Parse(imageIdentifier);
                if (uri != null && context.ContentResolver != null)
                {
                    var stream = context.ContentResolver.OpenInputStream(uri);
                    return Task.FromResult<Stream?>(stream);
                }
            }
            catch
            {
                // Fail gracefully
            }
        }
#endif

        return Task.FromResult<Stream?>(null);
    }
}
