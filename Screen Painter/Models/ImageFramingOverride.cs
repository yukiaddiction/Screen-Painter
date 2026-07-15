using System.Text.Json.Serialization;

namespace Screen_Painter.Models;

public class ImageFramingOverride
{
    [JsonPropertyName("collectionId")]
    public string CollectionId { get; set; } = string.Empty;

    [JsonPropertyName("imageKey")]
    public string ImageKey { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public ImageFramingConfig Config { get; set; } = new();
}
