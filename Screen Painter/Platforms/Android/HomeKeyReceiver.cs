#if ANDROID
using System;
using Android.App;
using Android.Content;
using Microsoft.Extensions.Logging;
using Screen_Painter.Services;
using Screen_Painter.Services.Logging;

namespace Screen_Painter.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class HomeKeyReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent == null || context == null) return;

        if (intent.Action != Intent.ActionCloseSystemDialogs) return;

        var reason = intent.GetStringExtra("reason");
        if (reason != "homekey") return;

        var log = GetLogger();
        log.LogInformation("Home key pressed — forwarding to foreground service");

        if (!WallpaperForegroundService.NotifyHomeKeyEvent())
        {
            log.LogWarning("Home key event dropped — foreground service not running");
        }
    }

    private static ILogger GetLogger()
    {
        var factory = ServiceAccessor.GetService<ILoggerFactory>();
        return factory != null ? factory.CreateLogger("ScreenPainter.Platform") : NullLogger.Instance;
    }
}
#endif
