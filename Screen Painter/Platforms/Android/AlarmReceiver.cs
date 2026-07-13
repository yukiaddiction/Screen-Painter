#if ANDROID
using System;
using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;
using Screen_Painter.Services.Logging;

namespace Screen_Painter.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class AlarmReceiver : BroadcastReceiver
{
    private const long DefaultAlarmIntervalMs = 15 * 60 * 1000;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;

        var serviceIntent = new Intent(context, typeof(WallpaperForegroundService));
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                context.StartForegroundService(serviceIntent);
            else
                context.StartService(serviceIntent);
        }
        catch (Exception ex)
        {
            GetLogger().LogWarning(ex, "AlarmReceiver could not start foreground service (background start restriction)");
        }

        // Exact alarms are one-shot, so re-arm the next watchdog tick.
        Schedule(context);
    }

    public static void Schedule(Context context)
    {
        var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarmManager == null) return;

        var intent = new Intent(context, typeof(AlarmReceiver));

        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= PendingIntentFlags.Immutable;
        }
        var pending = PendingIntent.GetBroadcast(context, 0, intent, flags);
        if (pending == null) return;

        long intervalMs = DefaultAlarmIntervalMs;
        long triggerTime = SystemClock.ElapsedRealtime() + intervalMs;

        try
        {
            bool canExact = !OperatingSystem.IsAndroidVersionAtLeast(31) || alarmManager.CanScheduleExactAlarms();

            if (canExact && OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                // Exact + allow-while-idle grants a temporary exemption to start a
                // foreground service from the background on Android 12+.
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerTime, pending);
            }
            else
            {
                // Exact alarms not permitted by the user — fall back to a Doze-tolerant
                // inexact alarm so the watchdog still fires (best effort).
                if (OperatingSystem.IsAndroidVersionAtLeast(23))
                    alarmManager.SetAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerTime, pending);
                else
                    alarmManager.Set(AlarmType.ElapsedRealtimeWakeup, triggerTime, pending);
            }
        }
        catch (Exception ex)
        {
            GetLogger().LogWarning(ex, "AlarmReceiver failed to schedule watchdog alarm");
        }
    }

    public static void Cancel(Context context)
    {
        var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarmManager == null) return;

        var intent = new Intent(context, typeof(AlarmReceiver));

        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            flags |= PendingIntentFlags.Immutable;
        }
        var pending = PendingIntent.GetBroadcast(context, 0, intent, flags);
        if (pending != null)
        {
            alarmManager.Cancel(pending);
        }
    }

    private static ILogger GetLogger()
    {
        var factory = ServiceAccessor.GetService<ILoggerFactory>();
        return factory != null ? factory.CreateLogger("ScreenPainter.Platform") : NullLogger.Instance;
    }
}
#endif
