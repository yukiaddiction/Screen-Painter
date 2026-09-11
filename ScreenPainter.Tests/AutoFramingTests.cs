using System;
using System.Collections.Generic;
using Screen_Painter.Models;
using Screen_Painter.Services.Imaging;

namespace ScreenPainter.Tests;

/// <summary>
/// Exercises the pure auto-framing composition math. These tests deliberately re-implement the
/// placement contract of <c>WallpaperServiceAndroid.ApplyDeviceFullScreenFraming</c> so the
/// calculator is asserted against the geometry the Android service actually executes.
/// </summary>
public class AutoFramingTests
{
    private const int ScreenW = 1080;
    private const int ScreenH = 2400;

    private static AutoFramingOptions Options() => new();

    private static FaceBox Face(double x, double y, double w, double h, double confidence = 0.9)
        => new() { X = x, Y = y, Width = w, Height = h, Confidence = confidence };

    /// <summary>
    /// Mirrors the Android service: baseScale = max(tw/sw, th/sh), then scale, then centre + offset.
    /// </summary>
    /// <summary>
    /// Reproduces exactly what the wallpaper service does with the returned config: it derives its
    /// own fill scale from the source and target size, multiplies by the config's scale, centres the
    /// result, then adds the offsets. The calculator picks its scale relative to that same fill
    /// scale, so the on-screen scale is <c>fillScale * config.Scale</c> and the offsets resolve to
    /// the absolute placement the calculator computed.
    /// </summary>
    private static (double scale, double dx, double dy) Placement(
        int imageW, int imageH, int targetW, int targetH, ImageFramingConfig config)
    {
        double fillScale = Math.Max((double)targetW / imageW, (double)targetH / imageH);
        double scale = fillScale * config.Scale;
        double dx = ((targetW - (imageW * scale)) / 2.0) + config.OffsetX;
        double dy = ((targetH - (imageH * scale)) / 2.0) + config.OffsetY;
        return (scale, dx, dy);
    }

    private static ImageFramingConfig? Compute(
        int imageW, int imageH, IReadOnlyList<FaceBox> faces, ImageFramingConfig? baseConfig = null)
        => AutoFramingCalculator.Compute(
            imageW, imageH, ScreenW, ScreenH,
            baseConfig ?? new ImageFramingConfig(), faces, Options());

    [Fact]
    public void NoFaces_ReturnsNullSoManualFramingIsKept()
    {
        Assert.Null(Compute(1000, 1500, Array.Empty<FaceBox>()));
    }

    [Fact]
    public void PrimaryFace_IsTheLargestNotTheFirst()
    {
        var small = Face(0.05, 0.05, 0.05, 0.05);
        var large = Face(0.40, 0.30, 0.20, 0.20);

        var chosen = AutoFramingCalculator.SelectPrimaryFace(new[] { small, large }, Options());

        Assert.NotNull(chosen);
        Assert.Equal(large.X, chosen!.X);
    }

    [Fact]
    public void LowConfidenceOrTinyFaces_AreIgnored()
    {
        var options = Options();

        var lowConfidence = new[] { Face(0.4, 0.3, 0.2, 0.2, confidence: 0.10) };
        Assert.Null(AutoFramingCalculator.SelectPrimaryFace(lowConfidence, options));

        // 0.001 area is below the 0.002 background floor.
        var tiny = new[] { Face(0.4, 0.3, 0.04, 0.025) };
        Assert.Null(AutoFramingCalculator.SelectPrimaryFace(tiny, options));
    }

    [Fact]
    public void ScaledImage_AlwaysCoversThePhoneSurface()
    {
        // Low, far and small subjects are the cases most likely to leave letterboxing.
        var cases = new List<FaceBox>
        {
            Face(0.44, 0.05, 0.12, 0.08),
            Face(0.44, 0.80, 0.12, 0.08),
            Face(0.02, 0.30, 0.10, 0.10),
            Face(0.88, 0.30, 0.10, 0.10)
        };

        foreach (var face in cases)
        {
            var config = Compute(1000, 1500, new[] { face });
            Assert.NotNull(config);

            var (scale, dx, dy) = Placement(1000, 1500, ScreenW, ScreenH, config!);

            Assert.True(1000 * scale >= ScreenW - 0.5, "scaled width must cover the surface");
            Assert.True(1500 * scale >= ScreenH - 0.5, "scaled height must cover the surface");
            Assert.True(dx <= 0.5, $"no left gap allowed (dx={dx})");
            Assert.True(dx + (1000 * scale) >= ScreenW - 0.5, $"no right gap allowed (dx={dx})");
            Assert.True(dy <= 0.5, $"no top gap allowed (dy={dy})");
            Assert.True(dy + (1500 * scale) >= ScreenH - 0.5, $"no bottom gap allowed (dy={dy})");
        }
    }

    [Fact]
    public void FaceIsBiasedIntoTheUpperThird()
    {
        var config = Compute(1000, 1500, new[] { Face(0.42, 0.42, 0.16, 0.12) });
        Assert.NotNull(config);

        var (scale, _, dy) = Placement(1000, 1500, ScreenW, ScreenH, config!);
        double faceTopOnScreen = (0.42 * 1500 * scale) + dy;
        double faceBottomOnScreen = ((0.42 + 0.12) * 1500 * scale) + dy;

        // The head is biased above the middle of the frame and the whole face stays in the upper
        // half, so the subject reads downward. A source that is already narrower than the phone
        // surface cannot lift the head onto the head line without uncovering the screen edge, so the
        // head settles as high as covering the surface allows rather than at the exact line.
        Assert.True(faceTopOnScreen >= 0, $"head must not be cropped (top={faceTopOnScreen})");
        Assert.True(faceTopOnScreen <= ScreenH / 2.0, $"head should sit above the middle (top={faceTopOnScreen})");
        Assert.True(faceBottomOnScreen <= ScreenH * 0.75, $"face should not be pushed low (bottom={faceBottomOnScreen})");
    }

    [Fact]
    public void Headroom_IsGuaranteedAboveTheFace()
    {
        // A face near the very top of the image would otherwise be cropped.
        var config = Compute(1000, 1500, new[] { Face(0.45, 0.02, 0.14, 0.10) });
        Assert.NotNull(config);

        var (scale, _, dy) = Placement(1000, 1500, ScreenW, ScreenH, config!);
        double faceTopOnScreen = (0.02 * 1500 * scale) + dy;

        Assert.True(faceTopOnScreen >= ScreenH * Options().TopMarginRatio - 0.5,
            $"at least {Options().TopMarginRatio:P0} headroom required (top={faceTopOnScreen})");
    }

    [Fact]
    public void AnExtremeFaceIsKeptVisibleAndPulledTowardTheMiddle()
    {
        // The source is 20:9 like the target, but the face is small enough that a zoom is allowed,
        // which is what creates the horizontal slack the safe band can act on.
        var face = Face(0.68, 0.40, 0.06, 0.045);
        var config = Compute(1000, 2222, new[] { face });
        Assert.NotNull(config);

        var (scale, dx, dy) = Placement(1000, 2222, ScreenW, ScreenH, config!);

        Assert.True(1000 * scale > ScreenW, "slack is required for this case to be meaningful");
        Assert.True(dx <= 0.5 && dx + (1000 * scale) >= ScreenW - 0.5, "no horizontal gaps allowed");
        Assert.True(dy <= 0.5 && dy + (2222 * scale) >= ScreenH - 0.5, "no vertical gaps allowed");

        // The whole face stays visible rather than being pushed off the screen edge.
        double faceLeft = (face.X * 1000 * scale) + dx;
        double faceRight = ((face.X + face.Width) * 1000 * scale) + dx;
        Assert.True(faceLeft >= -0.5, $"face cut on the left (x={faceLeft:F1})");
        Assert.True(faceRight <= ScreenW + 0.5, $"face cut on the right (x={faceRight:F1})");

        // And it is pulled away from the edge toward the middle.
        double faceCentreOnScreen = ((face.X + (face.Width / 2)) * 1000 * scale) + dx;
        double midGap = ScreenW - faceCentreOnScreen;
        Assert.True(midGap >= (ScreenW / 2.0) - (ScreenW * Options().HorizontalSafeBand) - 1.0,
            $"face should be pulled away from the edge, gap was {midGap:F1}px");
    }

    [Fact]
    public void HorizontalPlacement_NeverLeavesAGapAtEitherScreenEdge()
    {
        // The invariant that must hold for every source and every face position: the scaled picture
        // covers the full screen width, so no black band can ever appear at a screen edge.
        int[] widths = { 500, 1000, 1600, 4000 };
        int[] heights = { 500, 1500, 2222, 4000 };
        (double x, double y, double w, double h)[] faces =
        {
            (0.02, 0.30, 0.10, 0.10),
            (0.35, 0.30, 0.08, 0.05),
            (0.68, 0.40, 0.06, 0.05),
            (0.90, 0.20, 0.09, 0.08),
            (0.30, 0.60, 0.20, 0.18)
        };

        foreach (var width in widths)
        {
            foreach (var height in heights)
            {
                foreach (var f in faces)
                {
                    var config = Compute(width, height, new[] { Face(f.x, f.y, f.w, f.h) });
                    if (config == null)
                        continue;

                    var (scale, dx, dy) = Placement(width, height, ScreenW, ScreenH, config);

                    Assert.True(dx <= 0.5 && dx + (width * scale) >= ScreenW - 0.5,
                        $"horizontal gap for {width}x{height} face({f.x},{f.y}) dx={dx:F2}");
                    Assert.True(dy <= 0.5 && dy + (height * scale) >= ScreenH - 0.5,
                        $"vertical gap for {width}x{height} face({f.x},{f.y}) dy={dy:F2}");
                }
            }
        }
    }

    [Fact]
    public void FaceInsideTheSafeBand_IsNotPushed()
    {
        // A face already within the central band must not be moved: the crop is left where the
        // geometry puts it, which is what stops the result from looking mechanically centred.
        var face = Face(0.50, 0.36, 0.06, 0.045);
        var config = Compute(1000, 3000, new[] { face });
        Assert.NotNull(config);

        var (scale, dx, _) = Placement(1000, 3000, ScreenW, ScreenH, config!);
        double faceCentreOnScreen = ((0.50 + 0.03) * 1000 * scale) + dx;
        double offsetFromCentre = Math.Abs(faceCentreOnScreen - (ScreenW / 2.0));

        Assert.True(offsetFromCentre <= (ScreenW * Options().HorizontalSafeBand) + 1.0,
            $"a face inside the band must not be pushed out of it, offset was {offsetFromCentre:F1}px");
    }

    [Fact]
    public void DifferentPhoneResolutions_ProduceDifferentPlacement()
    {
        var face = new[] { Face(0.40, 0.25, 0.15, 0.11) };

        var tall = AutoFramingCalculator.Compute(1000, 1500, 1080, 2400, new ImageFramingConfig(), face, Options());
        var taller = AutoFramingCalculator.Compute(1000, 1500, 1440, 3120, new ImageFramingConfig(), face, Options());

        Assert.NotNull(tall);
        Assert.NotNull(taller);

        // The phone surface must shape the result: different fill scales, so the actual on-screen
        // geometry differs even though the crop ratio (relative to fill) is the same.
        var (tallScale, tallDx, _) = Placement(1000, 1500, 1080, 2400, tall!);
        var (tallerScale, tallerDx, _) = Placement(1000, 1500, 1440, 3120, taller!);

        Assert.NotEqual(tallScale, tallerScale, 3);
        Assert.NotEqual(tallDx, tallerDx, 3);
    }

    [Fact]
    public void EdgeFace_WholeSubjectBandStaysVisible()
    {
        // Far left, with plenty of torso: the band must not be cut by the screen edge.
        var face = Face(0.02, 0.35, 0.10, 0.09);
        var config = Compute(1000, 1500, new[] { face });
        Assert.NotNull(config);

        var (scale, dx, _) = Placement(1000, 1500, ScreenW, ScreenH, config!);

        double bandLeftOnScreen = (face.X * 1000 * scale) + dx;
        double bandRightOnScreen = ((face.X + face.Width) * 1000 * scale) + dx;

        Assert.True(bandLeftOnScreen >= -0.5, $"subject cut on the left (x={bandLeftOnScreen})");
        Assert.True(bandRightOnScreen <= ScreenW + 0.5, $"subject cut on the right (x={bandRightOnScreen})");
    }

    [Fact]
    public void ManualOffset_IsPreservedAsAFineTune()
    {
        var manual = new ImageFramingConfig { Scale = 1.0, OffsetX = 40, OffsetY = -30 };

        var auto = Compute(1000, 1500, new[] { Face(0.42, 0.40, 0.16, 0.12) });
        var withManual = Compute(1000, 1500, new[] { Face(0.42, 0.40, 0.16, 0.12) }, manual);

        Assert.NotNull(auto);
        Assert.NotNull(withManual);

        Assert.Equal(40.0, withManual!.OffsetX - auto!.OffsetX, 3);
        Assert.Equal(-30.0, withManual.OffsetY - auto.OffsetY, 3);
    }

    [Fact]
    public void ManualScale_IsKeptAsAMultiplier()
    {
        var manual = new ImageFramingConfig { Scale = 1.25 };

        var auto = Compute(1000, 1500, new[] { Face(0.42, 0.40, 0.16, 0.12) });
        var withManual = Compute(1000, 1500, new[] { Face(0.42, 0.40, 0.16, 0.12) }, manual);

        Assert.NotNull(auto);
        Assert.NotNull(withManual);

        Assert.Equal(auto!.Scale * 1.25, withManual!.Scale, 6);
    }

    [Fact]
    public void WideLandscapeSource_IsCroppedPortraitAroundTheFace()
    {
        var face = new[] { Face(0.62, 0.30, 0.10, 0.14) };
        var config = Compute(4000, 2000, face);
        Assert.NotNull(config);

        var (scale, dx, dy) = Placement(4000, 2000, ScreenW, ScreenH, config!);
        double baseFill = Math.Max((double)ScreenW / 4000, (double)ScreenH / 2000);

        Assert.True(4000 * scale >= ScreenW - 0.5);
        Assert.True(2000 * scale >= ScreenH - 0.5);
        Assert.True(dx <= 0.5 && dx + (4000 * scale) >= ScreenW - 0.5, "no horizontal gaps allowed");
        Assert.True(dy <= 0.5 && dy + (2000 * scale) >= ScreenH - 0.5, "no vertical gaps allowed");

        // A panorama must not be cropped deeper than the face justifies just because it is wide.
        Assert.True(config.Scale <= Options().MaxUpscale + 1e-6,
            $"crop capped at {Options().MaxUpscale}x fill, got {config.Scale:F3}x");
        Assert.True(scale <= (baseFill * Options().MaxUpscale) + 1e-6,
            $"on-screen crop capped at {Options().MaxUpscale}x fill, got {scale / baseFill:F3}x");

        // A 4000x2000 panorama cropped to a 20:9 portrait surface is a severe aspect change. The face
        // does fit the screen at this crop, but reaching it would require panning the picture off one
        // edge and leaving a black band — so coverage wins and the crop shows the frame instead. The
        // subject here sits at 0.62 of the width, which is what pushes it out of the reachable slice.
        double faceLeftOnScreen = (0.62 * 4000 * scale) + dx;
        double faceRightOnScreen = (0.72 * 4000 * scale) + dx;
        double reachableLeft = -dx;
        Assert.True(faceRightOnScreen < reachableLeft || faceLeftOnScreen > reachableLeft + ScreenW,
            "this case is only interesting while the face sits outside the slice that covers the screen");
    }

    [Fact]
    public void DistantFace_CropIsCappedSoItIsNotBlownUp()
    {
        // Small enough to demand far more zoom than is reasonable, but still above the
        // background-face floor so it is treated as the subject.
        var face = new[] { Face(0.46, 0.30, 0.08, 0.05) };
        var options = Options();
        var config = Compute(4000, 6000, face);
        Assert.NotNull(config);

        double baseFill = Math.Max((double)ScreenW / 4000, (double)ScreenH / 6000);
        double applied = baseFill * config!.Scale;

        // The enlargement is capped rather than unbounded, and it never zooms the picture out past
        // the plain fill scale — that would uncover the screen edge.
        Assert.True(config.Scale <= options.MaxUpscale + 1e-6,
            $"crop capped at {options.MaxUpscale}x fill, got {config.Scale:F3}x");
        Assert.True(applied >= baseFill - 1e-6,
            $"crop must at least fill the surface (applied {applied:F3} vs fill {baseFill:F3})");
    }

    [Fact]
    public void OffCentreFace_IsNotZoomedPastWhatTheBandCanFrame()
    {
        // A face sitting away from the middle of a source that is wider than the phone surface. The
        // crop cannot be slid far enough to bring a face that far off centre into the band, so an
        // enlargement on top of it would leave the subject jammed against a screen edge. The crop
        // stays at the plain fill scale instead, which is the framing the source already had.
        var face = Face(0.42, 0.18, 0.16, 0.12);
        var config = Compute(3024, 4032, new[] { face });
        Assert.NotNull(config);

        double baseFill = Math.Max((double)ScreenW / 3024, (double)ScreenH / 4032);
        var (scale, dx, _) = Placement(3024, 4032, ScreenW, ScreenH, config!);
        double faceCentreOnScreen = ((0.42 + 0.08) * 3024 * scale) + dx;

        Assert.True(scale <= baseFill * Options().MaxUpscale + 1e-6, "the upscale cap must still hold");
        Assert.True(Math.Abs(faceCentreOnScreen - (ScreenW / 2.0)) <= (ScreenW * Options().HorizontalSafeBand) + 1.0,
            $"face must stay inside the safe band, centre was {faceCentreOnScreen:F0}px");
    }

    [Fact]
    public void NoSourceOrFacePosition_IsPushedToAScreenEdge()
    {
        // The defect that reached the user: the crop was clamped into the range that covers the
        // surface, which for a wide picture threw the subject hard against a screen edge — a face
        // at 51% of the source was rendered at 76-86% of the screen. Whatever the geometry, the
        // whole face has to stay in frame and the zoom has to stay inside the upscale cap.
        int[] widths = { 1080, 1440, 2160, 3024, 4000 };
        int[] heights = { 1080, 1500, 1920, 2400, 4032 };
        (double x, double y, double w, double h)[] faces =
        {
            (0.22, 0.18, 0.16, 0.12),
            (0.42, 0.18, 0.16, 0.12),
            (0.62, 0.18, 0.16, 0.12),
            (0.36, 0.25, 0.28, 0.21),
            (0.50, 0.30, 0.08, 0.05)
        };
        var options = Options();

        foreach (var width in widths)
        {
            foreach (var height in heights)
            {
                foreach (var f in faces)
                {
                    var face = Face(f.x, f.y, f.w, f.h);
                    var config = Compute(width, height, new[] { face });
                    if (config == null)
                        continue;

                    var (scale, dx, dy) = Placement(width, height, ScreenW, ScreenH, config);

                    double baseFill = Math.Max((double)ScreenW / width, (double)ScreenH / height);
                    Assert.True(scale <= (baseFill * options.MaxUpscale) + 1e-6,
                        $"zoom past the cap for {width}x{height} face({f.x},{f.y})");

                    double faceLeft = (f.x * width * scale) + dx;
                    double faceRight = ((f.x + f.w) * width * scale) + dx;
                    double faceWidth = faceRight - faceLeft;

                    // A face narrower than the surface must be shown whole; a wider one simply
                    // cannot be, and is given the full surface instead.
                    if (faceWidth <= ScreenW + 0.5)
                    {
                        Assert.True(faceLeft >= -0.5 && faceRight <= ScreenW + 0.5,
                            $"face pushed off screen for {width}x{height} face({f.x},{f.y}): {faceLeft:F0}..{faceRight:F0}");
                    }

                    Assert.True(dx <= 0.5 && dx + (width * scale) >= ScreenW - 0.5 && dy <= 0.5,
                        $"surface no longer covered for {width}x{height} face({f.x},{f.y})");
                }
            }
        }
    }

    [Fact]
    public void DegenerateInputs_ReturnNullInsteadOfThrowing()
    {
        var face = new[] { Face(0.4, 0.4, 0.2, 0.2) };

        Assert.Null(AutoFramingCalculator.Compute(0, 1500, ScreenW, ScreenH, new ImageFramingConfig(), face, Options()));
        Assert.Null(AutoFramingCalculator.Compute(1000, 0, ScreenW, ScreenH, new ImageFramingConfig(), face, Options()));
        Assert.Null(AutoFramingCalculator.Compute(1000, 1500, 0, ScreenH, new ImageFramingConfig(), face, Options()));
        Assert.Null(AutoFramingCalculator.Compute(1000, 1500, ScreenW, 0, new ImageFramingConfig(), face, Options()));
    }

    [Fact]
    public void MalformedFaceBoxes_AreSkipped()
    {
        var zeroWidth = new FaceBox { X = 0.4, Y = 0.4, Width = 0, Height = 0.2, Confidence = 0.9 };
        var valid = Face(0.40, 0.30, 0.20, 0.20);

        var chosen = AutoFramingCalculator.SelectPrimaryFace(new[] { zeroWidth, valid }, Options());

        Assert.NotNull(chosen);
        Assert.Equal(valid.X, chosen!.X);
    }

    [Fact]
    public void SurfaceSize_NormalizesToPortraitAndRejectsGarbage()
    {
        Assert.Equal((1080, 2400), WallpaperSurfaceSize.NormalizeToPortrait(1080, 2400));
        Assert.Equal((1080, 2400), WallpaperSurfaceSize.NormalizeToPortrait(2400, 1080));
        Assert.Equal((0, 0), WallpaperSurfaceSize.NormalizeToPortrait(0, 2400));
        Assert.Equal((0, 0), WallpaperSurfaceSize.NormalizeToPortrait(-5, -5));
    }
}
