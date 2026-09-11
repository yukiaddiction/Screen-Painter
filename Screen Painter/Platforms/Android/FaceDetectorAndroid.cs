#if ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Android.Graphics;
using Microsoft.Maui.ApplicationModel;
using Screen_Painter.Services.Imaging;
using MpBaseOptions = MediaPipe.Tasks.Core.BaseOptions;
using MpBitmapImageBuilder = MediaPipe.Framework.Image.BitmapImageBuilder;
using MpDelegate = MediaPipe.Tasks.Core.Delegates;
using MpDetection = MediaPipe.Tasks.Components.Containers.Detection;
using MpFaceDetector = MediaPipe.Tasks.Vision.FaceDetector.FaceDetector;
// The binding keeps the generated options type nested inside the detector class.
using MpFaceDetectorOptions = MediaPipe.Tasks.Vision.FaceDetector.FaceDetector.FaceDetectorOptions;
using MpRunningMode = MediaPipe.Tasks.Vision.Core.RunningMode;

namespace Screen_Painter.Platforms.Android;

/// <summary>
/// On-device face detection for auto-framing, backed by MediaPipe Tasks Vision and the bundled
/// BlazeFace short-range model. Fully offline: no network access, and no image ever leaves the
/// device.
///
/// <para>
/// The Android Context is resolved lazily rather than injected, because this service is also
/// constructed from receivers and the foreground service, where no Activity exists.
/// </para>
/// </summary>
[SupportedOSPlatform("android")]
public class FaceDetectorAndroid : IFaceDetector
{
    private readonly object _initLock = new();
    private MpFaceDetector? _detector;
    private bool _initFailed;
    private float _configuredConfidence = -1f;

    public Task<FaceDetectionResult?> DetectAsync(
        string imagePath,
        AutoFramingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(imagePath))
            return Task.FromResult<FaceDetectionResult?>(null);

        // MediaPipe inference is blocking native work; keep it off the caller's thread.
        return Task.Run(() => DetectInternal(imagePath, options, cancellationToken), cancellationToken);
    }

    private FaceDetectionResult? DetectInternal(
        string imagePath,
        AutoFramingOptions options,
        CancellationToken cancellationToken)
    {
        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        if (context == null)
            return null;

        // Two passes: read the real dimensions first, then decode a bounded bitmap.
        var (width, height) = ReadImageSize(context, imagePath);
        if (width <= 0 || height <= 0)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = DecodeBounded(context, imagePath, width, height);
        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;

        var detector = GetDetector(context, options);
        if (detector == null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        using var mpImage = new MpBitmapImageBuilder(bitmap).Build();
        if (mpImage == null)
            return null;

        using var result = detector.Detect(mpImage);
        if (result == null)
            return null;

        // The binding suppresses the plain `detections()` getter because the Java class also exposes
        // `detections(int)`, so the generated member is the invoker instead of a property.
        var detections = result.Detections();
        if (detections == null || detections.Count == 0)
        {
            // A real "no face here" answer, recorded so it is not re-analysed every rotation.
            return new FaceDetectionResult
            {
                Faces = Array.Empty<FaceBox>(),
                ImageWidth = bitmap.Width,
                ImageHeight = bitmap.Height
            };
        }

        // The bitmap was decoded down to at most PreferredDecodeLongEdge, so a face that is still
        // tiny after that is background detail rather than a subject.
        int longEdge = Math.Max(bitmap.Width, bitmap.Height);
        double minFaceFraction = (double)Math.Max(1, options?.MinFaceSizeInModelPixels ?? 24) / longEdge;

        var faces = new List<FaceBox>(detections.Count);
        foreach (var detection in detections)
        {
            // These Java getters are surfaced as methods, not properties: the classes also expose
            // `boundingBox()`/`score()` style accessors, which suppresses the generated properties.
            var box = detection?.BoundingBox();
            if (box == null)
                continue;

            double w = box.Width();
            double h = box.Height();
            if (w <= 0 || h <= 0)
                continue;

            double normW = w / bitmap.Width;
            double normH = h / bitmap.Height;
            if (normW < minFaceFraction || normH < minFaceFraction)
                continue;

            faces.Add(new FaceBox
            {
                X = Clamp01(box.Left / bitmap.Width),
                Y = Clamp01(box.Top / bitmap.Height),
                Width = normW,
                Height = normH,
                Confidence = ReadConfidence(detection)
            });
        }

        return new FaceDetectionResult
        {
            // Face boxes are normalized against the bitmap MediaPipe actually saw, which is why
            // these dimensions travel with the result.
            Faces = faces.ToArray(),
            ImageWidth = bitmap.Width,
            ImageHeight = bitmap.Height
        };
    }

    /// <summary>
    /// Highest score across the detection's categories. BlazeFace emits a single "face" category,
    /// but reading the list keeps this correct if the model is ever swapped.
    /// </summary>
    private static double ReadConfidence(MpDetection detection)
    {
        // Same reason as Detections(): `categories(int)` suppresses the `categories()` getter.
        var categories = detection.Categories();
        if (categories == null || categories.Count == 0)
            return 1.0;

        double best = 0.0;
        foreach (var category in categories)
        {
            var score = category?.Score() ?? 0f;
            if (score > best)
                best = score;
        }

        return best;
    }

    private static double Clamp01(double value)
    {
        if (value < 0) return 0;
        if (value > 1) return 1;
        return value;
    }

    /// <summary>
    /// Builds (and caches) the MediaPipe detector. Rebuilt only when the confidence floor changes,
    /// since that is the sole option this project varies at runtime.
    /// </summary>
    private MpFaceDetector? GetDetector(global::Android.Content.Context context, AutoFramingOptions options)
    {
        float confidence = (float)Math.Clamp(options?.MinConfidence ?? 0.5, 0.0, 1.0);

        lock (_initLock)
        {
            if (_detector != null && Math.Abs(_configuredConfidence - confidence) < float.Epsilon)
                return _detector;

            if (_initFailed && _detector == null)
                return null;

            try
            {
                _detector?.Close();
                _detector = null;

                var baseOptions = MpBaseOptions.InvokeBuilder()
                    .SetModelAssetPath(FaceDetectorModel.AssetName)
                    // CPU only: this runs once per wallpaper on a downscaled bitmap, where CPU is
                    // already fast enough, and it avoids the GPU delegate's extra native surface.
                    .SetDelegate(MpDelegate.Cpu)
                    .Build();

                var detectorOptions = MpFaceDetectorOptions.InvokeBuilder()
                    .SetBaseOptions(baseOptions)
                    .SetRunningMode(MpRunningMode.Image)
                    .SetMinDetectionConfidence(confidence)
                    .Build();

                _detector = MpFaceDetector.CreateFromOptions(context, detectorOptions);
                _configuredConfidence = confidence;
                return _detector;
            }
            catch (Exception ex)
            {
                // Most likely the model asset is missing from the APK. Fail once and keep failing
                // quietly: auto-framing then degrades to the user's existing manual framing.
                System.Diagnostics.Debug.WriteLine($"[FaceDetectorAndroid] init failed: {ex}");
                _initFailed = true;
                _detector = null;
                return null;
            }
        }
    }

    private static (int width, int height) ReadImageSize(global::Android.Content.Context context, string imagePath)
    {
        try
        {
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            using (var stream = OpenStream(context, imagePath))
            {
                if (stream == null) return (0, 0);
                BitmapFactory.DecodeStream(stream, null, options);
            }

            return (options.OutWidth, options.OutHeight);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Decodes the image no larger than <see cref="AutoFramingOptions.PreferredDecodeLongEdge"/> on
    /// its long edge, halving with BitmapFactory's inSampleSize. Detection does not need full
    /// resolution, and this is what keeps inference memory-bounded on a very large photo.
    /// </summary>
    private static Bitmap? DecodeBounded(
        global::Android.Content.Context context,
        string imagePath,
        int width,
        int height)
    {
        try
        {
            int sampleSize = 1;
            int longEdge = Math.Max(width, height);
            while (longEdge / sampleSize > AutoFramingOptions.PreferredDecodeLongEdge)
            {
                sampleSize *= 2;
            }

            var decodeOptions = new BitmapFactory.Options
            {
                InSampleSize = sampleSize,
                InPreferredConfig = Bitmap.Config.Argb8888,
                InScaled = false
            };
#pragma warning disable CA1422
            decodeOptions.InDither = false;
#pragma warning restore CA1422

            using var stream = OpenStream(context, imagePath);
            if (stream == null)
                return null;

            return BitmapFactory.DecodeStream(stream, null, decodeOptions);
        }
        catch
        {
            return null;
        }
    }

    private static Stream? OpenStream(global::Android.Content.Context context, string imagePath)
    {
        try
        {
            if (imagePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = global::Android.Net.Uri.Parse(imagePath);
                if (uri != null)
                    return context.ContentResolver?.OpenInputStream(uri);
            }

            if (File.Exists(imagePath))
                return File.OpenRead(imagePath);
        }
        catch
        {
        }

        return null;
    }
}
#endif
