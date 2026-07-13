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
[IntentFilter(new[] { Intent.ActionBootCompleted, "android.intent.action.QUICKBOOT_POWERON" })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        if (intent.Action == Intent.ActionBootCompleted || intent.Action == "android.intent.action.QUICKBOOT_POWERON")
        {
            var pendingResult = GoAsync();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var log = GetLogger();
                try
                {
                    log.LogInformation("Boot completed — checking for enabled collections");

                    var scheduler = ServiceAccessor.GetService<ICollectionScheduler>();
                    if (scheduler == null) return;

                    var collections = await scheduler.GetAllCollectionsAsync();
                    if (collections.Any(c => c.IsEnabled))
                    {
                        log.LogInformation("Starting foreground service after boot — {Count} enabled collections",
                            collections.Count(c => c.IsEnabled));

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
                        }
                    }
                    else
                    {
                        log.LogInformation("Boot — no enabled collections, skipping service start");
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
