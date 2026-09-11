using System.Threading;
using System.Threading.Tasks;

namespace Screen_Painter.Services.Imaging;

/// <summary>
/// Detects faces in an image file. Implemented per platform; the shared auto-framing logic only
/// ever sees <see cref="FaceBox"/> values, so it stays testable off-device.
/// </summary>
public interface IFaceDetector
{
    /// <summary>
    /// Analyses <paramref name="imagePath"/> and returns the faces found, or <c>null</c> when the
    /// file cannot be analysed. Implementations must never throw: auto-framing is best-effort and
    /// a failure has to fall back to the user's existing framing.
    /// </summary>
    Task<FaceDetectionResult?> DetectAsync(
        string imagePath,
        AutoFramingOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identity of the detection model and preprocessing. It is part of every cache key, so bumping
/// it invalidates previously cached detections instead of reusing results from an older model.
/// Kept free of platform types so the cache stays unit-testable.
/// </summary>
public static class FaceDetectorModel
{
    /// <summary>
    /// Bundled MediaPipe BlazeFace short-range model, shipped as a MAUI raw asset so it lands at
    /// the APK asset root and can be addressed by name from MediaPipe's native runtime.
    /// </summary>
    public const string AssetName = "blaze_face_short_range.tflite";

    /// <summary>Bump when the bundled model asset or the detector's preprocessing changes.</summary>
    public const string Version = "blazeface-short-range-1";

    /// <summary>
    /// A detector that never finds anything. Registered on non-Android targets, where wallpaper
    /// management is unsupported anyway, so the pipeline behaves exactly as it did before.
    /// </summary>
    public class Null : IFaceDetector
    {
        public Task<FaceDetectionResult?> DetectAsync(
            string imagePath,
            AutoFramingOptions options,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<FaceDetectionResult?>(null);
        }
    }
}
