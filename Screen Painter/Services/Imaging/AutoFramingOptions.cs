namespace Screen_Painter.Services.Imaging;

/// <summary>
/// Tunables for smart auto-framing. The defaults encode the agreed composition rule: the subject
/// is framed as a group — every face of a comparable size, not just the largest — with its head
/// biased into the upper third, and the crop is bounded so that a picture whose shape is already
/// close to the phone's is nudged rather than re-composed. Every value is overridable from the
/// "AutoFraming" appsettings section.
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
    /// Where the top of the subject band should land vertically (0 = top edge, 1 = bottom).
    /// </summary>
    public double HeadTopRatio { get; set; } = 0.12;

    /// <summary>
    /// How far below the face the subject is assumed to continue — head, shoulders and any held
    /// prop or gesture — expressed as a multiple of face height. Extends the subject band, so a
    /// gesture is not cropped away.
    /// </summary>
    public double TorsoExtendRatio { get; set; } = 2.4;

    /// <summary>Guaranteed headroom above the detected face, as a fraction of target height.</summary>
    public double TopMarginRatio { get; set; } = 0.03;

    /// <summary>
    /// The share of the screen height the subject's face should occupy after framing. This is what
    /// asks for a crop at all: a face smaller than this is enlarged toward it, so a distant or
    /// full-body subject is framed rather than left as a speck on a 20:9 surface.
    /// <see cref="MinVisibleAreaFraction"/> is what keeps that request from being granted at any
    /// cost, and <see cref="MaxUpscale"/> is the hard ceiling under both.
    /// </summary>
    public double TargetFaceHeightRatio { get; set; } = 0.24;

    /// <summary>
    /// The share of the source the plain fill crop must have kept before a tighter crop is allowed
    /// at all, and the inverse is the ceiling on that crop. Covering a phone screen with a 3:4
    /// photo already discards a quarter of its width and three quarters of its area; this is what
    /// stops auto-framing from spending the rest of the composition on top of that. A source wide
    /// enough that the fill has already reduced it to a narrow slice has nothing left to protect,
    /// so only <see cref="MaxUpscale"/> applies there.
    /// </summary>
    public double MinVisibleAreaFraction { get; set; } = 0.80;

    /// <summary>
    /// How large a face must be, relative to the largest one, to count as part of the subject
    /// group. Two characters standing together are within a few tens of percent of each other in
    /// area and are both framed; a face far enough back to be background detail falls below this
    /// and is ignored, so it cannot drag the frame wide open.
    /// </summary>
    public double RelativeFaceAreaRatio { get; set; } = 0.40;

    /// <summary>Detections below this confidence are discarded.</summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>
    /// Hard cap on the aspect-preserving crop when enlarging, relative to the plain fill scale.
    /// Stops a distant face from being blown up into mush on a long phone screen. 1.35x keeps a
    /// typical landscape source's subjects at roughly a fifth of the screen height, which reads as
    /// a wallpaper rather than a passport photo.
    /// </summary>
    public double MaxUpscale { get; set; } = 1.35;

    /// <summary>Long edge, in pixels, that the image is downscaled to before inference.</summary>
    public int MaxPixels { get; set; } = 640;

    /// <summary>Minimum accepted face box in inference pixels; smaller boxes are treated as noise.</summary>
    public int MinFaceSizeInModelPixels { get; set; } = 24;

    /// <summary>Detection is attempted only for files at least this large, in bytes.</summary>
    public long MinFileSizeBytes { get; set; } = 1024;

    /// <summary>Wall-clock ceiling for one detection; on expiry the pipeline falls back to manual framing.</summary>
    public int DetectionTimeoutMs { get; set; } = 4000;
}
