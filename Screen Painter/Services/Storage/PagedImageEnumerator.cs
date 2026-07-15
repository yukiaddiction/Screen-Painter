using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Storage;

public class GalleryImageRef
{
    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [JsonPropertyName("lastModifiedUtc")]
    public DateTime? LastModifiedUtc { get; set; }
}

public class PendingDirectory
{
    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("depth")]
    public int Depth { get; set; }
}

public class GalleryManifest
{
    [JsonPropertyName("savedUtc")]
    public DateTime SavedUtc { get; set; }

    [JsonPropertyName("images")]
    public List<GalleryImageRef> Images { get; set; } = new();

    [JsonPropertyName("buffered")]
    public List<GalleryImageRef> Buffered { get; set; } = new();

    [JsonPropertyName("pendingDirs")]
    public List<PendingDirectory> PendingDirs { get; set; } = new();

    [JsonPropertyName("pendingFolderIds")]
    public List<string> PendingFolderIds { get; set; } = new();

    [JsonPropertyName("visitedDirs")]
    public List<string> VisitedDirs { get; set; } = new();

    [JsonPropertyName("hadFailures")]
    public bool HadFailures { get; set; }

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }
}

public class PagedImageEnumerator
{
    private readonly WallpaperCollection _collection;
    private readonly IEnumerable<IStorageProvider> _providers;

    private readonly Queue<FolderSource> _pendingFolders = new();
    private readonly Queue<PendingDirectory> _pendingDirs = new();
    private readonly Queue<GalleryImageRef> _buffer = new();
    private readonly HashSet<string> _seenIdentifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visitedDirs = new(StringComparer.OrdinalIgnoreCase);

    public PagedImageEnumerator(WallpaperCollection collection, IEnumerable<IStorageProvider> providers)
    {
        _collection = collection;
        _providers = providers;

        foreach (var folder in collection.Folders?.Where(f => f.IsActive) ?? Enumerable.Empty<FolderSource>())
        {
            _pendingFolders.Enqueue(folder);
        }
    }

    public bool HasMore => _buffer.Count > 0 || _pendingDirs.Count > 0 || _pendingFolders.Count > 0;

    public bool HasListingFailures { get; private set; }

    public void RestoreFrom(GalleryManifest manifest)
    {
        _pendingFolders.Clear();
        _pendingDirs.Clear();
        _buffer.Clear();
        _seenIdentifiers.Clear();
        _visitedDirs.Clear();

        var folderById = (_collection.Folders ?? new List<FolderSource>())
            .Where(f => f.IsActive)
            .ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var folderId in manifest.PendingFolderIds)
        {
            if (folderById.TryGetValue(folderId, out var folder))
                _pendingFolders.Enqueue(folder);
        }

        foreach (var dir in manifest.PendingDirs)
        {
            if (folderById.ContainsKey(dir.FolderId))
                _pendingDirs.Enqueue(dir);
        }

        foreach (var img in manifest.Images)
        {
            _seenIdentifiers.Add(img.Identifier);
        }

        foreach (var img in manifest.Buffered)
        {
            if (_seenIdentifiers.Add(img.Identifier) && folderById.ContainsKey(img.FolderId))
                _buffer.Enqueue(img);
        }

        foreach (var url in manifest.VisitedDirs)
        {
            _visitedDirs.Add(url);
        }

        HasListingFailures = manifest.HadFailures;
    }

    public GalleryManifest CaptureState(IEnumerable<GalleryImageRef> returnedImages)
    {
        return new GalleryManifest
        {
            SavedUtc = DateTime.UtcNow,
            Images = returnedImages.ToList(),
            Buffered = _buffer.ToList(),
            PendingDirs = _pendingDirs.ToList(),
            PendingFolderIds = _pendingFolders.Select(f => f.Id).ToList(),
            VisitedDirs = _visitedDirs.ToList(),
            HadFailures = HasListingFailures,
            IsComplete = !HasMore
        };
    }

    public async Task<List<GalleryImageRef>> GetNextBatchAsync(int batchSize, CancellationToken ct = default)
    {
        var batch = new List<GalleryImageRef>();

        while (batch.Count < batchSize && HasMore)
        {
            ct.ThrowIfCancellationRequested();

            if (_buffer.Count > 0)
            {
                batch.Add(_buffer.Dequeue());
                continue;
            }

            if (_pendingDirs.Count > 0)
            {
                await ScanNextDirectoryAsync(ct).ConfigureAwait(false);
                continue;
            }

            if (_pendingFolders.Count > 0)
            {
                await StartNextFolderAsync(ct).ConfigureAwait(false);
            }
        }

        return batch;
    }

    private async Task StartNextFolderAsync(CancellationToken ct)
    {
        var folder = _pendingFolders.Dequeue();

        if (folder.Type == StorageType.WebDav)
        {
            var rootUrl = folder.PathOrUrl;
            if (!string.IsNullOrEmpty(rootUrl))
            {
                _pendingDirs.Enqueue(new PendingDirectory { FolderId = folder.Id, Url = rootUrl, Depth = 0 });
            }
            return;
        }

        var provider = _providers.FirstOrDefault(p => p.SupportedType == folder.Type);
        if (provider == null)
        {
            HasListingFailures = true;
            return;
        }

        try
        {
            var identifiers = await provider.ListImageIdentifiersAsync(folder).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            foreach (var id in identifiers)
            {
                if (_seenIdentifiers.Add(id))
                {
                    _buffer.Enqueue(new GalleryImageRef
                    {
                        FolderId = folder.Id,
                        Identifier = id,
                        LastModifiedUtc = GetLocalLastModifiedUtc(folder, id)
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Skip folders that fail to list
            HasListingFailures = true;
        }
    }

    private static DateTime? GetLocalLastModifiedUtc(FolderSource folder, string identifier)
    {
        if (folder.Type != StorageType.Local)
            return null;

        try
        {
            if (System.IO.File.Exists(identifier))
                return System.IO.File.GetLastWriteTimeUtc(identifier);
        }
        catch
        {
            // Timestamp unavailable (e.g. content:// URI)
        }

        return null;
    }

    private async Task ScanNextDirectoryAsync(CancellationToken ct)
    {
        var dir = _pendingDirs.Dequeue();

        if (dir.Depth > AppConstants.MaxWebDavRecursionDepth)
            return;

        if (!_visitedDirs.Add(dir.Url))
            return;

        var folder = _collection.Folders?.FirstOrDefault(f => string.Equals(f.Id, dir.FolderId, StringComparison.OrdinalIgnoreCase));
        if (folder == null)
        {
            HasListingFailures = true;
            return;
        }

        var webDavProvider = _providers.OfType<WebDavStorageProvider>().FirstOrDefault();
        if (webDavProvider == null)
        {
            HasListingFailures = true;
            return;
        }

        var listing = await webDavProvider.ListDirectoryEntriesAsync(folder, dir.Url, ct).ConfigureAwait(false);

        if (!listing.Success)
        {
            HasListingFailures = true;
        }

        foreach (var image in listing.Images)
        {
            if (_seenIdentifiers.Add(image.Url))
            {
                _buffer.Enqueue(new GalleryImageRef
                {
                    FolderId = folder.Id,
                    Identifier = image.Url,
                    LastModifiedUtc = image.LastModifiedUtc
                });
            }
        }

        foreach (var subfolder in listing.Subfolders)
        {
            if (!_visitedDirs.Contains(subfolder))
            {
                _pendingDirs.Enqueue(new PendingDirectory { FolderId = folder.Id, Url = subfolder, Depth = dir.Depth + 1 });
            }
        }
    }
}
