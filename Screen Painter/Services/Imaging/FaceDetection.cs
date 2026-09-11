using System;
using System.Text.Json.Serialization;

namespace Screen_Painter.Services.Imaging;

/// <summary>
/// A detected face as a normalized rectangle (0..1, origin top-left) measured against the
/// image the detector actually ran on. Deliberately free of any platform type so the
/// auto-framing math stays unit-testable off-device.
/// </summary>
public class FaceBox
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonIgnore]
    public double Area => Width * Height;

    [JsonIgnore]
    public double CenterX => X + (Width / 2.0);

    [JsonIgnore]
    public double Bottom => Y + Height;

    [JsonIgnore]
    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>
/// Outcome of one detection pass. Carries the dimensions of the bitmap the faces were
/// measured against so normalized coordinates can never be reinterpreted against a
/// differently sized decode.
/// </summary>
public class FaceDetectionResult
{
    [JsonPropertyName("faces")]
    public FaceBox[] Faces { get; set; } = Array.Empty<FaceBox>();

    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }

    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    [JsonIgnore]
    public bool HasFaces => Faces != null && Faces.Length > 0;

    /// <summary>A result is only usable when it measured a real bitmap.</summary>
    [JsonIgnore]
    public bool IsUsable => ImageWidth > 0 && ImageHeight > 0;
}
