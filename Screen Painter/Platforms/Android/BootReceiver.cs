#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;
using Screen_Painter.Services.Logging;
using Screen_Painter.Services.Scheduling;
using System.Linq;

namespace Screen_Painter.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, "android.intent.action.QUICKBOOT_POWERON", "android.intent.action.LOCKED_BOOT_COMPLETED" })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        if (intent.Action == Intent.ActionBootCompleted ||
            intent.Action == "android.intent.action.QUICKBOOT_POWERON" ||
            intent.Action == "android.intent.action.LOCKED_BOOT_COMPLETED")
        {
            var pendingResult = GoAsync();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var log = GetLogger();
                try
                {
                    log.LogInformation("Boot completed ({Action}) — checking for enabled collections", intent.Action);

                    var scheduler = ServiceAccessor.GetService<ICollectionScheduler>();
                    bool startService = false;

                    if (scheduler != null)
                    {
                        try
                        {
                            // Before the first unlock, credential-encrypted app storage
                            // is NOT accessible on DirectBootAware receivers. A read
                            // failure returns an empty list; in that case arm the
                            // watchdog alarm so the service starts after unlock instead
                            // of dying silently.
                            var collections = await scheduler.GetAllCollectionsAsync();
                            startService = collections.Any(c => c.IsEnabled);
                            if (!startService)
                                log.LogInformation("Boot — no enabled collections, skipping service start");
                        }
                        catch (System.Exception ex)
                        {
                            log.LogWarning(ex, "Boot — storage not yet accessible (pre-unlock); arming watchdog instead");
                            startService = false;
                        }
                    }

                    if (startService)
                    {
                        log.LogInformation("Starting foreground service after boot");
                        var serviceIntent = new Intent(context, typeof(WallpaperForegroundService));
                        try
                        {
                            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                            {
                                context.StartForegroundService(serviceIntent);
                            }
                            else
                            {
                                context.StartService(serviceIntent);
                            }
                        }
                        catch (System.Exception startEx)
                        {
                            log.LogWarning(startEx, "BootReceiver could not start foreground service (background start restriction)");
                            AlarmReceiver.Schedule(context);
                        }
                    }
                    else
                    {
                        // Either nothing is enabled yet or we cannot read storage yet.
                        // Arm the watchdog alarm so the chain survives pre-unlock boots.
                        AlarmReceiver.Schedule(context);
                    }
                }
                catch (System.Exception ex)
                {
                    log.LogError(ex, "BootReceiver error");
                }
                finally
                {
                    pendingResult?.Finish();
                }
            });
        }
    }

    private static ILogger GetLogger()
    {
        var factory = ServiceAccessor.GetService<ILoggerFactory>();
        return factory != null ? factory.CreateLogger("ScreenPainter.Platform") : NullLogger.Instance;
    }
}
#endif
