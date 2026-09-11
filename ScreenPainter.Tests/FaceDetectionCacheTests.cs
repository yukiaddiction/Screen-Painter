using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Screen_Painter.Services.Imaging;

namespace ScreenPainter.Tests;

/// <summary>
/// Covers the detection cache, which is what makes the feature "learn" an image instead of
/// re-analysing it on every rotation. The cloud behaviour matters most here: cached wallpaper
/// files are deleted after each apply and re-downloaded under the same hashed name, so a
/// path-keyed cache would never hit.
/// </summary>
public class FaceDetectionCacheTests : IDisposable
{
    private readonly string _dir;
    private readonly string _cacheFile;

    public FaceDetectionCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sp_facecache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cacheFile = Path.Combine(_dir, "face_detections.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
        GC.SuppressFinalize(this);
    }

    private FaceDetectionCache CreateCache(TimeSpan? ttl = null, int maxEntries = 100)
        => new(_cacheFile, null!, ttl ?? TimeSpan.FromDays(30), maxEntries);

    private string CreateImageFile(long bytes = 8 * 1024)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static FaceBox[] Faces(params (double X, double Y)[] centres)
    {
        var list = new List<FaceBox>();
        foreach (var (x, y) in centres)
            list.Add(new FaceBox { X = x, Y = y, Width = 0.1, Height = 0.1, Confidence = 0.9 });
        return list.ToArray();
    }

    private sealed class CountingDetector : IFaceDetector
    {
        private readonly FaceBox[] _faces;
        private readonly bool _returnsNothing;

        public CountingDetector(FaceBox[] faces, bool returnsNothing = false)
        {
            _faces = faces;
            _returnsNothing = returnsNothing;
        }

        public int Calls { get; private set; }

        public Task<FaceDetectionResult?> DetectAsync(
            string imagePath, AutoFramingOptions options, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (_returnsNothing)
                return Task.FromResult<FaceDetectionResult?>(null);

            return Task.FromResult<FaceDetectionResult?>(new FaceDetectionResult
            {
                Faces = _faces,
                ImageWidth = 1000,
                ImageHeight = 1500
            });
        }
    }

    private sealed class ThrowingDetector : IFaceDetector
    {
        public int Calls { get; private set; }

        public Task<FaceDetectionResult?> DetectAsync(
            string imagePath, AutoFramingOptions options, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("native detector exploded");
        }
    }

    private sealed class HangingDetector : IFaceDetector
    {
        public async Task<FaceDetectionResult?> DetectAsync(
            string imagePath, AutoFramingOptions options, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    [Fact]
    public async Task SecondCall_IsServedFromCacheWithoutReDetecting()
    {
        var image = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var cache = CreateCache();

        var first = await cache.GetOrDetectAsync("remote-id-1", image, new AutoFramingOptions(), detector);
        var second = await cache.GetOrDetectAsync("remote-id-1", image, new AutoFramingOptions(), detector);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, detector.Calls);
        Assert.Equal(first!.ImageWidth, second!.ImageWidth);
        Assert.Equal(first.Faces.Length, second.Faces.Length);
    }

    [Fact]
    public async Task DetectionSurvivesTheCloudDeleteAndRedownloadCycle()
    {
        // This is the cloud scenario: the cached wallpaper file is deleted after applying, then a
        // later rotation downloads the same remote image to a brand-new path.
        var firstDownload = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var cache = CreateCache();

        await cache.GetOrDetectAsync("sha256ofRemoteId", firstDownload, new AutoFramingOptions(), detector);
        Assert.Equal(1, detector.Calls);

        File.Delete(firstDownload);
        var secondDownload = CreateImageFile();
        Assert.NotEqual(firstDownload, secondDownload);

        var result = await cache.GetOrDetectAsync("sha256ofRemoteId", secondDownload, new AutoFramingOptions(), detector);

        Assert.NotNull(result);
        Assert.Equal(1, detector.Calls);
    }

    [Fact]
    public async Task CachePersistsAcrossInstances()
    {
        var image = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));

        await CreateCache().GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);

        var secondDetector = new CountingDetector(Faces((0.4, 0.3)));
        var hit = await CreateCache().GetOrDetectAsync("key-a", image, new AutoFramingOptions(), secondDetector);

        Assert.NotNull(hit);
        Assert.Equal(0, secondDetector.Calls);
    }

    [Fact]
    public async Task DifferentDetectionKeys_DoNotShareResults()
    {
        var image = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var cache = CreateCache();

        await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);
        await cache.GetOrDetectAsync("key-b", image, new AutoFramingOptions(), detector);

        Assert.Equal(2, detector.Calls);
    }

    [Fact]
    public async Task FailedDetection_IsNotCachedAndCanBeRetried()
    {
        var image = CreateImageFile();
        var detector = new ThrowingDetector();
        var cache = CreateCache();

        var first = await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);
        var second = await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);

        // A failure must never be remembered as "this image has no face".
        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(2, detector.Calls);
    }

    [Fact]
    public async Task DetectorReturningNothing_IsNotCached()
    {
        var image = CreateImageFile();
        var detector = new CountingDetector(Array.Empty<FaceBox>(), returnsNothing: true);
        var cache = CreateCache();

        Assert.Null(await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector));
        Assert.Null(await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector));
        Assert.Equal(2, detector.Calls);
    }

    [Fact]
    public async Task DetectorThatHangs_TimesOutInsteadOfBlockingTheApply()
    {
        var image = CreateImageFile();
        var cache = CreateCache();
        var options = new AutoFramingOptions { DetectionTimeoutMs = 150 };

        var started = DateTime.UtcNow;
        var result = await cache.GetOrDetectAsync("key-a", image, options, new HangingDetector());
        var elapsed = DateTime.UtcNow - started;

        Assert.Null(result);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"timeout should cut detection short, took {elapsed}");
    }

    [Fact]
    public async Task MissingFile_ReturnsNullWithoutCallingTheDetector()
    {
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var missing = Path.Combine(_dir, "does-not-exist.jpg");

        var result = await CreateCache().GetOrDetectAsync("key-a", missing, new AutoFramingOptions(), detector);

        Assert.Null(result);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public async Task TooSmallFile_IsSkippedAsAnIncompleteDownload()
    {
        var tiny = CreateImageFile(16);
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var options = new AutoFramingOptions { MinFileSizeBytes = 4096 };

        var result = await CreateCache().GetOrDetectAsync("key-a", tiny, options, detector);

        Assert.Null(result);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public async Task TtlExpiry_ForcesAFreshDetection()
    {
        var image = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var cache = CreateCache(ttl: TimeSpan.FromMilliseconds(60));

        await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);
        await Task.Delay(200);
        await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);

        Assert.Equal(2, detector.Calls);
    }

    [Fact]
    public async Task MaxEntries_PrunesOldestFirst()
    {
        var image = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var cache = CreateCache(maxEntries: 3);

        for (int i = 0; i < 6; i++)
        {
            await cache.GetOrDetectAsync($"key-{i}", image, new AutoFramingOptions(), detector);
            await Task.Delay(5);
        }

        Assert.True(await cache.CountAsync() <= 3, "cache must respect MaxCacheEntries");
    }

    [Fact]
    public async Task CorruptCacheFile_IsQuarantinedAndRebuilt()
    {
        await File.WriteAllTextAsync(_cacheFile, "{ this is not valid json ");
        var image = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));

        var result = await CreateCache().GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);

        Assert.NotNull(result);
        Assert.False(File.Exists(_cacheFile + ".tmp"), "temp file must not be left behind");
    }

    [Fact]
    public void BuildKey_IncludesModelVersionSoAModelSwapInvalidatesResults()
    {
        var v1 = FaceDetectionCache.BuildKey("remote", 1000, 1500, "model-1");
        var v2 = FaceDetectionCache.BuildKey("remote", 1000, 1500, "model-2");
        var otherDims = FaceDetectionCache.BuildKey("remote", 800, 1200, "model-1");

        Assert.NotEqual(v1, v2);
        Assert.NotEqual(v1, otherDims);
        Assert.Equal(string.Empty, FaceDetectionCache.BuildKey(string.Empty, 1000, 1500, "model-1"));
    }

    [Fact]
    public async Task ClearAsync_DropsCachedDetections()
    {
        var image = CreateImageFile();
        var detector = new CountingDetector(Faces((0.4, 0.3)));
        var cache = CreateCache();

        await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);
        await cache.ClearAsync();
        await cache.GetOrDetectAsync("key-a", image, new AutoFramingOptions(), detector);

        Assert.Equal(2, detector.Calls);
    }
}
