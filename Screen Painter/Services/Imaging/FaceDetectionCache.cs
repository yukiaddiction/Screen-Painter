using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Screen_Painter.Services.Imaging;

/// <summary>
/// One cached detection outcome.
///
/// <para>
/// <see cref="DetectionKey"/> is the stable identity of the image, <b>not</b> a local file path.
/// Cloud images are deleted from the cache directory after every apply and re-downloaded under
/// the same name, so a path-keyed cache would never hit for them; the remote identifier survives
/// that cycle. The image dimensions are part of the key because normalized face boxes are only
/// meaningful against the bitmap they were measured on.
/// </para>
/// </summary>
public class FaceDetectionEntry
{
    [JsonPropertyName("key")]
    public string DetectionKey { get; set; } = string.Empty;

    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; set; } = string.Empty;

    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }

    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    [JsonPropertyName("faces")]
    public FaceBox[] Faces { get; set; } = Array.Empty<FaceBox>();

    [JsonPropertyName("detectedAtUtc")]
    public DateTime DetectedAtUtc { get; set; }
}

/// <summary>
/// Persistent store of face detection outcomes, so a wallpaper is analysed once and every later
/// apply is a cache hit. This is the "learning" the feature relies on: the app accumulates
/// knowledge of which images contain a face and where, instead of re-inferring on every rotation.
/// </summary>
public class FaceDetectionCache
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<FaceDetectionEntry>? _entries;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FaceDetectionCache(string filePath, ILogger logger, TimeSpan ttl, int maxEntries)
    {
        _filePath = filePath;
        _logger = logger;
        _ttl = ttl;
        _maxEntries = maxEntries;
    }

    /// <summary>Composes the stable cache key for a detection target.</summary>
    public static string BuildKey(string detectionKey, int imageWidth, int imageHeight, string modelVersion)
    {
        if (string.IsNullOrEmpty(detectionKey))
            return string.Empty;

        return $"{detectionKey}|{imageWidth}x{imageHeight}|{modelVersion}";
    }

    /// <summary>
    /// Returns the cached detection for this image, running the detector once on a miss.
    ///
    /// <para>
    /// Detection is best-effort by contract: a missing, unreadable or too-small file, a hung
    /// detector, and any thrown exception all resolve to <c>null</c> rather than propagating, so
    /// auto-framing can never break the wallpaper apply that triggered it.
    /// </para>
    /// </summary>
    public async Task<FaceDetectionResult?> GetOrDetectAsync(
        string detectionKey,
        string imagePath,
        AutoFramingOptions options,
        IFaceDetector detector,
        CancellationToken cancellationToken = default)
    {
        var hit = await TryGetArbitrarySizeAsync(detectionKey).ConfigureAwait(false);
        if (hit != null)
            return hit;

        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return null;

        try
        {
            var info = new FileInfo(imagePath);
            if (info.Length < Math.Max(0, options?.MinFileSizeBytes ?? 0))
                return null;
        }
        catch
        {
            return null;
        }

        FaceDetectionResult? detected;
        try
        {
            var timeoutMs = Math.Max(0, options?.DetectionTimeoutMs ?? 0);
            if (timeoutMs > 0)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeoutMs);
                detected = await detector.DetectAsync(imagePath, options!, timeoutCts.Token).ConfigureAwait(false);
            }
            else
            {
                detected = await detector.DetectAsync(imagePath, options!, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Face detection failed for {File}", Path.GetFileName(imagePath));
            return null;
        }

        // A detector that measured nothing is a failure, not an empty result: caching it would
        // permanently suppress detection for this image.
        if (detected == null || !detected.IsUsable)
            return null;

        await StoreAsync(detectionKey, detected.ImageWidth, detected.ImageHeight,
            FaceDetectorModel.Version, detected.Faces).ConfigureAwait(false);

        return detected;
    }

    /// <summary>
    /// Looks up a detection when the caller does not know the cached dimensions yet. The stored
    /// dimensions are authoritative and are returned alongside the faces.
    /// </summary>
    public async Task<FaceDetectionResult?> TryGetArbitrarySizeAsync(string detectionKey)
    {
        if (string.IsNullOrEmpty(detectionKey))
            return null;

        var prefix = detectionKey + "|";
        var entries = await LoadAsync().ConfigureAwait(false);
        var cutoff = DateTime.UtcNow - _ttl;

        var match = entries
            .Where(e => e.DetectionKey.StartsWith(prefix, StringComparison.Ordinal) &&
                        e.DetectedAtUtc >= cutoff &&
                        e.ImageWidth > 0 && e.ImageHeight > 0)
            .OrderByDescending(e => e.DetectedAtUtc)
            .FirstOrDefault();

        if (match == null)
            return null;

        return new FaceDetectionResult
        {
            Faces = match.Faces ?? Array.Empty<FaceBox>(),
            ImageWidth = match.ImageWidth,
            ImageHeight = match.ImageHeight
        };
    }

    public async Task<FaceDetectionResult?> TryGetAsync(
        string detectionKey,
        int imageWidth,
        int imageHeight,
        string modelVersion)
    {
        var key = BuildKey(detectionKey, imageWidth, imageHeight, modelVersion);
        if (key.Length == 0)
            return null;

        var entries = await LoadAsync().ConfigureAwait(false);
        var cutoff = DateTime.UtcNow - _ttl;

        var match = entries.FirstOrDefault(e =>
            string.Equals(e.DetectionKey, key, StringComparison.Ordinal) &&
            e.DetectedAtUtc >= cutoff);

        if (match == null)
            return null;

        return new FaceDetectionResult
        {
            Faces = match.Faces ?? Array.Empty<FaceBox>(),
            ImageWidth = match.ImageWidth,
            ImageHeight = match.ImageHeight
        };
    }

    public async Task StoreAsync(
        string detectionKey,
        int imageWidth,
        int imageHeight,
        string modelVersion,
        IReadOnlyList<FaceBox> faces)
    {
        var key = BuildKey(detectionKey, imageWidth, imageHeight, modelVersion);
        if (key.Length == 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = await LoadUnlockedAsync().ConfigureAwait(false);
            var cutoff = DateTime.UtcNow - _ttl;

            entries.RemoveAll(e =>
                string.Equals(e.DetectionKey, key, StringComparison.Ordinal) ||
                e.DetectedAtUtc < cutoff);

            entries.Add(new FaceDetectionEntry
            {
                DetectionKey = key,
                ModelVersion = modelVersion,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                Faces = faces?.ToArray() ?? Array.Empty<FaceBox>(),
                DetectedAtUtc = DateTime.UtcNow
            });

            PruneUnlocked(entries);
            await SaveUnlockedAsync(entries).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops every cached detection. Used when the model or its tunables change.</summary>
    public async Task ClearAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _entries = new List<FaceDetectionEntry>();
            await SaveUnlockedAsync(_entries).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync()
    {
        var entries = await LoadAsync().ConfigureAwait(false);
        var cutoff = DateTime.UtcNow - _ttl;
        return entries.Count(e => e.DetectedAtUtc >= cutoff);
    }

    private async Task<List<FaceDetectionEntry>> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadUnlockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<FaceDetectionEntry>> LoadUnlockedAsync()
    {
        if (_entries != null)
            return _entries;

        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<List<FaceDetectionEntry>>(json);
                if (parsed != null)
                {
                    _entries = parsed;
                    return _entries;
                }

                QuarantineCorruptFile();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read face detection cache");
            QuarantineCorruptFile();
        }

        _entries = new List<FaceDetectionEntry>();
        return _entries;
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var corruptPath = _filePath + ".corrupt";
            File.Move(_filePath, corruptPath, overwrite: true);
            _logger?.LogWarning("Quarantined corrupt face detection cache -> {CorruptName}",
                Path.GetFileName(corruptPath));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to quarantine corrupt face detection cache");
        }
    }

    private void PruneUnlocked(List<FaceDetectionEntry> entries)
    {
        if (_maxEntries <= 0 || entries.Count <= _maxEntries)
            return;

        // Newest first, so the least recently detected entries fall off first.
        var keep = entries
            .OrderByDescending(e => e.DetectedAtUtc)
            .Take(_maxEntries)
            .ToList();

        entries.Clear();
        entries.AddRange(keep);
    }

    private async Task SaveUnlockedAsync(List<FaceDetectionEntry> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(entries, JsonOptions);
            var tmpPath = _filePath + ".tmp";

            await File.WriteAllTextAsync(tmpPath, json).ConfigureAwait(false);

            try
            {
                if (File.Exists(_filePath))
                    File.Replace(tmpPath, _filePath, null);
                else
                    File.Move(tmpPath, _filePath);
            }
            catch
            {
                // File.Replace can fail on some filesystems; an overwriting move still lands.
                File.Move(tmpPath, _filePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to write face detection cache");
        }
    }
}
