using System.Text.Json;
using Screen_Painter.Models;

namespace ScreenPainter.Tests;

public class FramingOverrideTests
{
    [Fact]
    public void OverrideRecord_RoundTrips()
    {
        var original = new ImageFramingOverride
        {
            CollectionId = "col-123",
            ImageKey = Screen_Painter.Services.Imaging.ImageKey.Compute("https://example.com/cat.jpg"),
            Config = new ImageFramingConfig
            {
                Scale = 1.5,
                OffsetX = -42.0,
                OffsetY = 17.5,
                AspectRatioMode = AspectRatioMode.AspectFit,
                CustomAspectRatio = 0.75
            }
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ImageFramingOverride>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.CollectionId, restored!.CollectionId);
        Assert.Equal(original.ImageKey, restored.ImageKey);
        Assert.Equal(original.Config.Scale, restored.Config.Scale);
        Assert.Equal(original.Config.OffsetX, restored.Config.OffsetX);
        Assert.Equal(original.Config.OffsetY, restored.Config.OffsetY);
        Assert.Equal(original.Config.AspectRatioMode, restored.Config.AspectRatioMode);
        Assert.Equal(original.Config.CustomAspectRatio, restored.Config.CustomAspectRatio);
    }

    [Fact]
    public void OverrideRecord_UsesCamelCaseJsonNames()
    {
        var record = new ImageFramingOverride
        {
            CollectionId = "col-1",
            ImageKey = "abc",
            Config = new ImageFramingConfig()
        };

        var json = JsonSerializer.Serialize(record);

        Assert.Contains("\"collectionId\"", json);
        Assert.Contains("\"imageKey\"", json);
        Assert.Contains("\"config\"", json);
    }

    [Fact]
    public void DefaultRecord_RoundTrips()
    {
        var json = JsonSerializer.Serialize(new ImageFramingOverride());
        var restored = JsonSerializer.Deserialize<ImageFramingOverride>(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored!.Config);
        Assert.Equal(1.0, restored.Config.Scale);
    }
}
