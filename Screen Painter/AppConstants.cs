namespace Screen_Painter;

public static class AppConstants
{
    private static AppSettings _settings = new();

    public static void Initialize(AppSettings settings)
    {
        _settings = settings;
    }

    // Wallpaper
    public static int FallbackDisplayWidth => _settings.Wallpaper.FallbackDisplayWidth;
    public static int FallbackDisplayHeight => _settings.Wallpaper.FallbackDisplayHeight;
    public static int WallpaperPostApplyDelayMs => _settings.Wallpaper.PostApplyDelayMs;

    // Foreground Service
    public static int ForegroundServicePollingIntervalSeconds => _settings.ForegroundService.PollingIntervalSeconds;
    public static int ForegroundNotificationId => _settings.ForegroundService.NotificationId;
    public static string ForegroundNotificationChannelId => _settings.ForegroundService.NotificationChannelId;

    // HTTP
    public static int HttpRequestTimeoutSeconds => _settings.Http.RequestTimeoutSeconds;
    public static int MaxHttpRedirects => _settings.Http.MaxRedirects;

    // WebDAV
    public static int MaxWebDavRecursionDepth => _settings.WebDav.MaxRecursionDepth;

    // Cache
    public static int DefaultCacheCountPerCollection => _settings.Cache.DefaultCountPerCollection;
    public static long MaxCacheSizeBytes => _settings.Cache.MaxSizeBytes;

    // Alarm
    public static long DefaultAlarmIntervalMs => _settings.Alarm.IntervalMs;

    // Wake Lock
    public static int WakeLockTimeoutMs => _settings.WakeLock.TimeoutMs;
    public static int RecentRotationThresholdSeconds => _settings.WakeLock.RecentRotationThresholdSeconds;

    // App
    public static string DefaultAppTheme => _settings.App.DefaultTheme;
    public static int PromptMaxLength => _settings.App.PromptMaxLength;
}
