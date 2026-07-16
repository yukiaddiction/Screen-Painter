using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Screen_Painter.Models;
using Screen_Painter.Services;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Scheduling;
using Screen_Painter.Services.Security;
using Screen_Painter.Services.Storage;
using Screen_Painter.Services.Wallpaper;

namespace Screen_Painter.ViewModels;

public class CollectionDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ICollectionScheduler _scheduler;
    private readonly ISecureStorageService _secureStorage;
    private readonly ICloudAccountService _cloudAccountService;
    private readonly ICacheManager _cacheManager;
    private readonly IWallpaperService _wallpaperService;
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly IFramingOverrideService _framingOverrides;

    private string _collectionId = string.Empty;
    private WallpaperCollection _currentCollection = new();
    private bool _hasSavedCloudAccounts = false;
    private string _folderSelectionStatusMessage = string.Empty;

    public string CollectionId
    {
        get => _collectionId;
        set => SetProperty(ref _collectionId, value);
    }

    public WallpaperCollection CurrentCollection
    {
        get => _currentCollection;
        set
        {
            if (SetProperty(ref _currentCollection, value))
            {
                OnPropertyChanged(nameof(IsTimerEnabled));
                OnPropertyChanged(nameof(IsScreenAwakeEnabled));
                OnPropertyChanged(nameof(IsTimerVisible));
            }
        }
    }

    public bool IsTimerVisible => CurrentCollection?.IsTimerEnabled == true;

    public bool HasSavedCloudAccounts
    {
        get => _hasSavedCloudAccounts;
        set => SetProperty(ref _hasSavedCloudAccounts, value);
    }

    public string FolderSelectionStatusMessage
    {
        get => _folderSelectionStatusMessage;
        set
        {
            if (SetProperty(ref _folderSelectionStatusMessage, value))
            {
                OnPropertyChanged(nameof(IsFolderStatusVisible));
            }
        }
    }

    public bool IsFolderStatusVisible => !string.IsNullOrEmpty(FolderSelectionStatusMessage);

    public ObservableCollection<FolderSource> FolderSources { get; } = new();

    public TargetScreen[] TargetScreens { get; } = Enum.GetValues<TargetScreen>();

    public bool IsTimerEnabled
    {
        get => CurrentCollection.IsTimerEnabled;
        set
        {
            if (CurrentCollection.IsTimerEnabled != value)
            {
                CurrentCollection.IsTimerEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTimerVisible));
            }
        }
    }

    public bool IsScreenAwakeEnabled
    {
        get => CurrentCollection.IsScreenAwakeEnabled;
        set
        {
            if (CurrentCollection.IsScreenAwakeEnabled != value)
            {
                CurrentCollection.IsScreenAwakeEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    // Day of Week Toggles
    public bool IsSundaySelected { get => IsDaySelected(DayOfWeek.Sunday); set => SetDay(DayOfWeek.Sunday, value); }
    public bool IsMondaySelected { get => IsDaySelected(DayOfWeek.Monday); set => SetDay(DayOfWeek.Monday, value); }
    public bool IsTuesdaySelected { get => IsDaySelected(DayOfWeek.Tuesday); set => SetDay(DayOfWeek.Tuesday, value); }
    public bool IsWednesdaySelected { get => IsDaySelected(DayOfWeek.Wednesday); set => SetDay(DayOfWeek.Wednesday, value); }
    public bool IsThursdaySelected { get => IsDaySelected(DayOfWeek.Thursday); set => SetDay(DayOfWeek.Thursday, value); }
    public bool IsFridaySelected { get => IsDaySelected(DayOfWeek.Friday); set => SetDay(DayOfWeek.Friday, value); }
    public bool IsSaturdaySelected { get => IsDaySelected(DayOfWeek.Saturday); set => SetDay(DayOfWeek.Saturday, value); }

    public ICommand SaveCommand { get; }
    public ICommand ReapplyNowCommand { get; }
    public ICommand AddLocalFolderCommand { get; }
    public ICommand AddSavedCloudAccountCommand { get; }
    public ICommand GoToSettingsCommand { get; }
    public ICommand RemoveFolderCommand { get; }
    public ICommand EditFramingCommand { get; }
    public ICommand BrowsePicturesCommand { get; }

    public CollectionDetailViewModel(
        ICollectionScheduler scheduler,
        ISecureStorageService secureStorage,
        ICloudAccountService cloudAccountService,
        ICacheManager cacheManager,
        IWallpaperService wallpaperService,
        IEnumerable<IStorageProvider> storageProviders,
        IFramingOverrideService framingOverrides)
    {
        _scheduler = scheduler;
        _secureStorage = secureStorage;
        _cloudAccountService = cloudAccountService;
        _cacheManager = cacheManager;
        _wallpaperService = wallpaperService;
        _storageProviders = storageProviders;
        _framingOverrides = framingOverrides;

        Title = "Edit Collection";

        SaveCommand = new Command(async () => await SaveAsync());
        ReapplyNowCommand = new Command(async () => await ReapplyNowAsync());
        AddLocalFolderCommand = new Command<string>(async (path) => await AddLocalFolderAsync(path));
        AddSavedCloudAccountCommand = new Command(async () => await AddSavedCloudAccountAsync());
        GoToSettingsCommand = new Command(async () => await GoToSettingsAsync());
        RemoveFolderCommand = new Command<FolderSource>(async (f) => await RemoveFolderAsync(f));
        EditFramingCommand = new Command(async () => await EditFramingAsync());
        BrowsePicturesCommand = new Command(async () => await BrowsePicturesAsync());
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idObj) && idObj is string id && id != _collectionId)
        {
            _collectionId = id;
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    await LoadCollectionAsync(id);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CollectionDetail Load Error]: {ex}");
                }
            });
        }

        if (query.TryGetValue("SelectedFolderSource", out var folderObj) && folderObj is FolderSource folder)
        {
            if (folder != null)
            {
                if (!FolderSources.Any(f => string.Equals(f.PathOrUrl, folder.PathOrUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    FolderSources.Add(folder);
                    CurrentCollection.Folders = FolderSources.ToList();
                    _ = SaveCollectionSafeAsync();

                    FolderSelectionStatusMessage = $"✓ Cloud Folder Added: {folder.Name}";
                }
            }
        }
    }

    private async Task SaveCollectionSafeAsync()
    {
        try
        {
            await _scheduler.SaveCollectionAsync(CurrentCollection);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectionDetail Save Error]: {ex}");
        }
    }

    private void SetDay(DayOfWeek day, bool value) { ToggleDay(day, value); OnPropertyChanged(); }

    private bool IsDaySelected(DayOfWeek day)
    {
        return CurrentCollection.ScheduleDays != null && CurrentCollection.ScheduleDays.Contains(day);
    }

    private void ToggleDay(DayOfWeek day, bool isSelected)
    {
        CurrentCollection.ScheduleDays ??= new List<DayOfWeek>();
        if (isSelected)
        {
            if (!CurrentCollection.ScheduleDays.Contains(day))
                CurrentCollection.ScheduleDays.Add(day);
        }
        else
        {
            CurrentCollection.ScheduleDays.Remove(day);
        }
    }

    public async Task CheckCloudAccountsStatusAsync()
    {
        var accounts = await _cloudAccountService.GetAllAccountsAsync();
        HasSavedCloudAccounts = accounts.Any();
    }

    private async Task LoadCollectionAsync(string id)
    {
        await CheckCloudAccountsStatusAsync();

        var list = await _scheduler.GetAllCollectionsAsync();
        var match = list.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            CurrentCollection = match;
        }
        else
        {
            CurrentCollection = new WallpaperCollection
            {
                Id = id,
                Name = "My Collection",
                IsEnabled = true,
                Target = TargetScreen.Both,
                IsTimerEnabled = true,
                IsScreenAwakeEnabled = false,
                TriggersMigrated = true,
                TimerIntervalMinutes = 15,
                IsScheduleEnabled = false,
                ScheduleDays = new List<DayOfWeek>(),
                Folders = new List<FolderSource>()
            };
        }

        FolderSources.Clear();
        foreach (var folder in CurrentCollection.Folders)
        {
            FolderSources.Add(folder);
        }
        RefreshDayProperties();
        OnPropertyChanged(nameof(IsTimerEnabled));
        OnPropertyChanged(nameof(IsScreenAwakeEnabled));
        OnPropertyChanged(nameof(IsTimerVisible));
    }

    private void RefreshDayProperties()
    {
        foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
        {
            OnPropertyChanged($"Is{day}Selected");
        }
    }

    private async Task RefreshFramingFromStoreAsync()
    {
        // The framing editor persists directly to storage; re-fetch so this page's
        // stale in-memory copy never overwrites framing saved from the editor.
        try
        {
            var latest = await _scheduler.GetCollectionByIdAsync(CurrentCollection.Id);
            if (latest?.FramingConfig != null)
            {
                CurrentCollection.FramingConfig = latest.FramingConfig;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RefreshFraming Error]: {ex}");
        }
    }

    private async Task SaveAsync()
    {
        if (!CurrentCollection.IsTimerEnabled && !CurrentCollection.IsScreenAwakeEnabled)
        {
            await ShellHelper.DisplayAlert(
                "Enable a Trigger",
                "Enable at least one trigger: Timer and/or Screen Wake.",
                "OK");
            return;
        }

        if (CurrentCollection.TimerIntervalMinutes < 1)
            CurrentCollection.TimerIntervalMinutes = 1;

        CurrentCollection.TriggersMigrated = true;
        CurrentCollection.Folders = FolderSources.ToList();
        await RefreshFramingFromStoreAsync();
        await _scheduler.SaveCollectionAsync(CurrentCollection);
        var collectionToCache = CurrentCollection;
        _ = Task.Run(async () =>
        {
            try
            {
                await _cacheManager.PreCacheCollectionAsync(collectionToCache, 10);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreCache Error]: {ex}");
            }
        });
        await ShellHelper.GoToAsync("..");
    }

    private async Task ReapplyNowAsync()
    {
        IsBusy = true;
        try
        {
            if (CurrentCollection.TimerIntervalMinutes < 1)
                CurrentCollection.TimerIntervalMinutes = 1;

            CurrentCollection.Folders = FolderSources.ToList();
            await RefreshFramingFromStoreAsync();
            await _scheduler.SaveCollectionAsync(CurrentCollection);
            await _cacheManager.PreCacheCollectionAsync(CurrentCollection, 1);

            string? selectedImage = null;

            foreach (var folder in CurrentCollection.Folders)
            {
                var provider = _storageProviders.FirstOrDefault(p => p.SupportedType == folder.Type);
                if (provider != null)
                {
                    if (folder.Type == StorageType.Local)
                    {
                        var files = await provider.ListImageIdentifiersAsync(folder);
                        if (files.Any())
                        {
                        selectedImage = files[Random.Shared.Next(files.Count)];
                            break;
                        }
                    }
                    else
                    {
                        var cached = await _cacheManager.PopNextCachedImageAsync(CurrentCollection);
                        if (!string.IsNullOrEmpty(cached))
                        {
                            selectedImage = cached;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(selectedImage))
            {
                await _wallpaperService.ApplyWallpaperAsync(selectedImage, CurrentCollection.Target, await _framingOverrides.ResolveFramingAsync(CurrentCollection, selectedImage));
                await ShellHelper.DisplayAlert("Wallpaper Refreshed", $"Applied new wallpaper from '{CurrentCollection.Name}' to your device!", "OK");
            }
            else
            {
                await ShellHelper.DisplayAlert("No Images Found", "No images found in configured folders to apply.", "OK");
            }
        }
        catch (Exception ex)
        {
            await ShellHelper.DisplayAlert("Re-apply Error", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddLocalFolderAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var folder = new FolderSource
        {
            Name = System.IO.Path.GetFileName(path),
            PathOrUrl = path,
            Type = StorageType.Local
        };
        FolderSources.Add(folder);
        CurrentCollection.Folders = FolderSources.ToList();
        await _scheduler.SaveCollectionAsync(CurrentCollection);

        System.Diagnostics.Debug.WriteLine($"[CollectionDetail] Local folder added: {folder.Name} — path: {folder.PathOrUrl}");

        FolderSelectionStatusMessage = $"✓ Local Folder Added: {folder.Name}";
    }

    private async Task AddSavedCloudAccountAsync()
    {
        var accounts = await _cloudAccountService.GetAllAccountsAsync();
        if (!accounts.Any())
        {
            await GoToSettingsAsync();
            return;
        }

        string[] names = accounts.Select(a => $"{a.Name} ({a.Type})").ToArray();
        string? choice = await ShellHelper.DisplayActionSheet("Select Saved Cloud Account", "Cancel", null, names);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        var selected = accounts.FirstOrDefault(a => $"{a.Name} ({a.Type})" == choice);
        if (selected == null) return;

        await ShellHelper.GoToAsync($"CloudFolderPickerPage?accountId={selected.Id}&serverUrl={Uri.EscapeDataString(selected.ServerUrl)}&type={selected.Type}&userKey={selected.EncryptedUsername}&passKey={selected.EncryptedPasswordOrToken}");
    }

    private async Task GoToSettingsAsync()
    {
        await ShellHelper.GoToAsync("//SettingsPage");
    }

    private async Task RemoveFolderAsync(FolderSource? folder)
    {
        if (folder == null) return;
        try
        {
            FolderSources.Remove(folder);
            CurrentCollection.Folders = FolderSources.ToList();
            await _scheduler.SaveCollectionAsync(CurrentCollection);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoveFolder Error]: {ex}");
        }
    }

    private async Task EditFramingAsync()
    {
        await ShellHelper.GoToAsync($"ImageEditorPage?collectionId={CurrentCollection.Id}");
    }

    private async Task BrowsePicturesAsync()
    {
        await ShellHelper.GoToAsync($"CollectionGalleryPage?collectionId={CurrentCollection.Id}");
    }
}
