namespace Screen_Painter.Services.Imaging;

/// <summary>
/// Tunables for smart auto-framing. The defaults encode the agreed composition rule: a picture
/// that is already the phone's size or shape is applied untouched, and anything else is framed by
/// panning the plain fill crop — never by cropping into it. The subject is framed as a group —
/// every face of a comparable size, not just the largest — and the frame moves only as far as the
/// subject's own position requires. Every value is overridable from the "AutoFraming" appsettings
/// section.
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
    /// How close to a screen side edge the subject may sit before the picture is panned
    /// horizontally, as a fraction of target width. Inside this band the picture is not moved at
    /// all, which is what keeps a picture the photographer already framed well untouched.
    /// </summary>
    public double EdgeMarginRatio { get; set; } = 0.05;

    /// <summary>
    /// How close the source's pixel size must be to the wallpaper surface, in each dimension, to
    /// count as already being the phone's own size and be applied with no framing at all.
    /// </summary>
    public double SizeToleranceRatio { get; set; } = 0.20;

    /// <summary>
    /// How close the source's aspect ratio must be to the surface's for the plain fill crop to be a
    /// pure uniform downscale, which also needs no framing at all.
    /// </summary>
    public double ShapeToleranceRatio { get; set; } = 0.03;

    /// <summary>
    /// How far below the face the subject is assumed to continue — head, shoulders and any held
    /// prop or gesture — expressed as a multiple of face height. Extends the subject band, so a
    /// gesture is not cropped away.
    /// </summary>
    public double TorsoExtendRatio { get; set; } = 2.4;

    /// <summary>
    /// Guaranteed headroom above the detected face, as a fraction of target height. This is also
    /// the vertical trigger: the picture is moved up or down only once the head comes closer to the
    /// top of the screen than this.
    /// </summary>
    public double TopMarginRatio { get; set; } = 0.03;

    /// <summary>
    /// How large a face must be, relative to the largest one, to count as part of the subject
    /// group. Two characters standing together are within a few tens of percent of each other in
    /// area and are both framed; a face far enough back to be background detail falls below this
    /// and is ignored, so it cannot drag the frame wide open.
    /// </summary>
    public double RelativeFaceAreaRatio { get; set; } = 0.40;

    /// <summary>Detections below this confidence are discarded.</summary>
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>Long edge, in pixels, that the image is downscaled to before inference.</summary>
    public int MaxPixels { get; set; } = 640;

    /// <summary>Minimum accepted face box in inference pixels; smaller boxes are treated as noise.</summary>
    public int MinFaceSizeInModelPixels { get; set; } = 24;

    /// <summary>Detection is attempted only for files at least this large, in bytes.</summary>
    public long MinFileSizeBytes { get; set; } = 1024;

    /// <summary>Wall-clock ceiling for one detection; on expiry the pipeline falls back to manual framing.</summary>
    public int DetectionTimeoutMs { get; set; } = 4000;
}
