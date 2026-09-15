using Screen_Painter.Services.Imaging;

namespace ScreenPainter.Tests;

/// <summary>
/// Exercises the gate that runs in front of face detection.
///
/// The defect it exists for: a picture already at the phone's own resolution was handed to the
/// composition math, which could offer it nothing but a crop — so it was cropped, panned and
/// enlarged for no reason at all. These tests pin the two cases that must never reach that math.
/// </summary>
public class AutoFramingGateTests
{
    private const int ScreenW = 1080;
    private const int ScreenH = 2400;

    private static AutoFramingOptions Options() => new();

    private static AutoFramingDecision Classify(int w, int h, AutoFramingOptions? options = null)
        => AutoFramingGate.Classify(w, h, ScreenW, ScreenH, options ?? Options());

    [Fact]
    public void APhoneSizedPicture_NeedsNoFraming()
    {
        // The exact regression: this is the picture that was being cropped to 74% of its area.
        Assert.Equal(AutoFramingDecision.AlreadyMatches, Classify(1080, 2400));
    }

    [Fact]
    public void APictureCloseToThePhoneSize_NeedsNoFraming()
    {
        // Within a fifth of the surface in both dimensions: re-framing could only lose picture.
        Assert.Equal(AutoFramingDecision.AlreadyMatches, Classify(1000, 2300));
    }

    [Fact]
    public void APictureExactlyAtTheSizeTolerance_StaysAlreadyMatching()
    {
        // 1296/1080 and 2880/2400 are both exactly 1.20, the tolerance boundary.
        Assert.Equal(AutoFramingDecision.AlreadyMatches, Classify(1296, 2880));
    }

    [Fact]
    public void APictureJustPastTheSizeTolerance_FallsToTheShapeTier()
    {
        // 1297/1080 is past 1.20, so the size promise no longer holds — but the shape is within
        // 0.08% of the surface's, so the plain fill is still a pure downscale.
        Assert.Equal(AutoFramingDecision.ShapeMatches, Classify(1297, 2880));
    }

    [Fact]
    public void AHighResolutionPictureOfThePhoneShape_OnlyNeedsAScale()
    {
        // 2160x4800 is exactly 9:20, the same shape as a 1080x2400 phone, so covering the screen
        // crops nothing at all.
        Assert.Equal(AutoFramingDecision.ShapeMatches, Classify(2160, 4800));
    }

    [Fact]
    public void AVeryLargePictureOfThePhoneShape_OnlyNeedsAScale()
    {
        // 4000x8889 is 0.45000 to five decimals. Far too large for the size tier, but its shape is
        // the phone's, so it needs nothing but a uniform downscale.
        Assert.Equal(AutoFramingDecision.ShapeMatches, Classify(4000, 8889));
    }

    [Fact]
    public void ALandscapePictureCarryingThePortraitPixelCount_IsNotThePhoneSize()
    {
        // 2400x1080 holds a 1080x2400 phone's pixel count transposed, which is a 2:1 crop waiting
        // to happen rather than a picture that already fits. This is why the size tier compares
        // against the surface in the picture's own orientation instead of normalizing first.
        Assert.Equal(AutoFramingDecision.NeedsFraming, Classify(2400, 1080));
    }

    [Fact]
    public void APictureWhoseShapeIsFarOff_NeedsFraming()
    {
        // A 4:3 photo on a 9:20 surface: covering the screen discards most of its height, so the
        // subject's position genuinely decides the result.
        Assert.Equal(AutoFramingDecision.NeedsFraming, Classify(4000, 3000));
    }

    [Fact]
    public void APictureJustOutsideTheShapeTolerance_NeedsFraming()
    {
        // 4000x8620 is 0.46404 against the surface's 0.45 — 3.12%, just past the 3% allowance.
        Assert.Equal(AutoFramingDecision.NeedsFraming, Classify(4000, 8620));
    }

    [Fact]
    public void APictureJustInsideTheShapeTolerance_OnlyNeedsAScale()
    {
        // 4000x8660 is 0.46189 — 2.64%, inside the allowance.
        Assert.Equal(AutoFramingDecision.ShapeMatches, Classify(4000, 8660));
    }

    [Fact]
    public void Tolerances_AreDrivenByOptions()
    {
        // With the size tier switched off, a slightly mismatched picture has to earn its place on
        // shape alone — and a shape allowance wide enough then lets it through.
        var strictSize = new AutoFramingOptions { SizeToleranceRatio = 0.0 };

        // 1000x2300 is 0.43478 against 0.45 — 3.38%, outside the default 3%.
        Assert.Equal(AutoFramingDecision.NeedsFraming, Classify(1000, 2300, strictSize));

        var looseShape = new AutoFramingOptions
        {
            SizeToleranceRatio = 0.0,
            ShapeToleranceRatio = 0.05
        };
        Assert.Equal(AutoFramingDecision.ShapeMatches, Classify(1000, 2300, looseShape));
    }

    [Fact]
    public void AnExactSizeMatch_SurvivesAZeroSizeTolerance()
    {
        var exactOnly = new AutoFramingOptions { SizeToleranceRatio = 0.0 };
        Assert.Equal(AutoFramingDecision.AlreadyMatches, Classify(1080, 2400, exactOnly));
    }

    [Fact]
    public void UnknownGeometry_FallsThroughToFraming()
    {
        // A picture whose dimensions could not be read must be analysed, never waved through:
        // "already matches" is a promise that no framing is needed.
        Assert.Equal(AutoFramingDecision.NeedsFraming, AutoFramingGate.Classify(0, 2400, ScreenW, ScreenH, Options()));
        Assert.Equal(AutoFramingDecision.NeedsFraming, AutoFramingGate.Classify(1080, 0, ScreenW, ScreenH, Options()));
        Assert.Equal(AutoFramingDecision.NeedsFraming, AutoFramingGate.Classify(-1, -1, ScreenW, ScreenH, Options()));
        Assert.Equal(AutoFramingDecision.NeedsFraming, AutoFramingGate.Classify(1080, 2400, 0, 2400, Options()));
        Assert.Equal(AutoFramingDecision.NeedsFraming, AutoFramingGate.Classify(1080, 2400, ScreenW, 0, Options()));
    }

    [Fact]
    public void MissingOptions_FallBackToTheDocumentedTolerances()
    {
        Assert.Equal(AutoFramingDecision.AlreadyMatches, AutoFramingGate.Classify(1080, 2400, ScreenW, ScreenH, null));
        Assert.Equal(AutoFramingDecision.ShapeMatches, AutoFramingGate.Classify(2160, 4800, ScreenW, ScreenH, null));
        Assert.Equal(AutoFramingDecision.NeedsFraming, AutoFramingGate.Classify(4000, 3000, ScreenW, ScreenH, null));
    }
}
