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
/// <para>
/// The crop is <b>pan-only</b>. The plain fill scale — the smallest scale that still covers the
/// surface — is both the floor and the ceiling, so the picture is never enlarged into a tighter
/// crop and never reduced into a letterbox. What is left is placement: the frame slides over the
/// picture to follow the subject, and every pixel the photographer shot stays in the result.
/// A tighter crop is not available as a solution to any framing problem, which is the point —
/// it is lossy, and it was being spent on pictures that did not need it.
/// </para>
///
/// <para>
/// The subject is a <em>band</em>: the union of every detected face that counts as a subject,
/// extended below the tallest face to cover the head-and-torso read. Framing from a group rather
/// than from one face is what keeps a picture with two characters from being cropped onto one of
/// them.
/// </para>
///
/// <para>
/// Contains no MAUI, Android or I/O dependency, so it is compile-linked into the unit test
/// assembly and exercised directly (see <c>ArchitectureTests.ExtractedLogic_HasNoMauiOrAndroidDependencies</c>).
/// </para>
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

        public double Height { get; init; }
        public double CenterX => (Left + Right) / 2.0;
        public double CenterY => (Top + Bottom) / 2.0;
    }

    /// <summary>
    /// Computes the auto-framed configuration, or <c>null</c> when the caller should keep the
    /// incoming configuration untouched (bad geometry, or no usable face).
    ///
    /// <para>
    /// Only <see cref="ImageFramingConfig.OffsetX"/>, <see cref="ImageFramingConfig.OffsetY"/> and
    /// the user's own zoom are ever meaningful here: the returned
    /// <see cref="ImageFramingConfig.Scale"/> is the plain fill ratio unless the user asked for a
    /// zoom of their own.
    /// </para>
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

        // Fill is both the floor and the ceiling. Above it the picture would be cropped tighter
        // than covering the screen requires, which throws away picture for a framing that panning
        // can usually reach anyway; below it a screen edge would show through. The user's own zoom
        // stays a fine-tune multiplier on top of that, exactly as it was, so a hand-set zoom still
        // magnifies the framing the editor previewed.
        double scale = baseFillScale * Math.Max(1.0, baseConfig.Scale);

        double scaledW = imageW * scale;
        double scaledH = imageH * scale;

        // Placement is computed as an absolute top-left for the scaled image, but the contract with
        // the wallpaper service is relative: it centres the scaled image first and then adds the
        // offset (see ApplyDeviceFullScreenFraming). Subtracting that centring term here is what
        // makes the two agree.
        double centredX = (targetW - scaledW) / 2.0;
        double centredY = (targetH - scaledH) / 2.0;

        // Horizontal placement follows the subject sideways — the axis a portrait surface almost
        // always has room on.
        double placedX = PanToClear(
            band.Left * imageW * scale,
            band.Right * imageW * scale,
            targetW,
            scaledW,
            options.EdgeMarginRatio,
            centredX);

        // Vertical placement is asked for by the head alone, and only once the subject has come
        // within the headroom of an edge. A head that already clears both edges is not chased onto
        // a fixed line: that bias moved pictures which needed nothing.
        double placedY = PanToClear(
            band.Top * imageH * scale,
            band.Bottom * imageH * scale,
            targetH,
            scaledH,
            options.TopMarginRatio,
            centredY);

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
    /// The offset that brings <paramref name="nearPx"/>..<paramref name="farPx"/> just inside the
    /// screen, and no further. Returned as the absolute position of the scaled picture's leading
    /// edge, which is negative whenever the picture overflows the surface.
    ///
    /// <para>
    /// <paramref name="centred"/> — where the plain fill crop already sits — is the starting point,
    /// so a subject that clears both edges is returned untouched and a picture the photographer
    /// framed well is not moved at all. When an edge is violated, the picture moves to the position
    /// that clears that edge exactly: auto-framing nudges the composition, it does not re-make it.
    /// </para>
    ///
    /// <para>
    /// The result is then clamped to the offsets that still cover the surface, and that clamp is
    /// what makes the "not the end of the picture" rule fall out for free. Once the picture's own
    /// edge reaches the screen edge there is no offset left to give, so a subject standing at the
    /// edge of the photograph is left where it is instead of being chased by a black band.
    /// </para>
    ///
    /// <para>
    /// A subject larger than the safe zone cannot be brought inside it at all — a pair standing far
    /// apart on a narrow surface. That is the one case where the overhang is balanced rather than
    /// nudged, because there is no clear position to nudge to and splitting the loss evenly is
    /// better than spending it all on one side.
    /// </para>
    /// </summary>
    private static double PanToClear(
        double nearPx,
        double farPx,
        double target,
        double scaled,
        double marginRatio,
        double centred)
    {
        double coverageMin = target - scaled;
        double margin = Math.Max(0.0, marginRatio) * target;

        if (farPx - nearPx > target - (2 * margin))
            return Math.Clamp((target - nearPx - farPx) / 2.0, coverageMin, 0.0);

        // Where the picture already sits. A subject that clears both edges keeps it exactly.
        double offset = centred;

        // Too close to (or past) the leading edge: move in by the overhang. A subject that fits the
        // safe zone cannot violate both edges at once, so one branch is always enough.
        if (nearPx + offset < margin)
            offset = margin - nearPx;
        else if (farPx + offset > target - margin)
            offset = target - margin - farPx;

        return Math.Clamp(offset, coverageMin, 0.0);
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

        // The subject continues below the faces: shoulders and any held prop or gesture.
        double bandBottom = Math.Min(bottom + (tallest * Math.Max(0.0, options.TorsoExtendRatio)), 1.0);

        return new SubjectBand
        {
            Left = Math.Max(0.0, left),
            Top = Math.Max(0.0, top),
            Right = Math.Min(1.0, right),
            Bottom = Math.Max(bottom, bandBottom),
            Height = Math.Max(bottom, bandBottom) - Math.Max(0.0, top)
        };
    }
}
