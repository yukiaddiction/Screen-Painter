using Screen_Painter.Services.Imaging;

namespace ScreenPainter.Tests;

public class ImageKeyTests
{
    [Fact]
    public void Compute_IsDeterministic()
    {
        var a = ImageKey.Compute("https://example.com/photos/cat.jpg");
        var b = ImageKey.Compute("https://example.com/photos/cat.jpg");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ProducesLowercase64CharHex()
    {
        var key = ImageKey.Compute("/storage/emulated/0/Pictures/dog.png");

        Assert.Equal(64, key.Length);
        Assert.True(ImageKey.IsKey(key));
        Assert.Equal(key, key.ToLowerInvariant());
    }

    [Fact]
    public void Compute_DifferentIdentifiers_ProduceDifferentKeys()
    {
        var a = ImageKey.Compute("https://example.com/a.jpg");
        var b = ImageKey.Compute("https://example.com/b.jpg");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_EmptyIdentifier_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ImageKey.Compute(string.Empty));
    }

    [Fact]
    public void ForPath_CachedFileNamedByKey_ReturnsKeyFromFileName()
    {
        var sourceKey = ImageKey.Compute("https://example.com/photos/cat.jpg");
        var cachedPath = $"/data/cache/collection_cache/col-1/{sourceKey}.jpg";

        Assert.Equal(sourceKey, ImageKey.ForPath(cachedPath));
    }

    [Fact]
    public void ForPath_RegularLocalFile_ReturnsHashOfFullPath()
    {
        var path = "/storage/emulated/0/Pictures/holiday.jpg";

        Assert.Equal(ImageKey.Compute(path), ImageKey.ForPath(path));
    }

    [Fact]
    public void ForPath_LegacyGuidCacheFile_DoesNotLookLikeKey()
    {
        var legacyPath = "/data/cache/collection_cache/col-1/3f2504e0-4f89-11d3-9a0c-0305e82c3301.jpg";
        var key = ImageKey.ForPath(legacyPath);

        Assert.Equal(ImageKey.Compute(legacyPath), key);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", false)]
    public void IsKey_RejectsNonKeyValues(string? value, bool expected)
    {
        Assert.Equal(expected, ImageKey.IsKey(value));
    }

    [Fact]
    public void IsKey_AcceptsUppercaseHex()
    {
        var key = ImageKey.Compute("test").ToUpperInvariant();

        Assert.True(ImageKey.IsKey(key));
    }
}
