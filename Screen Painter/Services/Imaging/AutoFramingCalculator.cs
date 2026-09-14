using System;
using System.Collections.Generic;
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
/// The subject is a <em>band</em>: the union of every detected face that counts as a subject,
/// extended below the tallest face to cover the head-and-torso read. Framing from a group rather
/// than from one face is what keeps a picture with two characters from being cropped onto one of
/// them.
///
/// Contains no MAUI, Android or I/O dependency, so it is compile-linked into the unit test
/// assembly and exercised directly (see <c>ArchitectureTests.ExtractedLogic_HasNoMauiOrAndroidDependencies</c>).
/// </summary>
public static class AutoFramingCalculator
{
    /// <summary>
    /// The subject band: the union of the qualifying faces, extended downward by
    /// <see cref="AutoFramingOptions.TorsoExtendRatio"/>. All values are normalized against the
    /// image the faces were measured on.
    /// </summary>
    public sealed class SubjectBand
    {
        public double Left { get; init; }
        public double Top { get; init; }
        public double Right { get; init; }
        public double Bottom { get; init; }

        /// <summary>The height of the tallest face in the group, which drives the requested zoom.</summary>
        public double FaceHeight { get; init; }

        public double Height { get; init; }
        public double CenterX => (Left + Right) / 2.0;
        public double CenterY => (Top + Bottom) / 2.0;
    }

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

        var band = SelectSubjects(faces, imageWidth, imageHeight, options);
        if (band == null)
            return null;

        double imageW = imageWidth;
        double imageH = imageHeight;
        double targetW = targetWidth;
        double targetH = targetHeight;

        // The crop scale the phone resolution actually requires to fill the wallpaper surface.
        double baseFillScale = Math.Max(targetW / imageW, targetH / imageH);
        if (baseFillScale <= 0)
            return null;

        double scale = ComputeScale(imageW, imageH, targetW, targetH, baseFillScale, band, options);

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

        double placedX = ComputePlacementX(band, imageW, targetW, scaledW, scale);
        double placedY = ComputePlacementY(band, imageH, targetH, scaledH, scale, options);

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
    /// The crop scale, chosen from the subject band rather than from any single face. The phone
    /// resolution and the image's aspect ratio set the baseline — the image must always cover the
    /// surface — and the band then decides whether a tighter crop frames the subject better, or
    /// would simply throw the photographer's framing away.
    ///
    /// <para>
    /// Two things ask for a crop and the larger wins. A face smaller than
    /// <see cref="AutoFramingOptions.TargetFaceHeightRatio"/> of the screen is enlarged toward it,
    /// so a distant or full-body subject is framed rather than left as a speck. A band that does
    /// not fit the surface — a pair standing apart, a face somewhere in a panorama — is brought in
    /// whole, which is what stops the crop from settling on one character and pushing the other
    /// off the screen.
    /// </para>
    ///
    /// <para>
    /// That request is then capped, because a crop is only worth taking while there is framing left
    /// to spend. Where covering the screen has kept at least
    /// <see cref="AutoFramingOptions.MinVisibleAreaFraction"/> of the picture, the shape change has
    /// been paid for and only <see cref="AutoFramingOptions.MaxUpscale"/> applies. Where covering
    /// the screen has already spent more than that — a landscape photo reduced to a vertical slice
    /// — there is no composition left to protect, so the ratio of the two governs instead and the
    /// less the fill kept, the tighter the crop may become.
    /// </para>
    ///
    /// <para>
    /// The user's own zoom is deliberately exempt from both bounds: it is applied to the scale
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
        SubjectBand band,
        AutoFramingOptions options)
    {
        double bandWidth = Math.Clamp(band.Right - band.Left, 0.0, 1.0);
        double bandHeight = Math.Clamp(band.Bottom - band.Top, 0.0, 1.0);

        // The zoom the subject asks for: a face smaller than the target share of the screen is
        // enlarged toward it, so a subject is framed rather than left as a speck on a 20:9 surface.
        double faceHpx = Math.Max(1.0, band.FaceHeight * imageH);
        double scaleForFace = (targetH * Math.Clamp(options.TargetFaceHeightRatio, 0.0, 1.0)) / faceHpx;

        // The zoom the band needs: the subject has to fit the screen it is being cropped for. A band
        // wider than the phone surface — a pair standing apart, a face somewhere in a panorama —
        // cannot be framed by covering the screen alone, because the slice that covers it need not
        // reach the subject at all. Taking whichever dimension demands more brings the band in, and
        // covers a wide subject and a tall one alike.
        double bandWpx = Math.Max(1.0, bandWidth * imageW);
        double bandHpx = Math.Max(1.0, bandHeight * imageH);
        double scaleForBand = Math.Max(targetW / bandWpx, targetH / bandHpx);

        double fitScale = Math.Max(scaleForFace, scaleForBand);

        // How far the crop may be enlarged. Where covering the screen has kept at least
        // MinVisibleAreaFraction of the picture, the shape change has been paid for and MaxUpscale
        // is the only bound left — which is what leaves a phone-shaped picture almost untouched.
        // Where covering the screen has already spent more than that — a landscape photo reduced to
        // a vertical slice — there is no composition left to protect, so the ratio of the two
        // governs instead, and the less the fill kept, the tighter the crop may become.
        double fillVisible = Math.Min(1.0, (targetW / baseFillScale) / imageW)
                           * Math.Min(1.0, (targetH / baseFillScale) / imageH);
        double minVisible = Math.Clamp(options.MinVisibleAreaFraction, 1e-6, 1.0);

        double cropCapRatio = fillVisible >= minVisible
            ? options.MaxUpscale
            : Math.Max(1.0, minVisible / Math.Max(1e-6, fillVisible));

        double upscaleCap = baseFillScale * Math.Min(options.MaxUpscale, cropCapRatio);

        double scale = Math.Clamp(fitScale, baseFillScale, upscaleCap);

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return baseFillScale;

        return scale;
    }

    /// <summary>
    /// Horizontal placement, returned as the absolute left edge of the scaled image.
    ///
    /// <para>
    /// The frame is centred on the band, which is the offset that keeps the subject whole wherever
    /// the crop is wide enough to allow it. It is bounded only by coverage: covering the surface
    /// pins the picture so no screen edge can show through, and where that makes exact centring
    /// impossible the picture sits as close to it as the surface permits — so a group mid-slide
    /// ends up split evenly between the two edges rather than one character being given up to
    /// whichever side the frame happened to stop on.
    /// </para>
    ///
    /// <para>
    /// Centring on the band is also what stops auto-framing from panning for its own sake. The crop
    /// is not moved to satisfy a notion of where a subject ought to sit; it moves only because the
    /// subject sits somewhere, and no further than the band's own centre requires.
    /// </para>
    /// </summary>
    private static double ComputePlacementX(
        SubjectBand band,
        double imageW,
        double targetW,
        double scaledW,
        double scale)
    {
        // Offsets that keep the picture covering the surface. Outside this range a screen edge
        // shows through, which is an outright visual defect.
        double coverageMin = targetW - scaledW;
        double coverageMax = 0.0;

        double bandLeft = band.Left * imageW * scale;
        double bandRight = band.Right * imageW * scale;

        // The offset that puts the band's centre on the screen's centre.
        double centred = (targetW - bandLeft - bandRight) / 2.0;

        return Math.Clamp(centred, coverageMin, coverageMax);
    }

    /// <summary>
    /// Vertical placement, returned as the absolute top edge of the scaled image. The top of the
    /// subject band is biased to the head line so the subject reads downward through the frame.
    ///
    /// <para>
    /// Placement is clamped against the offset that still covers the surface, never against zero.
    /// Clamping at zero instead would pin the picture to the top edge as soon as the crop is taller
    /// than the surface — which is the normal case — and the head would then land wherever that put
    /// it rather than on the head line. The subject's own headroom bound is what keeps the head
    /// from being pushed off the top; coverage still has the final say, because an edge gap is an
    /// outright visual defect while a head a fraction low is not.
    /// </para>
    ///
    /// <para>
    /// Lifting the band until its top reaches the head line would drag the picture down hard when
    /// the band is tall — a full-body or two-character subject would take a large bite out of the
    /// top of the frame to move the head a short distance. The band is therefore held inside the
    /// frame as well, which leaves the upper-third bias to operate at full strength on the compact
    /// subjects it was designed for and settles for a balanced frame on the tall ones.
    /// </para>
    /// </summary>
    private static double ComputePlacementY(
        SubjectBand band,
        double imageH,
        double targetH,
        double scaledH,
        double scale,
        AutoFramingOptions options)
    {
        // Where the top of the subject lands once the image is scaled.
        double bandTop = band.Top * imageH * scale;
        double bandBottom = band.Bottom * imageH * scale;
        double bandHeight = bandBottom - bandTop;

        // Bias the subject's head to the head line. This is the whole point of the feature, so it
        // wins unless it would take more of the frame than the composition can spare.
        double headLine = targetH * Math.Clamp(options.HeadTopRatio, 0.0, 0.9);
        double dy = headLine - bandTop;

        // Coverage is the floor: below it a gap would open at the bottom of the screen. Above, the
        // picture may not rise so far that the head crosses the headroom ceiling — the most it may
        // be lifted while the head still clears the top margin.
        double coverageFloor = targetH - scaledH;
        double minHeadroom = Math.Max(0.0, options.TopMarginRatio) * targetH;
        double headroomCeiling = Math.Max(coverageFloor, -bandTop + minHeadroom);

        double min = coverageFloor;
        double max = headroomCeiling;

        // A band taller than the frame cannot be placed whole, so keep it balanced instead of
        // letting the head-line bias push one end of the subject off the screen.
        if (bandHeight > targetH)
        {
            min = Math.Max(min, targetH - bandBottom);
            max = Math.Min(max, -bandTop);
        }

        if (min > max)
        {
            // Degenerate: fall back to whatever keeps the surface covered.
            min = coverageFloor;
            max = headroomCeiling;
        }

        return Math.Clamp(dy, min, max);
    }

    /// <summary>
    /// Picks the subject band: the union of every face that clears the confidence floor and is a
    /// comparable size to the largest one, extended below the tallest face by
    /// <see cref="AutoFramingOptions.TorsoExtendRatio"/> so the head-and-torso read is preserved.
    ///
    /// <para>
    /// Keeping a group rather than one winner is what makes a picture with two characters frame
    /// both of them. The relative floor is what keeps it from being carried away by a bystander:
    /// two people standing together are within a few tens of percent of each other in area, while a
    /// face far enough back to be background detail is a small fraction of the subject's.
    /// </para>
    /// </summary>
    public static SubjectBand? SelectSubjects(
        IReadOnlyList<FaceBox>? faces,
        int imageWidth,
        int imageHeight,
        AutoFramingOptions options)
    {
        if (faces == null || faces.Count == 0 || options == null)
            return null;

        if (imageWidth <= 0 || imageHeight <= 0)
            return null;

        // The anchor is the largest face: the subject the picture is most likely about.
        FaceBox? primary = null;
        foreach (var face in faces)
        {
            if (face == null || !face.IsValid)
                continue;

            if (face.Confidence < options.MinConfidence)
                continue;

            if (primary == null || face.Area > primary.Area)
                primary = face;
        }

        if (primary == null)
            return null;

        double areaFloor = primary.Area * Math.Clamp(options.RelativeFaceAreaRatio, 0.0, 1.0);

        double left = 1.0;
        double top = 1.0;
        double right = 0.0;
        double bottom = 0.0;
        double tallest = 0.0;
        bool any = false;

        foreach (var face in faces)
        {
            if (face == null || !face.IsValid)
                continue;

            if (face.Confidence < options.MinConfidence)
                continue;

            if (face.Area < areaFloor)
                continue;

            if (face.X < left) left = face.X;
            if (face.Y < top) top = face.Y;
            if (face.X + face.Width > right) right = face.X + face.Width;
            if (face.Bottom > bottom) bottom = face.Bottom;
            if (face.Height > tallest) tallest = face.Height;
            any = true;
        }

        if (!any || right <= left || bottom <= top)
            return null;

        // The subject continues below the faces: head, shoulders and any held prop or gesture.
        double bandBottom = Math.Min(bottom + (tallest * Math.Max(0.0, options.TorsoExtendRatio)), 1.0);

        return new SubjectBand
        {
            Left = Math.Max(0.0, left),
            Top = Math.Max(0.0, top),
            Right = Math.Min(1.0, right),
            Bottom = Math.Max(bottom, bandBottom),
            FaceHeight = tallest,
            Height = Math.Max(bottom, bandBottom) - Math.Max(0.0, top)
        };
    }
}
