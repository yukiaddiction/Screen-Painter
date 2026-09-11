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
    public GallerySettings Gallery { get; set; } = new();
    public AutoFramingSettings AutoFraming { get; set; } = new();
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

public class GallerySettings
{
    public int PageSize { get; set; } = 60;
    public int ManifestTtlMinutes { get; set; } = 15;
    public int ThumbnailMaxPixels { get; set; } = 256;
    public int PreviewMaxPixels { get; set; } = 1080;
    public int MaxParallelThumbnailJobs { get; set; } = 3;
    public int ViewportLookahead { get; set; } = 24;
    public long MaxThumbCacheSizeBytes { get; set; } = 100 * 1024 * 1024;
    public long MaxPreviewCacheSizeBytes { get; set; } = 50 * 1024 * 1024;
}

public class ApplicationSettings
{
    public string DefaultTheme { get; set; } = "Dark";
    public int PromptMaxLength { get; set; } = 500;
}

/// <summary>
/// Smart auto-framing: face-aware wallpaper placement computed for the device's real screen.
/// The numeric values feed <c>Screen_Painter.Services.Imaging.AutoFramingOptions</c>.
/// </summary>
public class AutoFramingSettings
{
    /// <summary>Default state for newly created collections; each collection can override it.</summary>
    public bool EnabledByDefault { get; set; }

    /// <summary>Where the top of the head lands vertically (0 = top edge, 1 = bottom).</summary>
    public double HeadTopRatio { get; set; } = 0.12;

    /// <summary>Half-width of the central band the face may occupy (0.30 = middle 60%).</summary>
    public double HorizontalSafeBand { get; set; } = 0.30;

    /// <summary>How far below the face the subject continues, as a multiple of face height.</summary>
    public double TorsoExtendRatio { get; set; } = 2.4;

    /// <summary>Guaranteed headroom above the face, as a fraction of screen height.</summary>
    public double TopMarginRatio { get; set; } = 0.03;

    /// <summary>Faces smaller than this fraction of the image are ignored as background.</summary>
    public double MinFaceAreaRatio { get; set; } = 0.002;

    /// <summary>Detection confidence floor.</summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>Cap on the aspect-preserving crop when enlarging, relative to the fill scale.</summary>
    public double MaxUpscale { get; set; } = 2.0;

    /// <summary>Long edge, in pixels, that an image is downscaled to before inference.</summary>
    public int DetectionMaxPixels { get; set; } = 640;

    /// <summary>Minimum accepted face box in inference pixels.</summary>
    public int MinFaceSizeInModelPixels { get; set; } = 24;

    /// <summary>Wall-clock ceiling for a single detection, in milliseconds.</summary>
    public int DetectionTimeoutMs { get; set; } = 4000;

    /// <summary>How long a cached detection stays valid, in days.</summary>
    public int CacheTtlDays { get; set; } = 30;

    /// <summary>Maximum number of cached detections kept on disk.</summary>
    public int MaxCacheEntries { get; set; } = 2000;
}
