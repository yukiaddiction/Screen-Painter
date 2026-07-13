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
            return;

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
                await rotationService.RotateCollectionWallpaperAsync(collection, target);
                rotated++;
            }

            if (rotated > 0 || deferred > 0 || cooldownSkip > 0)
            {
                log.LogDebug("Timer check result — rotated: {Rotated}, keyguard-deferred: {Deferred}, cooldown-skipped: {Cooldown}, total active: {Total}",
                    rotated, deferred, cooldownSkip, activeTimerCollections.Count);
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

        AlarmReceiver.Cancel(this);

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

        base.OnDestroy();
    }
}
#endif
