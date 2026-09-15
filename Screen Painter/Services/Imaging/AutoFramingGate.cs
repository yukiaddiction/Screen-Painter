using System;

namespace Screen_Painter.Services.Imaging;

/// <summary>
/// How a source picture should be placed on the wallpaper surface, decided from the picture's own
/// pixel dimensions and before any face detection runs.
/// </summary>
public enum AutoFramingDecision
{
    /// <summary>
    /// The source is already the phone's own size. Applying it resamples nothing and crops nothing,
    /// so any crop auto-framing could take would be pure loss.
    /// </summary>
    AlreadyMatches,

    /// <summary>
    /// The source's shape matches the surface's, so the plain fill crop is a pure uniform
    /// downscale. There is no slack to pan into and nothing to move.
    /// </summary>
    ShapeMatches,

    /// <summary>Faces have to decide the placement.</summary>
    NeedsFraming
}

/// <summary>
/// The cheap gate in front of face detection. A picture that is already the phone's size or shape
/// needs no analysis at all: the plain fill crop is already the right answer, so the detector is
/// skipped entirely and the wallpaper is applied exactly as it is.
///
/// <para>
/// This exists because analysing such a picture could only make it worse. The composition math
/// then has nothing to add but a crop, and a crop of a picture that already fits is lost picture.
/// </para>
///
/// <para>
/// Pure and platform-free — no MAUI, Android or I/O dependency — so the rule is unit-testable
/// off-device and compile-links into the test assembly.
/// </para>
/// </summary>
public static class AutoFramingGate
{
    /// <summary>Fallback size tolerance when no options are supplied.</summary>
    public const double DefaultSizeToleranceRatio = 0.20;

    /// <summary>Fallback shape tolerance when no options are supplied.</summary>
    public const double DefaultShapeToleranceRatio = 0.03;

    /// <summary>
    /// Classifies a source picture against the wallpaper surface.
    ///
    /// <para>
    /// The two tolerances are deliberately different. Pixel size has to be close before "already
    /// matches" is claimed, because that decision promises the picture is not resampled at all.
    /// Aspect ratio only has to be close, because a matching ratio means the fill crop is a plain
    /// uniform downscale — the picture keeps every pixel it has, whatever its resolution.
    /// </para>
    /// </summary>
    /// <param name="sourceWidth">The picture's real pixel width, in its own orientation.</param>
    /// <param name="sourceHeight">The picture's real pixel height, in its own orientation.</param>
    /// <param name="targetWidth">The wallpaper surface width, already normalized to portrait.</param>
    /// <param name="targetHeight">The wallpaper surface height, already normalized to portrait.</param>
    /// <param name="options">Tunables; <c>null</c> falls back to the built-in tolerances.</param>
    public static AutoFramingDecision Classify(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        AutoFramingOptions? options)
    {
        // Unknown geometry fails open. Falling through to detection is always safe, while claiming
        // "already matches" on a guess would leave a picture unframed that needed framing.
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            return AutoFramingDecision.NeedsFraming;

        double sizeTolerance = Math.Clamp(
            options?.SizeToleranceRatio ?? DefaultSizeToleranceRatio, 0.0, 1.0);
        double shapeTolerance = Math.Clamp(
            options?.ShapeToleranceRatio ?? DefaultShapeToleranceRatio, 0.0, 1.0);

        // Compared against the surface in the source's own orientation, deliberately not
        // portrait-normalized. A landscape picture that happens to carry a portrait phone's pixel
        // count is not "the phone's size" — it is a 2:1 crop waiting to happen.
        if (Within(sourceWidth, targetWidth, sizeTolerance) &&
            Within(sourceHeight, targetHeight, sizeTolerance))
        {
            return AutoFramingDecision.AlreadyMatches;
        }

        double sourceAspect = (double)sourceWidth / sourceHeight;
        double targetAspect = (double)targetWidth / targetHeight;

        if (Within(sourceAspect, targetAspect, shapeTolerance))
            return AutoFramingDecision.ShapeMatches;

        return AutoFramingDecision.NeedsFraming;
    }

    /// <summary>True when <paramref name="value"/> is within a relative tolerance of the reference.</summary>
    private static bool Within(double value, double reference, double tolerance)
    {
        if (reference <= 0)
            return false;

        return Math.Abs(value - reference) / reference <= tolerance;
    }
}
