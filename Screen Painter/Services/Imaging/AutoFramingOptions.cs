namespace Screen_Painter.Services.Imaging;

/// <summary>
/// Tunables for smart auto-framing. The defaults encode the agreed composition rule: the
/// head is biased into the upper third while the head-and-torso band stays visible, and the
/// face keeps horizontal freedom inside a generous central band instead of being forced to
/// the exact middle. Every value is overridable from the "AutoFraming" appsettings section.
/// </summary>
public class AutoFramingOptions
{
    /// <summary>
    /// Highest-quality long edge, in pixels, that an image is downscaled to before inference.
    /// The detector walks down from this in powers of two until the decoded bitmap fits, so a
    /// very large photo never forces a full-size decode.
    /// </summary>
    public const int PreferredDecodeLongEdge = 640;

    /// <summary>
    /// Where the top of the primary face should land vertically (0 = top edge, 1 = bottom).
    /// </summary>
    public double HeadTopRatio { get; set; } = 0.12;

    /// <summary>
    /// Half-width of the central band the face may occupy without being pushed (0.30 means the
    /// middle 60%). This is what keeps the result from looking mechanically dead-centred.
    /// </summary>
    public double HorizontalSafeBand { get; set; } = 0.30;

    /// <summary>
    /// The share of the screen height the primary face should occupy after auto-framing. This is
    /// what sets the crop scale: a face that already fills this much of the frame is left at the
    /// plain fill scale, while a smaller or more distant face is enlarged toward it. Without this
    /// the crop would be driven by the subject band, which is far wider than a phone screen and
    /// would over-zoom small faces badly.
    /// </summary>
    public double TargetFaceHeightRatio { get; set; } = 0.30;

    /// <summary>
    /// How far below the face the subject is assumed to continue — head, shoulders and any held
    /// prop or gesture — expressed as a multiple of face height. Used for placement and for the
    /// horizontal bounds, so a gesture is not cropped away.
    /// </summary>
    public double TorsoExtendRatio { get; set; } = 2.4;

    /// <summary>Guaranteed headroom above the detected face, as a fraction of target height.</summary>
    public double TopMarginRatio { get; set; } = 0.03;

    /// <summary>Faces smaller than this fraction of the image are treated as background and ignored.</summary>
    public double MinFaceAreaRatio { get; set; } = 0.002;

    /// <summary>Detections below this confidence are discarded.</summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Hard cap on the aspect-preserving crop when enlarging, relative to the plain fill scale.
    /// Stops a distant face from being blown up into mush on a long phone screen. 1.6x keeps a
    /// typical portrait source's face at roughly a fifth to a quarter of the screen height, which
    /// reads as a wallpaper rather than a passport photo.
    /// </summary>
    public double MaxUpscale { get; set; } = 1.6;

    /// <summary>Long edge, in pixels, that the image is downscaled to before inference.</summary>
    public int MaxPixels { get; set; } = 640;

    /// <summary>Minimum accepted face box in inference pixels; smaller boxes are treated as noise.</summary>
    public int MinFaceSizeInModelPixels { get; set; } = 24;

    /// <summary>Detection is attempted only for files at least this large, in bytes.</summary>
    public long MinFileSizeBytes { get; set; } = 1024;

    /// <summary>Wall-clock ceiling for one detection; on expiry the pipeline falls back to manual framing.</summary>
    public int DetectionTimeoutMs { get; set; } = 4000;
}
