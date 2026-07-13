namespace Screen_Painter;

public class AppSettings
{
    public WallpaperSettings Wallpaper { get; set; } = new();
    public ForegroundServiceSettings ForegroundService { get; set; } = new();
    public HttpSettings Http { get; set; } = new();
    public WebDavSettings WebDav { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public AlarmSettings Alarm { get; set; } = new();
    public WakeLockSettings WakeLock { get; set; } = new();
    public ApplicationSettings App { get; set; } = new();
}

public class WallpaperSettings
{
    public int FallbackDisplayWidth { get; set; } = 1080;
    public int FallbackDisplayHeight { get; set; } = 1920;
    public int PostApplyDelayMs { get; set; } = 1000;
}

public class ForegroundServiceSettings
{
    public int PollingIntervalSeconds { get; set; } = 10;
    public int NotificationId { get; set; } = 1001;
    public string NotificationChannelId { get; set; } = "screen_painter_service_channel";
}

public class HttpSettings
{
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxRedirects { get; set; } = 5;
    public int PooledConnectionLifetimeMinutes { get; set; } = 5;
}

public class WebDavSettings
{
    public int MaxRecursionDepth { get; set; } = 10;
}

public class CacheSettings
{
    public int DefaultCountPerCollection { get; set; } = 10;
    public long MaxSizeBytes { get; set; } = 500 * 1024 * 1024;
}

public class AlarmSettings
{
    public long IntervalMs { get; set; } = 15 * 60 * 1000;
}

public class WakeLockSettings
{
    public int TimeoutMs { get; set; } = 10000;
    public int RecentRotationThresholdSeconds { get; set; } = 60;
}

public class ApplicationSettings
{
    public string DefaultTheme { get; set; } = "Dark";
    public int PromptMaxLength { get; set; } = 500;
}
