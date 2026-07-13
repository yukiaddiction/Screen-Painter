namespace Screen_Painter.Services.Storage;

public static class ImageExtensions
{
    public static readonly System.Collections.Generic.HashSet<string> Valid = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".heic", ".heif", ".gif", ".tiff", ".jfif", ".avif"
    };
}
