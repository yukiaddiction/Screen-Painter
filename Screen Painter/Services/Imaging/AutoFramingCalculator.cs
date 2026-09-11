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

        // Subject band: the face plus the head/torso below it. This is what keeps a gesture or a
        // held prop inside the crop, and it bounds how far the subject may drift horizontally.
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
        double placedY = ComputePlacementY(face, imageH, targetH, scaledH, scale, bandBottom, options);

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
    /// The subject band (instead of the face alone) bounds how much zoom the face may ask for. That
    /// bound is what keeps a very wide source sane: wallpaper art runs up to roughly 2.7x wider than
    /// tall, and such a band is already taller than the phone surface, so its fit scale is the real
    /// limit on how far in the crop may go. Without it, a large face on a panorama pushed the frame
    /// to a 3x-zoom of the band's own fit, cropping away most of the picture.
    /// </para>
    ///
    /// <para>
    /// The band deliberately does not drive the offset: sizing the crop so the whole band fits
    /// magnified the frame 6-9x, because a band that wide cannot fit a phone screen at any sane zoom.
    /// The band still keeps the subject in frame through the horizontal bounds and the vertical
    /// anchor.
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
        // How far the subject band allows the crop to zoom in. For a portrait source this is large
        // and the face-height goal drives; for a wide source it clamps hard, which is what we want.
        double bandWpx = Math.Max(1.0, Math.Max(0.0, bandRight - bandLeft) * imageW);
        double bandHpx = Math.Max(1.0, Math.Max(0.0, bandBottom - face.Y) * imageH);
        double bandFitScale = Math.Max(targetW / bandWpx, targetH / bandHpx);

        // Zoom implied by wanting the face to fill TargetFaceHeightRatio of the screen height,
        // but never demanding a tighter crop than the band can justify.
        double maxFaceHeightRatio = Math.Max(0.0, options.MaxUpscale) / Math.Max(1e-6, bandFitScale / baseFillScale);
        double desiredFaceHeightRatio = Math.Min(options.TargetFaceHeightRatio, maxFaceHeightRatio);

        double faceHpx = Math.Max(1.0, face.Height * imageH);
        double scaleForFace = (targetH * desiredFaceHeightRatio) / faceHpx;

        // A face must not be magnified without limit: distant faces have little detail to reveal.
        double upscaleCap = baseFillScale * Math.Max(1.0, options.MaxUpscale);

        double scale = Math.Max(baseFillScale, Math.Min(scaleForFace, upscaleCap));

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return baseFillScale;

        return scale;
    }

    /// <summary>
    /// Horizontal placement, returned as the absolute left edge of the scaled image. The face is
    /// kept on screen and then pulled toward the middle, but never further than the safe band
    /// requires, so an already well-composed face stays where the artist put it. Covering the
    /// screen has the final say, because a gap at a screen edge is an outright visual defect.
    /// </summary>
    private static double ComputePlacementX(
        FaceBox face,
        double imageW,
        double targetW,
        double scaledW,
        double scale,
        AutoFramingOptions options)
    {
        // Keep the face on screen first: a crop that hides the subject is never useful. A face wider
        // than the screen itself is centred, since no position can show all of it.
        double faceWidthOnScreen = face.Width * imageW * scale;
        double minCentre = Math.Min(faceWidthOnScreen, targetW) / 2.0;
        double maxCentre = Math.Max(targetW - minCentre, minCentre);
        double faceCentre = face.CenterX * imageW * scale;
        double safeCentre = Clamp(faceCentre, minCentre, maxCentre);

        // Then pull it toward the middle, but never further than the safe band requires — that is
        // what leaves a well-composed face where the artist put it.
        double bandHalf = targetW * Math.Max(0.0, Math.Min(1.0, options.HorizontalSafeBand));
        double pullTowardCentre = Clamp((targetW / 2.0) - safeCentre, -bandHalf, bandHalf);

        double dx = (targetW / 2.0) - safeCentre + pullTowardCentre;

        // Finally, the picture must still fill the surface: no gap may appear at either screen edge.
        // This wins over the face position, because an edge gap is an outright visual defect while a
        // face that does not fit was never going to be satisfiable. (On a four-to-one panorama, for
        // instance, the face can be wider than the whole phone screen.)
        return Clamp(dx, Math.Min(0.0, targetW - scaledW), 0.0);
    }

    /// <summary>
    /// Vertical placement, returned as the absolute top edge of the scaled image. The top of the
    /// head is biased into the upper third so the subject reads downward through the frame, and any
    /// leftover screen height is then spent revealing more of the subject rather than left as an
    /// empty band below it.
    /// </summary>
    private static double ComputePlacementY(
        FaceBox face,
        double imageH,
        double targetH,
        double scaledH,
        double scale,
        double bandBottom,
        AutoFramingOptions options)
    {
        // Where the top of the head lands once the image is scaled.
        double headTop = face.Y * imageH * scale;
        double headTopTarget = targetH * Clamp(options.HeadTopRatio, 0.0, 0.9);

        // Bias the head into the upper third. This is the whole point of the feature, so it wins.
        double dy = Clamp(headTopTarget - headTop, targetH - scaledH, 0.0);

        // Then spend any slack on revealing the subject downward. A phone screen is far taller than
        // most sources once the head is placed, and centring the leftover space would leave a large
        // empty band below the subject. The shift is bounded by that empty band, by the room the
        // subject's own frame allows, and by the top margin, so the head is never pushed off the top.
        double bandBottomOnScreen = (bandBottom * imageH * scale) + dy;
        double emptyBelow = targetH - bandBottomOnScreen;
        double downwardRoom = Math.Min(0.0, -dy);
        double topMargin = Math.Max(0.0, options.TopMarginRatio) * targetH;
        double maxDy = Math.Max(targetH - scaledH, -topMargin);

        double shiftDown = Math.Min(emptyBelow, Math.Min(downwardRoom, targetH * 0.20));
        if (shiftDown > 0)
            dy = Clamp(dy + shiftDown, Math.Max(targetH - scaledH, -shiftDown), maxDy);

        return dy;
    }

    /// <summary>
    /// Keeps the scaled image covering the target horizontally, so no black band can appear on
    /// either side. Only meaningful when the image is wider than the target after scaling.
    /// </summary>
    private static double ClampToImage(double dx, double targetW, double scaledW)
    {
        if (scaledW <= targetW)
            return (targetW - scaledW) / 2.0;

        return Clamp(dx, targetW - scaledW, 0.0);
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
