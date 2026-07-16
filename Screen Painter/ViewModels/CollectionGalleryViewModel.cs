using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Screen_Painter.Models;
using Screen_Painter.Services;
using Screen_Painter.Services.Imaging;
using Screen_Painter.Services.Scheduling;
using Screen_Painter.Services.Storage;

namespace Screen_Painter.ViewModels;

public class GalleryImageItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string Identifier { get; init; } = string.Empty;
    public string FolderId { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public System.DateTime? LastModifiedUtc { get; init; }

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set { if (_thumbnailPath != value) { _thumbnailPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasThumbnail)); OnPropertyChanged(nameof(ShowPlaceholder)); } }
    }
    private string? _thumbnailPath;

    public bool HasThumbnail => !string.IsNullOrEmpty(_thumbnailPath);

    public bool ShowPlaceholder => string.IsNullOrEmpty(_thumbnailPath);

    public bool HasOverride
    {
        get => _hasOverride;
        set { if (_hasOverride != value) { _hasOverride = value; OnPropertyChanged(); } }
    }
    private bool _hasOverride;
}

public class CollectionGalleryViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ICollectionScheduler _scheduler;
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly IThumbnailService _thumbnailService;
    private readonly IFramingOverrideService _framingOverrides;
    private readonly IGalleryManifestStore _manifestStore;

    private string _collectionId = string.Empty;
    private WallpaperCollection? _collection;
    private PagedImageEnumerator? _enumerator;
    private CancellationTokenSource? _sessionCts;
    private readonly Dictionary<string, CancellationTokenSource> _thumbJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _thumbJobsLock = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private HashSet<string> _overrideKeys = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DateTime?> _previousModified = new(StringComparer.OrdinalIgnoreCase);
    private bool _prunedThisSession;
    private int _lastFirstVisible;
    private int _lastLastVisible;

    public ObservableCollection<GalleryImageItem> Images { get; } = new();

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set => SetProperty(ref _isLoadingMore, value);
    }
    private bool _isLoadingMore;

    public bool HasMore
    {
        get => _hasMore;
        set => SetProperty(ref _hasMore, value);
    }
    private bool _hasMore;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
    private string _statusText = string.Empty;

    public string EmptyText
    {
        get => _emptyText;
        set => SetProperty(ref _emptyText, value);
    }
    private string _emptyText = "Scanning folders…";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }
    private bool _isRefreshing;

    public ICommand LoadMoreCommand { get; }
    public ICommand EditImageCommand { get; }
    public ICommand RefreshCommand { get; }

    public CollectionGalleryViewModel(
        ICollectionScheduler scheduler,
        IEnumerable<IStorageProvider> storageProviders,
        IThumbnailService thumbnailService,
        IFramingOverrideService framingOverrides,
        IGalleryManifestStore manifestStore)
    {
        _scheduler = scheduler;
        _storageProviders = storageProviders;
        _thumbnailService = thumbnailService;
        _framingOverrides = framingOverrides;
        _manifestStore = manifestStore;

        Title = "Collection Pictures";

        LoadMoreCommand = new Command(async () => await LoadMoreAsync());
        EditImageCommand = new Command<GalleryImageItem>(async (item) => await EditImageAsync(item));
        RefreshCommand = new Command(async () => await ForceRefreshAsync());
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("collectionId", out var idObj) && idObj is string id && !string.IsNullOrEmpty(id) && id != _collectionId)
        {
            _collectionId = id;
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    await InitializeAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Gallery Init Error]: {ex}");
                }
            });
        }
    }

    private int _initVersion;

    private async Task InitializeAsync()
    {
        var version = Interlocked.Increment(ref _initVersion);
        IsBusy = true;
        EmptyText = "Scanning folders…";
        try
        {
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = new CancellationTokenSource();

            _collection = await _scheduler.GetCollectionByIdAsync(_collectionId);
            if (version != Volatile.Read(ref _initVersion))
                return; // Superseded by a newer initialization
            if (_collection == null)
            {
                StatusText = "Collection not found.";
                return;
            }

            Title = $"Pictures — {_collection.Name}";
            _overrideKeys = await _framingOverrides.GetOverrideKeysAsync(_collectionId);
            if (version != Volatile.Read(ref _initVersion))
                return;
            _prunedThisSession = false;

            _enumerator = new PagedImageEnumerator(_collection, _storageProviders);

            Images.Clear();
            var manifest = await _manifestStore.LoadAsync(_collectionId);
            if (version != Volatile.Read(ref _initVersion))
                return;
            if (manifest != null)
            {
                BuildPreviousModifiedMap(manifest);

                if (_manifestStore.IsFresh(manifest))
                {
                    _enumerator.RestoreFrom(manifest);
                    foreach (var imageRef in manifest.Images)
                    {
                        Images.Add(CreateItem(imageRef));
                    }
                }
                else
                {
                    _manifestStore.Delete(_collectionId);
                }
            }

            HasMore = _enumerator.HasMore;
            UpdateStatusText();

            if (Images.Count == 0)
            {
                await LoadMoreAsync();
            }
            else
            {
                RequestThumbnailsForWindow(0, EstimateVisibleCount());
                await MaybePruneOverridesAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery Init Error]: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            EmptyText = "No pictures found. Add local or cloud folders to this collection first.";
        }
    }

    private void BuildPreviousModifiedMap(GalleryManifest manifest)
    {
        _previousModified = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        foreach (var imageRef in manifest.Images.Concat(manifest.Buffered))
        {
            if (imageRef.LastModifiedUtc.HasValue)
                _previousModified[imageRef.Identifier] = imageRef.LastModifiedUtc;
        }
    }

    private GalleryImageItem CreateItem(GalleryImageRef imageRef)
    {
        var key = ImageKey.ForPath(imageRef.Identifier);
        var thumbnailPath = _thumbnailService.GetExistingThumbnailPath(imageRef.Identifier);

        // Invalidate stale thumbnail when the source picture changed (modified & re-uploaded)
        if (thumbnailPath != null && imageRef.LastModifiedUtc.HasValue &&
            _previousModified.TryGetValue(imageRef.Identifier, out var previous) &&
            previous.HasValue && previous.Value != imageRef.LastModifiedUtc.Value)
        {
            _thumbnailService.InvalidateThumbnail(imageRef.Identifier);
            thumbnailPath = null;
            _previousModified[imageRef.Identifier] = imageRef.LastModifiedUtc;
        }

        return new GalleryImageItem
        {
            Identifier = imageRef.Identifier,
            FolderId = imageRef.FolderId,
            Key = key,
            LastModifiedUtc = imageRef.LastModifiedUtc,
            HasOverride = _overrideKeys.Contains(key),
            ThumbnailPath = thumbnailPath
        };
    }

    public async Task LoadMoreAsync()
    {
        if (_enumerator == null || _sessionCts == null || !_enumerator.HasMore || IsLoadingMore)
            return;

        if (!await _loadLock.WaitAsync(0))
            return;

        IsLoadingMore = true;
        try
        {
            var token = _sessionCts.Token;
            var batch = await Task.Run(() => _enumerator.GetNextBatchAsync(AppConstants.GalleryPageSize, token), token);

            if (token.IsCancellationRequested)
                return;

            var newItems = batch.Select(CreateItem).ToList();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var item in newItems)
                {
                    Images.Add(item);
                }
                HasMore = _enumerator.HasMore;
                UpdateStatusText();
            });

            RequestThumbnailsForWindow(_lastFirstVisible, Math.Max(_lastLastVisible, _lastFirstVisible + EstimateVisibleCount()));

            if (!HasMore)
            {
                await MaybePruneOverridesAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Session ended
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery LoadMore Error]: {ex.Message}");
        }
        finally
        {
            IsLoadingMore = false;
            _loadLock.Release();
        }
    }

    private static int EstimateVisibleCount() => Math.Max(12, AppConstants.GalleryViewportLookahead);

    public async Task ForceRefreshAsync()
    {
        if (_collection == null || _enumerator == null)
        {
            IsRefreshing = false;
            return;
        }

        try
        {
            CancelAllThumbnailJobs();

            // Keep current timestamps so modified pictures get stale thumbnails invalidated on rescan
            _previousModified = Images
                .Where(i => i.LastModifiedUtc.HasValue)
                .GroupBy(i => i.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().LastModifiedUtc, StringComparer.OrdinalIgnoreCase);

            _manifestStore.Delete(_collectionId);
            _prunedThisSession = false;
            _overrideKeys = await _framingOverrides.GetOverrideKeysAsync(_collectionId);

            _enumerator = new PagedImageEnumerator(_collection, _storageProviders);
            Images.Clear();
            HasMore = _enumerator.HasMore;
            UpdateStatusText();

            await LoadMoreAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery Refresh Error]: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task MaybePruneOverridesAsync()
    {
        if (_prunedThisSession || _enumerator == null || _enumerator.HasMore || _enumerator.HasListingFailures)
            return;

        // Conservative guard: never prune based on an empty scan result
        if (Images.Count == 0 || _overrideKeys.Count == 0)
            return;

        _prunedThisSession = true;

        try
        {
            var validKeys = new HashSet<string>(Images.Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
            await _framingOverrides.PruneAsync(_collectionId, validKeys);
            await RefreshOverridesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery Prune Error]: {ex.Message}");
        }
    }

    private void CancelAllThumbnailJobs()
    {
        lock (_thumbJobsLock)
        {
            foreach (var cts in _thumbJobs.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _thumbJobs.Clear();
        }
    }

    public void OnScrolled(int firstVisibleIndex, int lastVisibleIndex)
    {
        if (firstVisibleIndex < 0 || lastVisibleIndex < 0)
            return;

        _lastFirstVisible = firstVisibleIndex;
        _lastLastVisible = lastVisibleIndex;
        RequestThumbnailsForWindow(firstVisibleIndex, lastVisibleIndex);
    }

    private void RequestThumbnailsForWindow(int firstVisible, int lastVisible)
    {
        if (_collection == null || _sessionCts == null)
            return;

        var lookahead = AppConstants.GalleryViewportLookahead;
        var windowStart = Math.Max(0, firstVisible - (lookahead / 2));
        var windowEnd = Math.Min(Images.Count - 1, lastVisible + lookahead);

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = windowStart; i <= windowEnd && i < Images.Count; i++)
        {
            wanted.Add(Images[i].Identifier);
        }

        lock (_thumbJobsLock)
        {
            // Cancel jobs that scrolled out of the window
            var stale = _thumbJobs.Keys.Where(k => !wanted.Contains(k)).ToList();
            foreach (var key in stale)
            {
                _thumbJobs[key].Cancel();
                _thumbJobs[key].Dispose();
                _thumbJobs.Remove(key);
            }
        }

        for (int i = windowStart; i <= windowEnd && i < Images.Count; i++)
        {
            QueueThumbnail(Images[i]);
        }
    }

    private void QueueThumbnail(GalleryImageItem item)
    {
        if (item.HasThumbnail || _collection == null || _sessionCts == null)
            return;

        var folder = _collection.Folders?.FirstOrDefault(f => string.Equals(f.Id, item.FolderId, StringComparison.OrdinalIgnoreCase));
        if (folder == null)
            return;

        CancellationTokenSource jobCts;
        lock (_thumbJobsLock)
        {
            if (_thumbJobs.ContainsKey(item.Identifier))
                return;

            jobCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
            _thumbJobs[item.Identifier] = jobCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var path = await _thumbnailService.GetOrCreateThumbnailAsync(folder, item.Identifier, jobCts.Token);
                if (!string.IsNullOrEmpty(path) && !jobCts.Token.IsCancellationRequested)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try { item.ThumbnailPath = path; }
                        catch (InvalidOperationException) { }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Scrolled away — expected
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Gallery Thumb Error]: {ex.Message}");
            }
            finally
            {
                lock (_thumbJobsLock)
                {
                    if (_thumbJobs.TryGetValue(item.Identifier, out var existing) && existing == jobCts)
                    {
                        _thumbJobs.Remove(item.Identifier);
                        existing.Dispose();
                    }
                }
            }
        });
    }

    private void UpdateStatusText()
    {
        StatusText = HasMore
            ? $"{Images.Count} pictures loaded — scroll for more"
            : $"{Images.Count} pictures";
    }

    public async Task EditImageAsync(GalleryImageItem? item)
    {
        if (item == null || string.IsNullOrEmpty(_collectionId))
            return;

        var route = $"ImageEditorPage?collectionId={Uri.EscapeDataString(_collectionId)}" +
                    $"&imageKey={Uri.EscapeDataString(item.Key)}" +
                    $"&identifier={Uri.EscapeDataString(item.Identifier)}" +
                    $"&folderId={Uri.EscapeDataString(item.FolderId)}";
        if (!string.IsNullOrEmpty(item.ThumbnailPath))
        {
            route += $"&previewPath={Uri.EscapeDataString(item.ThumbnailPath)}";
        }

        await ShellHelper.GoToAsync(route);
    }

    public async Task RefreshOverridesAsync()
    {
        if (string.IsNullOrEmpty(_collectionId))
            return;

        try
        {
            _overrideKeys = await _framingOverrides.GetOverrideKeysAsync(_collectionId);
            foreach (var item in Images)
            {
                item.HasOverride = _overrideKeys.Contains(item.Key);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery RefreshOverrides Error]: {ex.Message}");
        }
    }

    public async Task SaveStateAndSuspendAsync()
    {
        try
        {
            CancelAllThumbnailJobs();

            if (_enumerator != null && !string.IsNullOrEmpty(_collectionId))
            {
                var refs = Images.Select(i => new GalleryImageRef
                {
                    FolderId = i.FolderId,
                    Identifier = i.Identifier,
                    LastModifiedUtc = i.LastModifiedUtc
                });
                var manifest = _enumerator.CaptureState(refs);
                await _manifestStore.SaveAsync(_collectionId, manifest);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery SaveState Error]: {ex.Message}");
        }
    }

    public void CancelSession()
    {
        _sessionCts?.Cancel();
    }
}
