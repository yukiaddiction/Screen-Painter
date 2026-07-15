#if ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Screen_Painter.Services;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using Screen_Painter.Models;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Logging;
using Screen_Painter.Services.Scheduling;
using Screen_Painter.Services.Storage;
using Screen_Painter.Services.Wallpaper;

namespace Screen_Painter.Platforms.Android;

[Service(Name = "com.yukiaddiction.screenpainter.WallpaperForegroundService", ForegroundServiceType = ForegroundService.TypeDataSync)]
public class WallpaperForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "screen_painter_service_channel";
    private const double MinimumRotationCooldownSeconds = 2.0;
    private ScreenAwakeReceiver? _awakeReceiver;
    private CancellationTokenSource? _timerCts;
    private int _evaluationInProgress;
    private int _screenEventInProgress;
    private int _evaluationSkipCount;
    private DateTime _lastEvaluationSkipLog = DateTime.MinValue;
    private DateTime _lastNoActiveCollectionsLog = DateTime.MinValue;
    private DateTime _lastIntervalWaitingLog = DateTime.MinValue;
    private static WallpaperForegroundService? _instance;
    public static readonly Screen_Painter.Services.Scheduling.RotationGate Gate = new();

    public override IBinder? OnBind(Intent? intent) => null;

    private ILogger GetLogger()
    {
        var factory = ServiceAccessor.GetService<ILoggerFactory>();
        return factory != null ? factory.CreateLogger("ScreenPainter.Platform") : NullLogger.Instance;
    }

    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
        CreateNotificationChannel();
        RegisterAwakeReceiver();
    }

    [Register("onStartCommand", "(Landroid/content/Intent;II)I", "GetOnStartCommand_Landroid_content_Intent_IIHandler")]
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        if (_timerCts != null && !_timerCts.IsCancellationRequested)
        {
            GetLogger().LogDebug("Foreground service already running — skipping reinit");
            AlarmReceiver.Schedule(this);
            return StartCommandResult.Sticky;
        }

        GetLogger().LogInformation("Foreground service started");

        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = new CancellationTokenSource();
        _ = RunBackgroundTimerLoopAsync(_timerCts.Token);

        AlarmReceiver.Schedule(this);

        return StartCommandResult.Sticky;
    }

    private async Task RunBackgroundTimerLoopAsync(CancellationToken token)
    {
        int tickCount = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await EvaluateTimerRotationsAsync();
            }
            catch (Exception ex)
            {
                GetLogger().LogError(ex, "Timer loop error");
            }

            tickCount++;
            if (tickCount % 60 == 0)
            {
                GetLogger().LogInformation("Polling heartbeat — {Minutes} minutes elapsed, {Rotations} total rotations",
                    tickCount * AppConstants.ForegroundServicePollingIntervalSeconds / 60,
                    Gate.Count);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AppConstants.ForegroundServicePollingIntervalSeconds), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task EvaluateTimerRotationsAsync()
    {
        if (Interlocked.Exchange(ref _evaluationInProgress, 1) == 1)
        {
            var skipped = Interlocked.Increment(ref _evaluationSkipCount);
            var now = DateTime.UtcNow;
            if ((now - _lastEvaluationSkipLog).TotalMinutes >= 5)
            {
                GetLogger().LogWarning("Timer eval — previous evaluation still in progress ({Skips} skips over {Minutes:F0} min)",
                    skipped, (now - _lastEvaluationSkipLog).TotalMinutes);
                Interlocked.Exchange(ref _evaluationSkipCount, 0);
                _lastEvaluationSkipLog = now;
            }
            return;
        }

        var log = GetLogger();
        try
        {
            var rotationService = ServiceAccessor.GetService<IWallpaperRotationService>();
            var scheduler = ServiceAccessor.GetService<ICollectionScheduler>();

            if (rotationService == null || scheduler == null)
            {
                log.LogWarning("Timer eval skipped — services not available");
                return;
            }

            bool isKeyguardLocked = IsKeyguardLocked();

            var collections = await scheduler.GetAllCollectionsAsync();
            var activeTimerCollections = collections.Where(c => c.IsEnabled && c.IsTimerEnabled).ToList();

            if (!activeTimerCollections.Any())
            {
                var now = DateTime.UtcNow;
                if ((now - _lastNoActiveCollectionsLog).TotalMinutes >= 5)
                {
                    log.LogWarning("Timer eval — no active timer collections (total collections: {Total}, enabled: {Enabled}, timer: {Timer})",
                        collections.Count, collections.Count(c => c.IsEnabled), collections.Count(c => c.IsTimerEnabled));
                    _lastNoActiveCollectionsLog = now;
                }
                return;
            }

            int rotated = 0;
            int deferred = 0;
            int cooldownSkip = 0;

            foreach (var collection in activeTimerCollections)
            {
                if (!ShouldRotateTimerCollection(collection))
                    continue;

                if (collection.Target == TargetScreen.Both || collection.Target == TargetScreen.Home)
                {
                    if (isKeyguardLocked)
                    {
                        log.LogDebug("Rotation deferred (keyguard locked) — collection: {Name}, target: {Target}",
                            collection.Name, collection.Target);
                        deferred++;
                        continue;
                    }
                }

                // Atomically claim the rotation window; a simultaneous screen-wake
                // event competing for the same collection will lose the CAS and skip.
                if (!Gate.TryBeginRotation(collection.Id, MinimumRotationCooldownSeconds, DateTime.Now))
                {
                    cooldownSkip++;
                    continue;
                }

                var target = collection.Target;
                log.LogInformation("Timer trigger: applying wallpaper — collection: {Name}, target: {Target}",
                    collection.Name, target);
                await rotationService.RotateCollectionWallpaperAsync(collection, target, fastApply: true);
                rotated++;
            }

            if (rotated > 0 || deferred > 0 || cooldownSkip > 0)
            {
                log.LogInformation("Timer check result — rotated: {Rotated}, keyguard-deferred: {Deferred}, cooldown-skipped: {Cooldown}, total active: {Total}",
                    rotated, deferred, cooldownSkip, activeTimerCollections.Count);
            }
            else
            {
                var now = DateTime.UtcNow;
                if ((now - _lastIntervalWaitingLog).TotalMinutes >= 5)
                {
                    log.LogInformation("Timer eval — {Count} active timer collection(s) waiting for interval to elapse",
                        activeTimerCollections.Count);
                    _lastIntervalWaitingLog = now;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _evaluationInProgress, 0);
        }
    }

    private bool IsKeyguardLocked()
    {
        try
        {
            var keyguardManager = (KeyguardManager?)GetSystemService(KeyguardService);
            return keyguardManager?.IsKeyguardLocked ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Atomically claims a rotation window for the given collection. Delegates to the shared
    /// <see cref="Gate"/> so simultaneous timer + screen-wake events cannot both rotate.
    /// </summary>
    public static bool TryBeginRotation(string collectionId, double cooldownSeconds)
        => Gate.TryBeginRotation(collectionId, cooldownSeconds, DateTime.Now);

    public static bool ShouldRotateTimerCollection(WallpaperCollection collection)
        => Gate.ShouldRotateTimerCollection(collection, DateTime.Now);

    /// <summary>
    /// Entry point invoked by <see cref="ScreenAwakeReceiver"/>. Forwards the screen event to the
    /// long-lived service instance so the (potentially slow) wallpaper work runs outside the
    /// BroadcastReceiver's ~10s execution limit. Returns false if no live service is available.
    /// </summary>
    public static bool NotifyScreenEvent(bool isScreenOff)
    {
        var instance = _instance;
        if (instance == null)
            return false;

        instance.HandleScreenEvent(isScreenOff);
        return true;
    }

    private void HandleScreenEvent(bool isScreenOff)
    {
        // Skip overlapping screen events; the rotation cooldown already makes a rapid
        // repeat a no-op, so dropping the extra event avoids piling up work + wakelocks.
        if (Interlocked.Exchange(ref _screenEventInProgress, 1) == 1)
        {
            GetLogger().LogDebug("Screen event skipped — a rotation is already in progress");
            return;
        }

        var log = GetLogger();
        var wakeLock = AcquireScreenEventWakeLock(log);

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessScreenEventAsync(isScreenOff, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Screen event processing error");
            }
            finally
            {
                ReleaseWakeLock(wakeLock, log);
                Interlocked.Exchange(ref _screenEventInProgress, 0);
            }
        });
    }

    private PowerManager.WakeLock? AcquireScreenEventWakeLock(ILogger log)
    {
        try
        {
            var powerManager = (PowerManager?)GetSystemService(PowerService);
            var wakeLock = powerManager?.NewWakeLock(
                WakeLockFlags.Partial,
                $"ScreenPainter:ScreenEvent:{Guid.NewGuid()}");
            wakeLock?.Acquire(AppConstants.WakeLockTimeoutMs);
            log.LogDebug("Screen-event WakeLock acquired");
            return wakeLock;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Screen-event WakeLock acquire failed");
            return null;
        }
    }

    private static void ReleaseWakeLock(PowerManager.WakeLock? wakeLock, ILogger log)
    {
        try
        {
            if (wakeLock != null && wakeLock.IsHeld)
            {
                wakeLock.Release();
                log.LogDebug("Screen-event WakeLock released");
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Screen-event WakeLock release failed");
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
            await ApplyScreenAwakeFailSafeAsync(collections, rotationService, log);
            await ApplyDeferredTimerRotationsAsync(collections, rotationService, log);
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
            if (!Gate.TryBeginRotation(collection.Id, MinimumRotationCooldownSeconds, DateTime.Now))
            {
                log.LogInformation("ScreenAwake skipped (rotation claimed by another trigger) — collection: {Name}", collection.Name);
                continue;
            }

            log.LogInformation("ScreenAwake trigger: applying wallpaper — collection: {Name}, target: {Target}", collection.Name, collection.Target);
            await rotationService.RotateCollectionWallpaperAsync(collection, collection.Target, fastApply: true);
        }
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
            if (Gate.TryBeginRotation(collection.Id, AppConstants.RecentRotationThresholdSeconds, DateTime.Now))
            {
                log.LogInformation("ScreenAwake fail-safe: applying wallpaper — collection: {Name}, target: {Target}",
                    collection.Name, collection.Target);
                await rotationService.RotateCollectionWallpaperAsync(collection, collection.Target, fastApply: true);
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
            if (!Gate.ShouldRotateTimerCollection(collection, DateTime.Now))
                continue;

            if (!Gate.TryBeginRotation(collection.Id, MinimumRotationCooldownSeconds, DateTime.Now))
                continue;

            log.LogInformation("Timer trigger on unlock: applying wallpaper — collection: {Name}, target: {Target}", collection.Name, collection.Target);
            await rotationService.RotateCollectionWallpaperAsync(collection, collection.Target, fastApply: true);
        }
    }

    private void RegisterAwakeReceiver()
    {
        if (_awakeReceiver == null)
        {
            _awakeReceiver = new ScreenAwakeReceiver();
            var filter = new IntentFilter();
            filter.AddAction(Intent.ActionScreenOff);
            filter.AddAction(Intent.ActionScreenOn);
            filter.AddAction(Intent.ActionUserPresent);
            RegisterReceiver(_awakeReceiver, filter);
        }
    }

    private Notification BuildNotification()
    {
        var intent = new Intent(this, typeof(MainActivity));
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= PendingIntentFlags.Immutable;
        }
        var pendingIntent = PendingIntent.GetActivity(this, 0, intent, flags);

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle("Screen Painter Active");
        builder.SetContentText("Automatic live wallpaper rotator running in background");
        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetOngoing(true);
        if (pendingIntent != null)
        {
            builder.SetContentIntent(pendingIntent);
        }
        builder.SetPriority(NotificationCompat.PriorityLow);

        var notification = builder.Build();
        return notification ?? throw new InvalidOperationException("Failed to build notification");
    }

    private void CreateNotificationChannel()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, "Screen Painter Background Engine", NotificationImportance.Low)
            {
                Description = "Keeps Screen Painter live wallpaper rotator active 24/7 in background"
            };

            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }
    }

    public override void OnDestroy()
    {
        GetLogger().LogInformation("Foreground service stopping");
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;

        if (_awakeReceiver != null)
        {
            try
            {
                UnregisterReceiver(_awakeReceiver);
            }
            catch
            {
            }
            _awakeReceiver = null;
        }

        if (ReferenceEquals(_instance, this))
            _instance = null;

        base.OnDestroy();
    }
}
#endif
