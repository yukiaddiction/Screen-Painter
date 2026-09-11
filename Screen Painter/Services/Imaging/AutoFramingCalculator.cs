using System;
using System.Collections.Generic;
using System.Linq;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Imaging;

/// <summary>
/// Pure composition math: turns detected faces plus the phone's real wallpaper surface size
/// into a completed <see cref="ImageFramingConfig"/>.
///
/// The output contract mirrors <c>WallpaperServiceAndroid.ApplyDeviceFullScreenFraming</c> exactly:
/// the service computes <c>baseScale = max(targetW / imageW, targetH / imageH)</c>, multiplies by
/// <c>config.Scale</c>, then centres the result and adds <c>OffsetX</c>/<c>OffsetY</c>. This
/// calculator inverts that relationship, so it can return placement without touching the service.
///
/// Contains no MAUI, Android or I/O dependency, so it is compile-linked into the unit test
/// assembly and exercised directly (see <c>ArchitectureTests.ExtractedLogic_HasNoMauiOrAndroidDependencies</c>).
/// </summary>
public static class AutoFramingCalculator
{
    /// <summary>
    /// Computes the auto-framed configuration, or <c>null</c> when the caller should keep the
    /// incoming configuration untouched (bad geometry, or no usable face).
    /// </summary>
    public static ImageFramingConfig? Compute(
        int imageWidth,
        int imageHeight,
        int targetWidth,
        int targetHeight,
        ImageFramingConfig baseConfig,
        IReadOnlyList<FaceBox>? faces,
        AutoFramingOptions options)
    {
        if (baseConfig == null || options == null)
            return null;

        if (imageWidth <= 0 || imageHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            return null;

        var face = SelectPrimaryFace(faces, options);
        if (face == null)
            return null;

        double imageW = imageWidth;
        double imageH = imageHeight;
        double targetW = targetWidth;
        double targetH = targetHeight;

        // The crop scale the phone resolution actually requires to fill the wallpaper surface.
        double baseFillScale = Math.Max(targetW / imageW, targetH / imageH);
        if (baseFillScale <= 0)
            return null;

        // Subject band: the face plus the head/torso below it. This bounds how far the crop may
        // zoom in, so a held prop or gesture cannot be cut off by the enlargement.
        double bandLeft = face.X;
        double bandRight = Math.Min(1.0, face.X + face.Width);
        double bandBottom = Math.Min(1.0, face.Bottom + (face.Height * options.TorsoExtendRatio));

        double scale = ComputeScale(imageW, imageH, targetW, targetH, baseFillScale, face,
                                    bandLeft, bandRight, bandBottom, options);

        // The user's manual zoom stays in charge of the final frame, exactly as it does today:
        // it multiplies the chosen scale, but can never zoom out past covering the surface.
        scale = Math.Max(baseFillScale, scale * baseConfig.Scale);

        double scaledW = imageW * scale;
        double scaledH = imageH * scale;

        // Placement is computed as an absolute top-left for the scaled image, but the contract with
        // the wallpaper service is relative: it centres the scaled image first and then adds the
        // offset (see ApplyDeviceFullScreenFraming). Subtracting that centring term here is what
        // makes the two agree.
        double centredX = (targetW - scaledW) / 2.0;
        double centredY = (targetH - scaledH) / 2.0;

        double placedX = ComputePlacementX(face, imageW, targetW, scaledW, scale, options);
        double placedY = ComputePlacementY(face, imageH, targetH, scaledH, scale, options);

        return new ImageFramingConfig
        {
            // The service multiplies this by its own baseFillScale, so hand back the ratio.
            Scale = scale / baseFillScale,
            // The user's manual offset stays a pure fine-tune on top of the computed placement.
            OffsetX = (placedX - centredX) + baseConfig.OffsetX,
            OffsetY = (placedY - centredY) + baseConfig.OffsetY,
            AspectRatioMode = baseConfig.AspectRatioMode,
            CustomAspectRatio = baseConfig.CustomAspectRatio
        };
    }

    /// <summary>
    /// The crop scale. The phone resolution and the image's aspect ratio set the baseline — the
    /// image must always cover the surface — and the face size then sets the detail zoom, so a small
    /// subject is not left as a speck on a 20:9 screen.
    ///
    /// <para>
    /// Two things bound that zoom, and they serve different purposes. The subject band keeps the
    /// head-and-torso inside the crop, so a wide source cannot be cropped down to a sliver. The
    /// horizontal safe band keeps the crop narrow enough that the macro placement can still honour
    /// the band (see <see cref="MaxScaleForHorizontalBand"/>) — without it the enlargement and the
    /// placement disagree, and covering the surface jams the subject against a screen edge.
    /// </para>
    ///
    /// <para>
    /// The user's own zoom is deliberately exempt from the safe band: it is applied to the scale
    /// chosen here rather than folded into the constraint, so a hand-set zoom always magnifies the
    /// framing exactly as the editor previewed it.
    /// </para>
    /// </summary>
    private static double ComputeScale(
        double imageW,
        double imageH,
        double targetW,
        double targetH,
        double baseFillScale,
        FaceBox face,
        double bandLeft,
        double bandRight,
        double bandBottom,
        AutoFramingOptions options)
    {
        // How far the subject band allows the crop to zoom in. A portrait band is already taller
        // than the phone surface, so this binds only for a very wide source.
        double bandWpx = Math.Max(1.0, Math.Max(0.0, bandRight - bandLeft) * imageW);
        double bandHpx = Math.Max(1.0, Math.Max(0.0, bandBottom - face.Y) * imageH);
        double bandFitScale = Math.Max(targetW / bandWpx, targetH / bandHpx);

        // Zoom implied by wanting the face to fill TargetFaceHeightRatio of the screen height,
        // but never demanding a tighter crop than the band can justify.
        double maxFaceHeightRatio = Math.Max(0.0, options.MaxUpscale) / Math.Max(1e-6, bandFitScale / baseFillScale);
        double desiredFaceHeightRatio = Math.Min(options.TargetFaceHeightRatio, maxFaceHeightRatio);

        double faceHpx = Math.Max(1.0, face.Height * imageH);
        double scaleForFace = (targetH * desiredFaceHeightRatio) / faceHpx;

        // The face has to end up in frame at the scale the composition asks for. A crop that is too
        // narrow to shift the subject into the band is enlarged too far: covering the surface then
        // jams the face against a screen edge, which is what dragged subjects to the right of the
        // frame. So the enlargement is bounded by the largest scale whose crop can still be slid far
        // enough to bring the face inside the band.
        double scaleForBand = MaxScaleForHorizontalBand(face, imageW, targetW, baseFillScale, options);
        if (scaleForBand > 0)
            scaleForFace = Math.Min(scaleForFace, scaleForBand);

        // The head has to be able to reach its line in the upper third. A crop so tall that covering
        // the surface pins the picture near the top would drag the whole subject into the middle of
        // the frame instead, so the scale is trimmed to the point where the head does reach it.
        double scaleForHeadLine = MaxScaleForHeadLine(face, imageH, targetH, options);
        if (scaleForHeadLine > 0)
            scaleForFace = Math.Min(scaleForFace, scaleForHeadLine);

        // A face must not be magnified without limit: distant faces have little detail to reveal.
        // The cap only ever bounds an enlargement. On a source that is narrow relative to the phone
        // surface the cap can fall below the plain fill scale, and honouring it there would zoom the
        // picture *out* far enough to uncover the screen edge — the one thing the crop may never do.
        // The fill scale is therefore a hard floor under the cap, not a mere lower bound on the
        // result, so a zoom can never open a letterbox.
        double upscaleCap = Math.Max(baseFillScale, baseFillScale * Math.Max(1.0, options.MaxUpscale));

        double scale = Math.Max(baseFillScale, Math.Min(scaleForFace, upscaleCap));

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return baseFillScale;

        return scale;
    }

    /// <summary>
    /// The largest scale whose crop can still be slid far enough to bring the face inside the safe
    /// band, while the picture keeps covering the surface. Returns <c>0</c> when no enlargement is
    /// worth taking — a face too far off centre for the band to hold at any useful size.
    /// </summary>
    private static double MaxScaleForHorizontalBand(
        FaceBox face,
        double imageW,
        double targetW,
        double baseFillScale,
        AutoFramingOptions options)
    {
        if (imageW <= 0 || targetW <= 0)
            return 0.0;

        double faceCentreRatio = Clamp(face.CenterX, 0.0, 1.0);
        double centreX = targetW / 2.0;
        double bandHalf = centreX * Clamp(options.HorizontalSafeBand, 0.0, 1.0);

        // The face's own position in the picture has to reach the band once the crop is placed.
        // Moving the picture right moves the face right, so the reach is limited by whichever end
        // of the picture the crop would have to give up to get there.
        double reach = faceCentreRatio <= 0.5
            ? Math.Max(1e-6, faceCentreRatio * imageW)
            : Math.Max(1e-6, (1.0 - faceCentreRatio) * imageW);

        // The face lands inside the band as long as the crop can be offset to put it there, and the
        // crop may only reach the point where it still covers the surface.
        double idealCentre = Clamp(centreX, bandHalf, targetW - bandHalf);
        double scale = Math.Max(1e-6, (targetW - idealCentre) / reach);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 0.0;

        return Math.Max(baseFillScale, scale);
    }

    /// <summary>
    /// The largest scale at which the head can still be lifted onto the head line — the point in
    /// the upper third the composition targets — while the picture keeps covering the surface.
    /// Above it the crop is so tall that covering the screen pins the picture near the top, and the
    /// whole subject is dragged down into the middle of the frame.
    /// </summary>
    private static double MaxScaleForHeadLine(
        FaceBox face,
        double imageH,
        double targetH,
        AutoFramingOptions options)
    {
        double headTopRatio = Clamp(face.Y, 0.0, 1.0);
        if (headTopRatio <= 0 || imageH <= 0 || targetH <= 0)
            return 0.0;

        double headLine = targetH * Clamp(options.HeadTopRatio, 0.0, 0.9);
        if (headLine >= targetH)
            return 0.0;

        double scale = headLine / (headTopRatio * imageH);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return 0.0;

        return scale;
    }

    /// <summary>
    /// Horizontal placement, returned as the absolute left edge of the scaled image. The face is
    /// pulled toward the middle, but no further than the safe band requires and never at the cost of
    /// hiding part of the subject: the crop is chosen from the offsets that both cover the surface
    /// and keep the whole face visible. The crop scale is bounded so that this choice always exists.
    /// </summary>
    private static double ComputePlacementX(
        FaceBox face,
        double imageW,
        double targetW,
        double scaledW,
        double scale,
        AutoFramingOptions options)
    {
        // Offsets that keep the picture covering the surface. Outside this range a screen edge
        // shows through, which is an outright visual defect.
        double coverageMin = targetW - scaledW;
        double coverageMax = 0.0;

        // Offsets that keep the whole face on screen. A face wider than the surface cannot satisfy
        // this at any offset, so it is simply left with the full coverage range.
        double faceLeft = face.X * imageW * scale;
        double faceRight = (face.X + face.Width) * imageW * scale;
        if (faceRight - faceLeft <= targetW)
        {
            double visibleMin = Math.Max(coverageMin, -faceLeft);
            double visibleMax = Math.Min(coverageMax, targetW - faceRight);
            if (visibleMin <= visibleMax)
            {
                coverageMin = visibleMin;
                coverageMax = visibleMax;
            }
        }

        // Then pull the face toward the middle, but never further than the safe band requires — that
        // is what leaves a well-composed face where the artist put it. The band is a preference, so
        // where it conflicts with showing the whole subject, the subject wins.
        double bandHalf = targetW * Clamp(options.HorizontalSafeBand, 0.0, 1.0);
        double centreX = targetW / 2.0;
        double faceCentre = face.CenterX * imageW * scale;
        double desiredCentre = Clamp(faceCentre, centreX - bandHalf, centreX + bandHalf);
        double desiredDx = centreX - desiredCentre;

        return Clamp(desiredDx, coverageMin, coverageMax);
    }

    /// <summary>
    /// Vertical placement, returned as the absolute top edge of the scaled image. The top of the
    /// head is biased into the upper third so the subject reads downward through the frame.
    ///
    /// <para>
    /// Placement is clamped against the offset that still covers the surface, never against zero.
    /// Clamping at zero instead would pin the picture to the top edge as soon as the crop is taller
    /// than the surface — which is the normal case — and the head would then land wherever that put
    /// it rather than in the upper third. The subject's own headroom bound is what keeps the head
    /// from being pushed off the top; coverage still has the final say, because an edge gap is an
    /// outright visual defect while a head a fraction low is not.
    /// </para>
    /// </summary>
    private static double ComputePlacementY(
        FaceBox face,
        double imageH,
        double targetH,
        double scaledH,
        double scale,
        AutoFramingOptions options)
    {
        // Where the top of the head lands once the image is scaled.
        double headTop = face.Y * imageH * scale;
        double headTopTarget = targetH * Clamp(options.HeadTopRatio, 0.0, 0.9);

        // Bias the head into the upper third. This is the whole point of the feature, so it wins.
        double dy = headTopTarget - headTop;

        // A face near the very top of a source must not be cropped by that bias: lifting the picture
        // until the head reaches its line would carry the head off the top edge, because the head is
        // already above the line. The head therefore stops at the headroom ceiling — the most the
        // picture may be lifted while the head still clears the margin. Coverage stays the floor, so
        // this can never open a gap at the bottom of the screen.
        double minHeadroom = Math.Max(0.0, options.TopMarginRatio) * targetH;
        double coverageFloor = targetH - scaledH;
        double headroomCeiling = Math.Max(coverageFloor, -headTop + minHeadroom);

        return Clamp(dy, coverageFloor, headroomCeiling);
    }

    /// <summary>
    /// Picks the subject face: the largest bounding box that clears the confidence and
    /// minimum-area floors. Background faces in a crowd are deliberately ignored.
    /// </summary>
    public static FaceBox? SelectPrimaryFace(IReadOnlyList<FaceBox>? faces, AutoFramingOptions options)
    {
        if (faces == null || faces.Count == 0 || options == null)
            return null;

        FaceBox? best = null;
        foreach (var face in faces)
        {
            if (face == null || !face.IsValid)
                continue;

            if (face.Confidence < options.MinConfidence)
                continue;

            if (face.Area < options.MinFaceAreaRatio)
                continue;

            if (best == null || face.Area > best.Area)
                best = face;
        }

        return best;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
