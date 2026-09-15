using System;
using System.Collections.Generic;
using Screen_Painter.Models;
using Screen_Painter.Services.Imaging;

namespace ScreenPainter.Tests;

/// <summary>
/// Exercises the pure auto-framing composition math. These tests deliberately re-implement the
/// placement contract of <c>WallpaperServiceAndroid.ApplyDeviceFullScreenFraming</c> so the
/// calculator is asserted against the geometry the Android service actually executes.
///
/// The agreed rule these tests encode: the crop is pan-only. The plain fill scale is both the
/// floor and the ceiling, so no picture is ever enlarged into a tighter crop, and the frame moves
/// only as far as the subject's own position requires.
/// </summary>
public class AutoFramingTests
{
    private const int ScreenW = 1080;
    private const int ScreenH = 2400;

    private static AutoFramingOptions Options() => new();

    private static FaceBox Face(double x, double y, double w, double h, double confidence = 0.9)
        => new() { X = x, Y = y, Width = w, Height = h, Confidence = confidence };

    /// <summary>
    /// Reproduces exactly what the wallpaper service does with the returned config: it derives its
    /// own fill scale from the source and target size, multiplies by the config's scale, centres the
    /// result, then adds the offsets.
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
    public void AHeadAlreadyClearOfBothEdges_LeavesThePictureExactlyWhereItWas()
    {
        // The behaviour change the feature was asked for: a head that is already comfortably inside
        // the frame is not dragged onto a fixed head line. It is left where the photographer put it.
        var config = Compute(1000, 1500, new[] { Face(0.42, 0.42, 0.16, 0.12) });
        Assert.NotNull(config);

        var (scale, dx, dy) = Placement(1000, 1500, ScreenW, ScreenH, config!);

        // The plain fill placement, untouched: the picture is centred and not zoomed.
        Assert.Equal(Math.Max((double)ScreenW / 1000, (double)ScreenH / 1500), scale, 6);
        Assert.Equal((ScreenW - (1000 * scale)) / 2.0, dx, 6);
        Assert.Equal((ScreenH - (1500 * scale)) / 2.0, dy, 6);

        double faceTopOnScreen = (0.42 * 1500 * scale) + dy;
        Assert.True(faceTopOnScreen >= 0, $"head must not be cropped (top={faceTopOnScreen})");
        Assert.True(faceTopOnScreen <= ScreenH / 2.0, $"head should sit above the middle (top={faceTopOnScreen})");
    }

    [Fact]
    public void AHeadTooCloseToTheBottomEdge_IsLiftedUntilItClears()
    {
        // Vertical movement has to be paid for out of vertical slack, and only a source narrower
        // than the phone surface has any: covering the screen with a wider source already keeps its
        // full height, so there is nothing left to slide.
        const int imageW = 1000;
        const int imageH = 2600;
        var face = Face(0.45, 0.90, 0.14, 0.08);

        var config = Compute(imageW, imageH, new[] { face });
        Assert.NotNull(config);

        var (scale, _, dy) = Placement(imageW, imageH, ScreenW, ScreenH, config!);

        double faceTopOnScreen = (face.Y * imageH * scale) + dy;
        double faceBottomOnScreen = ((face.Y + face.Height) * imageH * scale) + dy;

        Assert.True(faceTopOnScreen >= 0, $"head must not be cropped (top={faceTopOnScreen:F0})");
        Assert.True(faceBottomOnScreen <= ScreenH + 0.5,
            $"head must be pulled back into frame (bottom={faceBottomOnScreen:F0})");
    }

    [Fact]
    public void AnExtremeFace_IsBroughtJustClearOfTheEdge()
    {
        // A face far enough right that the plain fill placement leaves it past the margin. The frame
        // moves because the subject sits there, and no further than clearing the edge requires.
        var face = Face(0.90, 0.40, 0.06, 0.045);
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

        // And it comes to rest exactly on the edge margin, not in the middle: the frame is nudged,
        // not re-composed, so it spends the least movement that solves the problem.
        double margin = Options().EdgeMarginRatio * ScreenW;
        Assert.Equal(ScreenW - margin, faceRight, 3);
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
    public void ASubjectThePhotographerCentred_IsNotMovedAtAll()
    {
        // A subject already in the middle of the picture must not be moved for the sake of moving.
        // This source is narrower than the phone, so covering the screen keeps its full width and
        // the frame has no horizontal freedom at all — the plain fill placement is already right.
        var face = Face(0.50, 0.36, 0.06, 0.045);
        var config = Compute(1000, 3000, new[] { face });
        Assert.NotNull(config);

        var (scale, dx, _) = Placement(1000, 3000, ScreenW, ScreenH, config!);

        Assert.Equal((ScreenW - (1000 * scale)) / 2.0, dx, 6);

        double faceCentreOnScreen = ((0.50 + 0.03) * 1000 * scale) + dx;
        Assert.True(faceCentreOnScreen > 0 && faceCentreOnScreen < ScreenW,
            $"a centred subject must stay on screen, was {faceCentreOnScreen:F1}px");
    }

    [Fact]
    public void DifferentPhoneResolutions_ProduceDifferentPlacement()
    {
        // A subject that needs the frame to move, so the phone's own surface is what shapes the
        // result: the same picture is rendered at a different scale and moved a different distance
        // on a 1080x2400 phone than on a 1440x3120 one.
        var face = new[] { Face(0.62, 0.25, 0.10, 0.11) };

        var tall = AutoFramingCalculator.Compute(1000, 1500, 1080, 2400, new ImageFramingConfig(), face, Options());
        var taller = AutoFramingCalculator.Compute(1000, 1500, 1440, 3120, new ImageFramingConfig(), face, Options());

        Assert.NotNull(tall);
        Assert.NotNull(taller);

        var (tallScale, tallDx, _) = Placement(1000, 1500, 1080, 2400, tall!);
        var (tallerScale, tallerDx, _) = Placement(1000, 1500, 1440, 3120, taller!);

        Assert.NotEqual(tallScale, tallerScale, 3);
        Assert.NotEqual(tallDx, tallerDx, 3);

        // And each phone gets a subject that is fully in frame on its own surface.
        foreach (var (w, h, config) in new[] { (1080, 2400, tall!), (1440, 3120, taller!) })
        {
            var (scale, dx, _) = Placement(1000, 1500, w, h, config);
            double faceLeft = (face[0].X * 1000 * scale) + dx;
            double faceRight = ((face[0].X + face[0].Width) * 1000 * scale) + dx;

            Assert.True(faceLeft >= -0.5 && faceRight <= w + 0.5,
                $"subject not framed on {w}x{h}: {faceLeft:F0}..{faceRight:F0}");
        }
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
    public void AHandSetZoomBelowFill_IsRaisedBackToCoveringTheSurface()
    {
        // A zoom below the fill scale would uncover a screen edge. The user's zoom survives only as
        // far as it can without showing a black band.
        var manual = new ImageFramingConfig { Scale = 0.5 };

        var config = Compute(1000, 1500, new[] { Face(0.42, 0.40, 0.16, 0.12) }, manual);
        Assert.NotNull(config);
        Assert.Equal(1.0, config!.Scale, 6);
    }

    [Fact]
    public void WideLandscapeSource_PansToItsSubjectInsteadOfLeavingItUnderASlice()
    {
        // A landscape source wider than the phone surface is cut to a vertical slice by covering the
        // screen, and the slice keeps whichever part of the picture happens to sit under it. Panning
        // is what follows the subject instead — this is the case panning exists for.
        int[] widths = { 4000, 6000, 9000 };
        const int imageH = 2000;

        foreach (var width in widths)
        {
            var face = Face(0.85, 0.20, 0.10, 0.16);
            var config = Compute(width, imageH, new[] { face });
            Assert.NotNull(config);

            var (scale, dx, dy) = Placement(width, imageH, ScreenW, ScreenH, config!);
            double baseFill = Math.Max((double)ScreenW / width, (double)ScreenH / imageH);

            Assert.True(width * scale >= ScreenW - 0.5);
            Assert.True(imageH * scale >= ScreenH - 0.5);
            Assert.True(dx <= 0.5 && dx + (width * scale) >= ScreenW - 0.5, "no horizontal gaps allowed");
            Assert.True(dy <= 0.5 && dy + (imageH * scale) >= ScreenH - 0.5, "no vertical gaps allowed");

            // The crop is exactly the fill crop: no picture is spent on the framing.
            Assert.Equal(baseFill, scale, 6);

            // The subject is framed rather than left under whichever slice happens to cover the
            // screen. A subject at 85% of a 2:1 source is off screen entirely without the pan.
            double faceLeft = (face.X * width * scale) + dx;
            double faceRight = ((face.X + face.Width) * width * scale) + dx;
            Assert.True(faceLeft >= -0.5 && faceRight <= ScreenW + 0.5,
                $"subject not framed for {width}x{imageH}: {faceLeft:F0}..{faceRight:F0}");

            double unpannedDx = (ScreenW - (width * scale)) / 2.0;
            double unpannedFaceLeft = (face.X * width * scale) + unpannedDx;
            Assert.True(unpannedFaceLeft > ScreenW,
                $"{width}x{imageH} subject was already in frame without panning, so the case proves nothing");
        }
    }

    [Fact]
    public void DistantFace_IsNeverEnlarged()
    {
        // The rule: do not zoom in. A face small enough that the old composition would have
        // enlarged it toward a share of the screen is now left alone entirely.
        var face = new[] { Face(0.46, 0.30, 0.08, 0.05) };
        var config = Compute(4000, 6000, face);
        Assert.NotNull(config);

        double baseFill = Math.Max((double)ScreenW / 4000, (double)ScreenH / 6000);
        double applied = baseFill * config!.Scale;

        Assert.Equal(1.0, config.Scale, 6);
        Assert.Equal(baseFill, applied, 6);
    }

    [Fact]
    public void NoSourceShape_IsEverEnlargedPastThePlainFill()
    {
        // The blanket form of the rule, swept across source shapes either side of the phone's own
        // 9:20 surface so neither the wider nor the narrower case can slip a zoom through.
        int[] widths = { 640, 1000, 1080, 1440, 2160, 3024, 4000, 6000, 9000 };
        int[] heights = { 480, 1080, 1500, 1920, 2400, 2600, 4032, 5000 };
        (double x, double y, double w, double h)[] faces =
        {
            (0.02, 0.05, 0.06, 0.04),
            (0.30, 0.25, 0.10, 0.08),
            (0.50, 0.30, 0.08, 0.05),
            (0.62, 0.20, 0.20, 0.15),
            (0.92, 0.80, 0.06, 0.05)
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
                    double baseFill = Math.Max((double)ScreenW / width, (double)ScreenH / height);

                    Assert.Equal(baseFill, scale, 6);
                    Assert.True(dx <= 0.5 && dx + (width * scale) >= ScreenW - 0.5,
                        $"horizontal gap for {width}x{height} face({f.x},{f.y}) dx={dx:F2}");
                    Assert.True(dy <= 0.5 && dy + (height * scale) >= ScreenH - 0.5,
                        $"vertical gap for {width}x{height} face({f.x},{f.y}) dy={dy:F2}");
                }
            }
        }
    }

    [Fact]
    public void TwoCharacters_AreBothKeptInFrameAtEverySeparation()
    {
        // The defect that reached the user: only the largest face was framed, so a picture with two
        // characters was cropped onto whichever one happened to be marginally bigger and the other
        // was pushed off the screen. Both have to survive at any distance apart.
        const int imageW = 3024;
        const int imageH = 4032;
        const double faceW = 0.10;
        const double faceH = 0.075;

        // A pair that no longer fits the screen at all has to overhang, and the available slack
        // decides how much. What is asserted is that the overhang is even and small, not absent.
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
        }
    }

    [Fact]
    public void TwoCharacters_AreNeverCroppedTighterThanFill()
    {
        // Framing a pair must not be an excuse to crop hard: the pair is a subject like any other,
        // and the crop stays the plain fill crop.
        var a = Face(0.34, 0.20, 0.10, 0.075);
        var b = Face(0.56, 0.20, 0.10, 0.075);

        var config = Compute(3024, 4032, new[] { a, b });
        Assert.NotNull(config);

        Assert.Equal(1.0, config!.Scale, 6);
    }

    [Fact]
    public void APairTooWideForTheSurface_HasItsOverhangSplitEvenly()
    {
        // The one case where the frame is centred rather than nudged: a subject wider than the safe
        // zone has no clear position to be nudged to, so the loss is split rather than spent on one
        // side. Both characters stay visible.
        const int imageW = 3024;
        const int imageH = 4032;

        var a = Face(0.02, 0.20, 0.10, 0.075);
        var b = Face(0.88, 0.20, 0.10, 0.075);

        var config = Compute(imageW, imageH, new[] { a, b });
        Assert.NotNull(config);

        var (scale, dx, _) = Placement(imageW, imageH, ScreenW, ScreenH, config!);

        double bandLeft = (a.X * imageW * scale) + dx;
        double bandRight = ((b.X + b.Width) * imageW * scale) + dx;

        // The pair is wider than the screen, so it must overhang — evenly, not on one side only.
        Assert.True(bandRight - bandLeft > ScreenW, "the pair must be wider than the surface for this case");
        Assert.Equal(ScreenW - bandRight, bandLeft, 3);
    }

    [Fact]
    public void APortraitSource_IsNotEnlarged()
    {
        // Covering a 20:9 surface with a 3:4 photo already spends a quarter of its width on the shape
        // change. The old composition responded by cropping further; now it crops nothing at all.
        (double x, double y, double w, double h)[] faces =
        {
            (0.42, 0.18, 0.16, 0.12),
            (0.44, 0.30, 0.12, 0.09),
            (0.40, 0.45, 0.20, 0.15),
            (0.46, 0.55, 0.08, 0.06),
            (0.40, 0.10, 0.10, 0.08)
        };

        (int w, int h)[] sources = { (1080, 1440), (1440, 1920), (3024, 4032), (4000, 3000), (3000, 4000) };

        foreach (var (width, height) in sources)
        {
            foreach (var f in faces)
            {
                var config = Compute(width, height, new[] { Face(f.x, f.y, f.w, f.h) });
                Assert.NotNull(config);

                Assert.Equal(1.0, config!.Scale, 6);
            }
        }
    }

    [Fact]
    public void ATallSubjectBand_IsNotDraggedOutOfFrame()
    {
        // A source narrower than the phone, so vertical slack exists and the vertical rule can act.
        // Lifting the subject must not carry the head off the top, and leaving it alone must not
        // push it off the bottom.
        const int imageW = 2000;
        const int imageH = 5000;
        double torso = Options().TorsoExtendRatio;

        (double y, double h)[] faces = { (0.05, 0.12), (0.30, 0.15), (0.55, 0.15), (0.75, 0.15) };

        foreach (var (y, h) in faces)
        {
            var face = Face(0.45, y, 0.12, h);
            var config = Compute(imageW, imageH, new[] { face });
            Assert.NotNull(config);

            var (scale, _, dy) = Placement(imageW, imageH, ScreenW, ScreenH, config!);

            double bandTop = (y * imageH * scale) + dy;
            double bandBottom = (Math.Min(y + h + (h * torso), 1.0) * imageH * scale) + dy;

            Assert.True(bandTop >= -0.5, $"head cropped off the top (top={bandTop:F0})");
            Assert.True(bandBottom <= ScreenH + 0.5, $"subject dragged off the bottom (bottom={bandBottom:F0})");
        }
    }

    [Fact]
    public void NoSourceOrFacePosition_IsPushedToAScreenEdge()
    {
        // The defect that reached the user: the crop was clamped into the range that covers the
        // surface, which for a wide picture threw the subject hard against a screen edge — a face
        // at 51% of the source was rendered at 76-86% of the screen. Whatever the geometry, the
        // whole face has to stay in frame and the crop has to stay at the plain fill scale.
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

                    double baseFill = Math.Max((double)ScreenW / width, (double)ScreenH / height);
                    Assert.Equal(baseFill, scale, 6);

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
