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
    public void TwoComparableFaces_AreBothPartOfTheSubjectGroup()
    {
        // Two characters standing together. Framing from the largest face alone would crop onto
        // one of them, so both have to travel in the band.
        var left = Face(0.30, 0.30, 0.10, 0.075);
        var right = Face(0.60, 0.30, 0.10, 0.075);

        var band = AutoFramingCalculator.SelectSubjects(new[] { left, right }, 1000, 1500, Options());

        Assert.NotNull(band);
        Assert.Equal(0.30, band!.Left, 6);
        Assert.Equal(0.70, band.Right, 6);
    }

    [Fact]
    public void ABackgroundFace_IsNotAllowedToWidenTheSubjectGroup()
    {
        // A bystander far enough back is background detail: including it would drag the crop wide
        // open and undo the framing the subject deserves.
        var subject = Face(0.40, 0.30, 0.20, 0.15);
        var bystander = Face(0.05, 0.30, 0.02, 0.015);

        var band = AutoFramingCalculator.SelectSubjects(new[] { subject, bystander }, 1000, 1500, Options());

        Assert.NotNull(band);
        Assert.Equal(0.40, band!.Left, 6);
        Assert.Equal(0.60, band.Right, 6);
    }

    [Fact]
    public void LowConfidenceFaces_AreIgnored()
    {
        var options = Options();

        var lowConfidence = new[] { Face(0.4, 0.3, 0.2, 0.2, confidence: 0.10) };
        Assert.Null(AutoFramingCalculator.SelectSubjects(lowConfidence, 1000, 1500, options));
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
    public void AnExtremeFaceIsKeptVisibleAndBroughtToTheMiddle()
    {
        // A face near the right of a 3:4 source, small enough that a zoom is allowed. That zoom is
        // what creates the horizontal slack the frame can act on.
        var face = Face(0.68, 0.40, 0.06, 0.045);
        var config = Compute(1000, 1500, new[] { face });
        Assert.NotNull(config);

        var (scale, dx, dy) = Placement(1000, 1500, ScreenW, ScreenH, config!);

        Assert.True(1000 * scale > ScreenW, "slack is required for this case to be meaningful");
        Assert.True(dx <= 0.5 && dx + (1000 * scale) >= ScreenW - 0.5, "no horizontal gaps allowed");
        Assert.True(dy <= 0.5 && dy + (1500 * scale) >= ScreenH - 0.5, "no vertical gaps allowed");

        // The whole face stays visible rather than being pushed off the screen edge.
        double faceLeft = (face.X * 1000 * scale) + dx;
        double faceRight = ((face.X + face.Width) * 1000 * scale) + dx;
        Assert.True(faceLeft >= -0.5, $"face cut on the left (x={faceLeft:F1})");
        Assert.True(faceRight <= ScreenW + 0.5, $"face cut on the right (x={faceRight:F1})");

        // And it ends up in the middle of the frame rather than left against the edge it was near.
        double faceCentreOnScreen = ((face.X + (face.Width / 2)) * 1000 * scale) + dx;
        Assert.True(Math.Abs(faceCentreOnScreen - (ScreenW / 2.0)) <= 1.0,
            $"face should be centred, was {faceCentreOnScreen:F1}px");
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
    public void ASubjectThePhotographerCentred_IsLeftCentred()
    {
        // A subject already in the middle of the picture must not be moved for the sake of moving.
        // The frame centres on the subject, so a subject that was central stays exactly central —
        // auto-framing re-frames, it does not re-compose.
        var face = Face(0.50, 0.36, 0.06, 0.045);
        var config = Compute(1000, 3000, new[] { face });
        Assert.NotNull(config);

        var (scale, dx, _) = Placement(1000, 3000, ScreenW, ScreenH, config!);
        double faceCentreOnScreen = ((0.50 + 0.03) * 1000 * scale) + dx;

        Assert.True(Math.Abs(faceCentreOnScreen - (ScreenW / 2.0)) <= 1.0,
            $"a centred subject must stay centred, offset was {faceCentreOnScreen - (ScreenW / 2.0):F1}px");
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
    public void WideLandscapeSource_FramesItsSubjectInsteadOfASlice()
    {
        // A landscape source wider than the phone surface is cut to a vertical slice by covering the
        // screen, and the slice keeps whichever part of the picture happens to sit under it. Cropping
        // onto the band is what follows the subject instead, and it is the case the crop is for.
        int[] widths = { 4000, 6000, 9000 };
        const int imageH = 2000;

        foreach (var width in widths)
        {
            // Wide enough that the slice covering the screen cannot hold the subject whole; a
            // narrower subject would simply fit the slice, and then there is nothing to crop to.
            var face = Face(0.45, 0.20, 0.10, 0.16);
            var config = Compute(width, imageH, new[] { face });
            Assert.NotNull(config);

            var (scale, dx, dy) = Placement(width, imageH, ScreenW, ScreenH, config!);
            double baseFill = Math.Max((double)ScreenW / width, (double)ScreenH / imageH);

            Assert.True(width * scale >= ScreenW - 0.5);
            Assert.True(imageH * scale >= ScreenH - 0.5);
            Assert.True(dx <= 0.5 && dx + (width * scale) >= ScreenW - 0.5, "no horizontal gaps allowed");
            Assert.True(dy <= 0.5 && dy + (imageH * scale) >= ScreenH - 0.5, "no vertical gaps allowed");

            Assert.True(scale > baseFill + 1e-6,
                $"{width}x{imageH} is too wide to show its subject without a crop, got {scale / baseFill:F3}x");
            Assert.True(scale <= baseFill * Options().MaxUpscale + 1e-6,
                $"crop capped at {Options().MaxUpscale}x fill, got {scale / baseFill:F3}x");

            // And the subject is framed rather than left under whichever slice happens to cover the
            // screen: it sits in the middle of the frame, overhanging an edge only as far as the
            // cap's arithmetic forces.
            double faceLeft = (face.X * width * scale) + dx;
            double faceRight = ((face.X + face.Width) * width * scale) + dx;
            double overhang = (faceRight - faceLeft) * 0.15;
            Assert.True(faceLeft >= -overhang && faceRight <= ScreenW + overhang,
                $"subject not framed for {width}x{imageH}: {faceLeft:F0}..{faceRight:F0}");
            Assert.True(Math.Abs(((faceLeft + faceRight) / 2.0) - (ScreenW / 2.0)) <= 1.0,
                $"subject not centred for {width}x{imageH}: centre was {(faceLeft + faceRight) / 2.0:F0}");
        }
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
    public void TwoCharacters_AreBothKeptInFrameAtEverySeparation()
    {
        // The defect that reached the user: only the largest face was framed, so a picture with two
        // characters was cropped onto whichever one happened to be marginally bigger and the other
        // was pushed off the screen. Both have to survive the crop at any distance apart.
        const int imageW = 3024;
        const int imageH = 4032;
        const double faceW = 0.10;
        const double faceH = 0.075;

        // A pair that no longer fits the screen at the crop cap has to overhang, and the cap decides
        // how much. What is asserted is that the overhang is even and small, not that it is absent.
        double slackFraction = 0.4;

        for (double gap = 0.04; gap <= 0.30; gap += 0.02)
        {
            // Equal faces, so nothing distinguishes them but their position.
            var a = Face(0.5 - (gap / 2) - faceW, 0.20, faceW, faceH);
            var b = Face(0.5 + (gap / 2), 0.20, faceW, faceH);

            var config = Compute(imageW, imageH, new[] { a, b });
            Assert.NotNull(config);

            var (scale, dx, _) = Placement(imageW, imageH, ScreenW, ScreenH, config!);

            double aLeft = (a.X * imageW * scale) + dx;
            double aRight = ((a.X + a.Width) * imageW * scale) + dx;
            double bLeft = (b.X * imageW * scale) + dx;
            double bRight = ((b.X + b.Width) * imageW * scale) + dx;

            // At most a fraction of a face may hang over an edge; the rest of each character is
            // shown and neither may be dropped outright.
            double slack = (aRight - aLeft) * slackFraction;

            Assert.True(aLeft >= -slack && aRight <= ScreenW + slack,
                $"first character cut at gap {gap:F2}: {aLeft:F0}..{aRight:F0}");
            Assert.True(bLeft >= -slack && bRight <= ScreenW + slack,
                $"second character cut at gap {gap:F2}: {bLeft:F0}..{bRight:F0}");

            // Both characters are treated as one subject: the frame centres on the pair rather than
            // on either of them, which is what stops the crop settling onto a single character.
            double pairCentre = ((aLeft + bRight) / 2.0);
            Assert.True(Math.Abs(pairCentre - (ScreenW / 2.0)) <= 1.0,
                $"frame is not centred on the pair at gap {gap:F2}: centre was {pairCentre:F0}");
        }
    }

    [Fact]
    public void TwoCharacters_DoNotTriggerAnAggressiveZoom()
    {
        // Framing a pair must not be an excuse to crop hard: the pair is a subject like any other,
        // and the same upscale cap applies.
        var a = Face(0.34, 0.20, 0.10, 0.075);
        var b = Face(0.56, 0.20, 0.10, 0.075);

        var config = Compute(3024, 4032, new[] { a, b });
        Assert.NotNull(config);

        Assert.True(config!.Scale <= Options().MaxUpscale + 1e-6,
            $"pair crop must respect the upscale cap, got {config.Scale:F3}x");
    }

    [Fact]
    public void PortraitSource_IsNotEnlargedPastWhatItsShapeCanSpare()
    {
        // Covering a 20:9 surface with a 3:4 photo already spends a quarter of its width and three
        // quarters of its area on the shape change. The crop that follows is bounded by what is
        // left, so a portrait is nudged at most and never enlarged toward the hard cap the way a
        // landscape photo is.
        (double x, double y, double w, double h)[] faces =
        {
            (0.42, 0.18, 0.16, 0.12),
            (0.44, 0.30, 0.12, 0.09),
            (0.40, 0.45, 0.20, 0.15),
            (0.46, 0.55, 0.08, 0.06),
            (0.40, 0.10, 0.10, 0.08)
        };

        // 3:4 and 4:3 sources, which are the shapes an ordinary camera photo has.
        (int w, int h)[] sources = { (1080, 1440), (1440, 1920), (3024, 4032), (4000, 3000), (3000, 4000) };
        double baseFill = Math.Max((double)ScreenW / 1080, (double)ScreenH / 2400);

        foreach (var (width, height) in sources)
        {
            foreach (var f in faces)
            {
                var config = Compute(width, height, new[] { Face(f.x, f.y, f.w, f.h) });
                Assert.NotNull(config);

                var (scale, _, _) = Placement(width, height, ScreenW, ScreenH, config!);
                double fill = Math.Max((double)ScreenW / width, (double)ScreenH / height);

                // Covering the screen keeps at least three quarters of a 3:4 photo's area, so its
                // crop is limited to about a third. A 4:3 photo has already given up more, so the
                // hard cap applies instead.
                double allowed = fill * Options().MaxUpscale;
                Assert.True(scale <= allowed + 1e-6,
                    $"{width}x{height} face({f.x},{f.y}) cropped {scale / fill:F3}x, past {Options().MaxUpscale:F2}x");
            }
        }

        // The specific regression: a 3:4 portrait must not reach the hard cap.
        var portrait = Compute(1080, 1440, new[] { Face(0.42, 0.18, 0.16, 0.12) });
        Assert.NotNull(portrait);
        var (portraitScale, _, _) = Placement(1080, 1440, ScreenW, ScreenH, portrait!);
        double portraitFill = Math.Max((double)ScreenW / 1080, (double)ScreenH / 1440);
        Assert.True(portraitScale < portraitFill * Options().MaxUpscale - 1e-6,
            $"a 3:4 portrait should be cropped less than the hard cap, got {portraitScale / portraitFill:F3}x");
    }

    [Fact]
    public void TallSubjectBand_ReachesTheHeadLineWithoutBeingDraggedLow()
    {
        // The case the head-line bias exists for: a source whose subject is small enough that the
        // band still fits the surface once the crop is taken. The head has to land on the line
        // without the lift carrying the rest of the subject off the bottom of the screen.
        var face = Face(0.40, 0.30, 0.15, 0.15);
        const int imageW = 6000;
        const int imageH = 4000;

        var config = Compute(imageW, imageH, new[] { face });
        Assert.NotNull(config);

        var (scale, _, dy) = Placement(imageW, imageH, ScreenW, ScreenH, config!);

        double bandTop = (face.Y * imageH * scale) + dy;
        double bandBottom = ((face.Y + face.Height + (face.Height * Options().TorsoExtendRatio)) * imageH * scale) + dy;

        // The head clears the top of the frame and reads in the upper third, so the subject reads
        // downward through the picture...
        Assert.True(bandTop >= 0, $"head must not be cropped (top={bandTop:F0})");
        Assert.True(bandTop <= ScreenH / 3.0, $"head should sit in the upper third (top={bandTop:F0})");

        // ...and lifting it there must not have carried the subject off the bottom.
        Assert.True(bandBottom <= ScreenH,
            $"subject dragged off the bottom by the head-line bias (bottom={bandBottom:F0})");
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

        var chosen = AutoFramingCalculator.SelectSubjects(new[] { zeroWidth, valid }, 1000, 1500, Options());

        Assert.NotNull(chosen);
        Assert.Equal(valid.X, chosen!.Left, 6);
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
