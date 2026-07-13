#if ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Microsoft.Extensions.Logging;
using Screen_Painter.Models;
using Screen_Painter.Services;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Logging;
using Screen_Painter.Services.Scheduling;
using Screen_Painter.Services.Storage;
using Screen_Painter.Services.Wallpaper;

namespace Screen_Painter.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class ScreenAwakeReceiver : BroadcastReceiver
{
    private const double MinimumRotationCooldownSeconds = 2.0;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent == null || context == null) return;

        bool isScreenOff = intent.Action == Intent.ActionScreenOff;
        bool isUserPresent = intent.Action == Intent.ActionUserPresent;

        if (!isScreenOff && !isUserPresent) return;

        var log = GetLogger();
        log.LogInformation("Screen event: {Event}", isScreenOff ? "ScreenOff" : "UserPresent");

        var pendingResult = GoAsync();
        global::Android.OS.PowerManager.WakeLock? wakeLock = null;

        if (isScreenOff)
        {
            wakeLock = AcquireWakeLock(context, log);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessScreenEventAsync(isScreenOff, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ScreenAwakeReceiver error");
            }
            finally
            {
                ReleaseWakeLock(wakeLock, log);
                pendingResult?.Finish();
            }
        });
    }

    private static ILogger GetLogger()
    {
        var factory = ServiceAccessor.GetService<ILoggerFactory>();
        return factory != null ? factory.CreateLogger("ScreenPainter.Platform") : NullLogger.Instance;
    }

    private static global::Android.OS.PowerManager.WakeLock? AcquireWakeLock(Context context, ILogger log)
    {
        try
        {
            var powerManager = (global::Android.OS.PowerManager?)context.GetSystemService(Context.PowerService);
            var wakeLock = powerManager?.NewWakeLock(
                global::Android.OS.WakeLockFlags.Partial,
                $"ScreenPainter:WakeLock:{Guid.NewGuid()}");
            wakeLock?.Acquire(AppConstants.WakeLockTimeoutMs);
            log.LogDebug("WakeLock acquired");
            return wakeLock;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "WakeLock acquire failed");
            return null;
        }
    }

    private static void ReleaseWakeLock(global::Android.OS.PowerManager.WakeLock? wakeLock, ILogger log)
    {
        try
        {
            if (wakeLock != null && wakeLock.IsHeld)
            {
                wakeLock.Release();
                log.LogDebug("WakeLock released");
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "WakeLock release failed");
        }
    }

    private static async Task ProcessScreenEventAsync(bool isScreenOff, ILogger log)
    {
        var rotationService = ServiceAccessor.GetService<IWallpaperRotationService>();
        var scheduler = ServiceAccessor.GetService<ICollectionScheduler>();

        if (rotationService == null || scheduler == null)
        {
            log.LogWarning("Screen event skipped — services not available");
            return;
        }

        var collections = await scheduler.GetAllCollectionsAsync();

        if (isScreenOff)
        {
            await ApplyScreenAwakeWallpapersAsync(collections, rotationService, log);
        }
        else
        {
            await HandleUserPresentAsync(collections, rotationService, log);
        }
    }

    private static async Task ApplyScreenAwakeWallpapersAsync(
        List<WallpaperCollection> collections,
        IWallpaperRotationService rotationService,
        ILogger log)
    {
        var activeAwake = collections.Where(c => c.IsEnabled && c.IsScreenAwakeEnabled).ToList();
        if (!activeAwake.Any())
        {
            log.LogDebug("ScreenOff: no active ScreenAwake collections");
            return;
        }

        foreach (var collection in activeAwake)
        {
            if (!WallpaperForegroundService.TryBeginRotation(collection.Id, MinimumRotationCooldownSeconds))
            {
                log.LogInformation("ScreenAwake skipped (rotation claimed by another trigger) — collection: {Name}", collection.Name);
                continue;
            }

            log.LogInformation("ScreenAwake trigger: applying wallpaper — collection: {Name}, target: {Target}", collection.Name, collection.Target);
            await rotationService.RotateCollectionWallpaperAsync(collection, collection.Target);
        }
    }

    private static async Task HandleUserPresentAsync(
        List<WallpaperCollection> collections,
        IWallpaperRotationService rotationService,
        ILogger log)
    {
        await ApplyScreenAwakeFailSafeAsync(collections, rotationService, log);
        await ApplyDeferredTimerRotationsAsync(collections, rotationService, log);
    }

    private static async Task ApplyScreenAwakeFailSafeAsync(
        List<WallpaperCollection> collections,
        IWallpaperRotationService rotationService,
        ILogger log)
    {
        var activeAwake = collections.Where(c => c.IsEnabled && c.IsScreenAwakeEnabled).ToList();
        foreach (var collection in activeAwake)
        {
            // Only rotate if the last rotation is older than the recent-threshold window.
            // TryBeginRotation atomically claims the window so a concurrent timer tick
            // cannot also rotate the same collection.
            if (WallpaperForegroundService.TryBeginRotation(collection.Id, AppConstants.RecentRotationThresholdSeconds))
            {
                log.LogInformation("ScreenAwake fail-safe: applying wallpaper — collection: {Name}, target: {Target}",
                    collection.Name, collection.Target);
                await rotationService.RotateCollectionWallpaperAsync(collection, collection.Target);
            }
            else
            {
                log.LogDebug("ScreenAwake fail-safe: skipped (rotated recently) — collection: {Name}", collection.Name);
            }
        }
    }

    private static async Task ApplyDeferredTimerRotationsAsync(
        List<WallpaperCollection> collections,
        IWallpaperRotationService rotationService,
        ILogger log)
    {
        var activeTimer = collections.Where(c => c.IsEnabled && c.IsTimerEnabled).ToList();
        foreach (var collection in activeTimer)
        {
            if (!WallpaperForegroundService.ShouldRotateTimerCollection(collection))
                continue;

            if (!WallpaperForegroundService.TryBeginRotation(collection.Id, MinimumRotationCooldownSeconds))
                continue;

            log.LogInformation("Timer trigger on unlock: applying wallpaper — collection: {Name}, target: {Target}", collection.Name, collection.Target);
            await rotationService.RotateCollectionWallpaperAsync(collection, collection.Target);
        }
    }
}
#endif
