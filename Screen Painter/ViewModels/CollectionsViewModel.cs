using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Screen_Painter.Models;
using Screen_Painter.Services;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Scheduling;
using Screen_Painter.Services.Storage;
using Screen_Painter.Services.Wallpaper;

namespace Screen_Painter.ViewModels;

public class CollectionsViewModel : BaseViewModel
{
    private readonly ICollectionScheduler _scheduler;
    private readonly IWallpaperService _wallpaperService;
    private readonly ICacheManager _cacheManager;
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly IFramingOverrideService _framingOverrides;
    private CancellationTokenSource? _loadCts;
    private static bool _foregroundServiceStarted;
    private readonly ILogger<CollectionsViewModel> _logger;

    private const int PreviewCollageCount = 4;

    public ObservableCollection<WallpaperCollection> Collections { get; } = new();

    public ICommand LoadCollectionsCommand { get; }
    public ICommand AddCollectionCommand { get; }
    public ICommand ToggleCollectionCommand { get; }
    public ICommand DeleteCollectionCommand { get; }
    public ICommand ApplyNowCommand { get; }
    public ICommand OpenGalleryCommand { get; }

    public CollectionsViewModel(
        ICollectionScheduler scheduler,
        IWallpaperService wallpaperService,
        ICacheManager cacheManager,
        IEnumerable<IStorageProvider> storageProviders,
        IFramingOverrideService framingOverrides,
        ILogger<CollectionsViewModel> logger)
    {
        _scheduler = scheduler;
        _wallpaperService = wallpaperService;
        _cacheManager = cacheManager;
        _storageProviders = storageProviders;
        _framingOverrides = framingOverrides;
        _logger = logger;
        Title = "Wallpaper Collections";

        LoadCollectionsCommand = new Command(async () => await LoadCollectionsAsync());
        AddCollectionCommand = new Command(async () => await AddCollectionAsync());
        ToggleCollectionCommand = new Command<WallpaperCollection>(async (c) => await ToggleCollectionAsync(c));
        DeleteCollectionCommand = new Command<WallpaperCollection>(async (c) => await DeleteCollectionAsync(c));
        ApplyNowCommand = new Command<WallpaperCollection>(async (c) => await ApplyNowAsync(c));
        OpenGalleryCommand = new Command<WallpaperCollection>(async (c) => await OpenGalleryAsync(c));
    }

    public async Task EnsureRequiredPermissionsAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
            if (status != PermissionStatus.Granted)
            {
                bool answer = await ShellHelper.DisplayAlert(
                    "Permissions Required",
                    "Screen Painter needs Storage & Media access to scan your picture folders and set wallpapers automatically.",
                    "Grant Access",
                    "Later");

                if (answer)
                {
                    status = await Permissions.RequestAsync<Permissions.StorageRead>();
                }
            }

            // On Android 11+ (API 30+), check All Files Access permission
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                if (!Android.OS.Environment.IsExternalStorageManager)
                {
                    try
                    {
                        var intent = new Android.Content.Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
                        intent.SetData(Android.Net.Uri.Parse($"package:{Android.App.Application.Context.PackageName}"));
                        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                        Android.App.Application.Context.StartActivity(intent);
                    }
                    catch
                    {
                        var intent = new Android.Content.Intent(Android.Provider.Settings.ActionManageAllFilesAccessPermission);
                        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                        Android.App.Application.Context.StartActivity(intent);
                    }
                }
            }
#endif

            await EnsureBatteryOptimizationExemptionAsync();
            await EnsureExactAlarmPermissionAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScreenPainter Permission Error]: {ex.Message}");
        }
    }

    private async Task EnsureBatteryOptimizationExemptionAsync()
    {
#if ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(23))
                return;

            var context = global::Android.App.Application.Context;
            var packageName = context.PackageName;
            var powerManager = (global::Android.OS.PowerManager?)context.GetSystemService(global::Android.Content.Context.PowerService);
            if (powerManager == null || packageName == null)
                return;

            if (powerManager.IsIgnoringBatteryOptimizations(packageName))
                return;

            bool answer = await ShellHelper.DisplayAlert(
                "Keep Running in Background",
                "To reliably rotate wallpapers when the app is closed, Screen Painter needs to be exempt from battery optimization. Please allow it on the next screen.",
                "Open Settings",
                "Later");

            if (!answer)
                return;

            try
            {
#pragma warning disable CA1416
                var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
                intent.SetData(global::Android.Net.Uri.Parse($"package:{packageName}"));
#pragma warning restore CA1416
                intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
            catch
            {
                var fallback = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings);
                fallback.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                context.StartActivity(fallback);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Battery Optimization Prompt Error]: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    private async Task EnsureExactAlarmPermissionAsync()
    {
#if ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31))
                return;

            var context = global::Android.App.Application.Context;
            var alarmManager = (global::Android.App.AlarmManager?)context.GetSystemService(global::Android.Content.Context.AlarmService);
            if (alarmManager == null)
                return;

            if (alarmManager.CanScheduleExactAlarms())
                return;

            bool answer = await ShellHelper.DisplayAlert(
                "Allow Exact Alarms",
                "Screen Painter uses exact alarms to restart its background wallpaper service if Android stops it. Please allow exact alarms on the next screen.",
                "Open Settings",
                "Later");

            if (!answer)
                return;

            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Exact Alarm Prompt Error]: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    private void StartForegroundServiceIfNeeded()
    {
#if ANDROID
        if (_foregroundServiceStarted)
            return;

        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new global::Android.Content.Intent(context, typeof(Platforms.Android.WallpaperForegroundService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
            _foregroundServiceStarted = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForegroundService Error]: {ex.Message}");
        }
#endif
    }

    public void CancelPreviewLoading()
    {
        _loadCts?.Cancel();
    }

    public async Task LoadCollectionsAsync()
    {
        IsBusy = true;
        try
        {
            await EnsureRequiredPermissionsAsync();

            var list = await _scheduler.GetAllCollectionsAsync();

            if (list.Any(c => c.IsEnabled))
            {
                StartForegroundServiceIfNeeded();
            }

            Collections.Clear();
            foreach (var item in list)
            {
                item.IsPreviewLoading = true;
                Collections.Add(item);
            }

            IsBusy = false;

            // Cancel any in-flight cloud count tasks from a previous load
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            _ = Task.Run(async () =>
            {
                var tasks = list.Select(async collection =>
                {
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        var sampleImages = await ResolveRandomPreviewImagesAsync(collection, PreviewCollageCount, token);
                        if (token.IsCancellationRequested) return;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    collection.PreviewImagePaths = sampleImages;
                                    collection.PreviewImagePath = sampleImages.FirstOrDefault();
                                    collection.IsPreviewLoading = false;
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                // Activity destroyed — safe to ignore
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Scan Background Error]: {ex.Message}");
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try { collection.IsPreviewLoading = false; }
                            catch (InvalidOperationException) { }
                        });
                    }
                });

                await Task.WhenAll(tasks);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadCollectionsAsync Error]: {ex.Message}");
            IsBusy = false;
        }
    }

    private async Task<string?> ResolveRandomPreviewImageAsync(WallpaperCollection collection, CancellationToken ct = default)
    {
        var images = await ResolveRandomPreviewImagesAsync(collection, 1, ct);
        return images.FirstOrDefault();
    }

    private async Task<List<string>> ResolveRandomPreviewImagesAsync(WallpaperCollection collection, int count, CancellationToken ct = default)
    {
        if (collection.Folders == null || !collection.Folders.Any())
        {
            return new List<string>();
        }

        var allImages = new List<string>();

        foreach (var folder in collection.Folders)
        {
            if (ct.IsCancellationRequested) break;
            if (folder.Type == StorageType.Local)
            {
                var provider = _storageProviders.FirstOrDefault(p => p.SupportedType == StorageType.Local);
                if (provider != null)
                {
                    var files = await provider.ListImageIdentifiersAsync(folder);
                    allImages.AddRange(files);
                }
            }
        }

        var cachedFiles = await _cacheManager.GetCachedImagesAsync(collection.Id);
        if (cachedFiles != null && cachedFiles.Any())
        {
            allImages.AddRange(cachedFiles);
        }

        if (!allImages.Any())
        {
            return new List<string>();
        }

        return allImages
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Min(count, allImages.Count))
            .ToList();
    }

    private async Task ApplyNowAsync(WallpaperCollection? collection)
    {
        if (collection == null) return;
        IsBusy = true;
        try
        {
            await EnsureRequiredPermissionsAsync();
            var selectedImage = await ResolveRandomPreviewImageAsync(collection);
            if (!string.IsNullOrEmpty(selectedImage))
            {
                await _wallpaperService.ApplyWallpaperAsync(selectedImage, collection.Target, await _framingOverrides.ResolveFramingAsync(collection, selectedImage));
                await ShellHelper.DisplayAlert("Wallpaper Refreshed", $"Successfully applied new wallpaper from '{collection.Name}' to your device!", "OK");
            }
            else
            {
                await ShellHelper.DisplayAlert("No Images Found", $"No images found in '{collection.Name}'. Make sure local or cloud folders contain images.", "OK");
            }
        }
        catch (Exception ex)
        {
            await ShellHelper.DisplayAlert("Apply Error", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCollectionAsync()
    {
        var newId = Guid.NewGuid().ToString();
        await ShellHelper.GoToAsync($"CollectionDetailPage?id={newId}");
    }

    private async Task OpenGalleryAsync(WallpaperCollection? collection)
    {
        if (collection == null) return;
        await ShellHelper.GoToAsync($"CollectionGalleryPage?collectionId={collection.Id}");
    }

    public async Task ToggleCollectionAsync(WallpaperCollection? collection, bool? newState = null)
    {
        if (collection == null) return;

        bool targetState = newState ?? !collection.IsEnabled;

        if (targetState)
        {
            await EnsureRequiredPermissionsAsync();

            collection.IsEnabled = true;
            await _scheduler.SaveCollectionAsync(collection);
            _logger.LogInformation("Collection enabled — name: {Name}", collection.Name);

            StartForegroundServiceIfNeeded();

            _ = Task.Run(async () => await _cacheManager.PreCacheCollectionAsync(collection, 10));

            var selectedImage = await ResolveRandomPreviewImageAsync(collection);
            if (!string.IsNullOrEmpty(selectedImage))
            {
                await _wallpaperService.ApplyWallpaperAsync(selectedImage, collection.Target, await _framingOverrides.ResolveFramingAsync(collection, selectedImage));
                await ShellHelper.DisplayAlert("Collection Enabled", $"'{collection.Name}' is now active! Applied initial wallpaper to device.", "OK");
            }
            else
            {
                await ShellHelper.DisplayAlert("Collection Enabled", $"'{collection.Name}' is active! Add images to your collection folders to start rotating wallpapers.", "OK");
            }
        }
        else
        {
            collection.IsEnabled = false;
            await _scheduler.SaveCollectionAsync(collection);
            _logger.LogInformation("Collection disabled — name: {Name}", collection.Name);
        }

        await LoadCollectionsAsync();
    }

    private async Task DeleteCollectionAsync(WallpaperCollection? collection)
    {
        if (collection == null) return;
        await _scheduler.DeleteCollectionAsync(collection.Id);
        _logger.LogInformation("Collection deleted — name: {Name}", collection.Name);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Collections.Remove(collection);
        });
    }
}
