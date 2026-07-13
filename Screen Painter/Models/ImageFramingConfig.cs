using System.Text.Json.Serialization;

namespace Screen_Painter.Models;

public enum AspectRatioMode
{
    AspectFill,
    AspectFit,
    Custom
}

public class ImageFramingConfig
{
    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.0;

    [JsonPropertyName("offsetX")]
    public double OffsetX { get; set; } = 0.0;

    [JsonPropertyName("offsetY")]
    public double OffsetY { get; set; } = 0.0;

    [JsonPropertyName("aspectRatioMode")]
    public AspectRatioMode AspectRatioMode { get; set; } = AspectRatioMode.AspectFill;

    [JsonPropertyName("customAspectRatio")]
    public double CustomAspectRatio { get; set; } = 9.0 / 16.0;
}
