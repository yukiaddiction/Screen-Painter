using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screen_Painter.Models;
using Screen_Painter.Services.Imaging;

namespace Screen_Painter.Services;

public interface IAutoFramingService
{
    /// <summary>
    /// Returns the framing configuration to apply. When smart auto-framing is off, unavailable or
    /// finds no face, this is <paramref name="config"/> unchanged.
    /// </summary>
    Task<ImageFramingConfig> ResolveAsync(
        WallpaperCollection collection,
        string? imagePath,
        string? detectionKey,
        ImageFramingConfig config);
}

/// <summary>
/// Decides whether a wallpaper should be auto-framed and, if so, completes the config with
/// face-aware placement for the device's real wallpaper surface.
///
/// <para>
/// Every failure path returns the incoming configuration untouched, so enabling this feature can
/// never prevent a wallpaper from being applied. Manual per-image overrides always win: they
/// arrive as <c>config</c> and auto-framing only layers placement on top of them.
/// </para>
/// </summary>
public class AutoFramingService : IAutoFramingService
{
    private readonly IFaceDetector _detector;
    private readonly FaceDetectionCache _cache;
    private readonly AutoFramingOptions _options;
    private readonly ILogger<AutoFramingService> _logger;

    public AutoFramingService(
        IFaceDetector detector,
        FaceDetectionCache cache,
        AutoFramingOptions options,
        ILogger<AutoFramingService> logger)
    {
        _detector = detector;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<ImageFramingConfig> ResolveAsync(
        WallpaperCollection collection,
        string? imagePath,
        string? detectionKey,
        ImageFramingConfig config)
    {
        config ??= new ImageFramingConfig();

        if (collection == null || !collection.AutoFramingEnabled)
            return config;

        if (string.IsNullOrEmpty(imagePath))
            return config;

        // Cloud images are popped from the cache directory and re-downloaded under the same name,
        // so the remote identifier is the only key that survives an apply. Fall back to the path
        // for local files, where the path is itself the stable identity.
        var key = string.IsNullOrEmpty(detectionKey) ? imagePath : detectionKey;

        // Resolved outside the try: a missing display must not be mistaken for a detection fault.
        var surface = WallpaperSurfaceSize.NormalizeToPortrait(
            DeviceDisplaySize.width, DeviceDisplaySize.height);

        if (surface.width <= 0 || surface.height <= 0)
            return config;

        try
        {
            var detection = await _cache
                .GetOrDetectAsync(key, imagePath, _options, _detector)
                .ConfigureAwait(false);

            if (detection == null || !detection.HasFaces)
            {
                _logger.LogDebug("Auto-framing: no face in {File}, keeping manual framing",
                    System.IO.Path.GetFileName(imagePath));
                return config;
            }

            var computed = AutoFramingCalculator.Compute(
                detection.ImageWidth,
                detection.ImageHeight,
                surface.width,
                surface.height,
                config,
                detection.Faces,
                _options);

            if (computed == null)
                return config;

            _logger.LogInformation(
                "Auto-framing applied — collection: {Name}, image: {File}, surface: {W}x{H}, faces: {Count}, scale: {Scale:F3}, offset: {Dx:F0},{Dy:F0}",
                collection.Name, System.IO.Path.GetFileName(imagePath),
                surface.width, surface.height, detection.Faces.Length,
                computed.Scale, computed.OffsetX, computed.OffsetY);

            return computed;
        }
        catch (Exception ex)
        {
            // Belt and braces: GetOrDetectAsync already contains its failures, but a wallpaper must
            // still be applied even if this layer misbehaves.
            _logger.LogWarning(ex, "Auto-framing failed for {File}, falling back to manual framing",
                System.IO.Path.GetFileName(imagePath));
            return config;
        }
    }

    /// <summary>
    /// The device's wallpaper surface, in natural portrait orientation. Read once per apply;
    /// wrapped so a missing display (receivers, services, non-interactive contexts) falls back to
    /// the appsettings dimensions instead of throwing.
    /// </summary>
    private static (int width, int height) DeviceDisplaySize
    {
        get
        {
            int width = AppConstants.FallbackDisplayWidth;
            int height = AppConstants.FallbackDisplayHeight;

            try
            {
                var info = Microsoft.Maui.Devices.DeviceDisplay.Current.MainDisplayInfo;
                if (info.Width > 0 && info.Height > 0)
                {
                    width = (int)Math.Round(info.Width);
                    height = (int)Math.Round(info.Height);
                }
            }
            catch
            {
                // Intentional: the appsettings fallback keeps the math sane off-screen.
            }

            return (width, height);
        }
    }
}
