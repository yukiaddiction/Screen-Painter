#if ANDROID
using System;
using Android.App;
using Android.Content;
using Microsoft.Extensions.Logging;
using Screen_Painter.Services;
using Screen_Painter.Services.Logging;

namespace Screen_Painter.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class ScreenAwakeReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent == null || context == null) return;

        bool isScreenOff = intent.Action == Intent.ActionScreenOff;
        bool isUserPresent = intent.Action == Intent.ActionUserPresent;

        if (!isScreenOff && !isUserPresent) return;

        var log = GetLogger();
        log.LogInformation("Screen event: {Event}", isScreenOff ? "ScreenOff" : "UserPresent");

        // Forward to the long-lived foreground service so the (potentially slow) wallpaper
        // work runs outside this BroadcastReceiver's ~10s execution limit. The receiver
        // returns immediately, avoiding ANR kills.
        if (!WallpaperForegroundService.NotifyScreenEvent(isScreenOff))
        {
            log.LogWarning("Screen event dropped — foreground service not running");
        }
    }

    private static ILogger GetLogger()
    {
        var factory = ServiceAccessor.GetService<ILoggerFactory>();
        return factory != null ? factory.CreateLogger("ScreenPainter.Platform") : NullLogger.Instance;
    }
}
#endif
