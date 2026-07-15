using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Screen_Painter.Services.Imaging;

public static class ImageKey
{
    public static string Compute(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return string.Empty;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identifier));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ForPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var stem = Path.GetFileNameWithoutExtension(path);
        if (IsKey(stem))
            return stem.ToLowerInvariant();

        return Compute(path);
    }

    public static bool IsKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64)
            return false;

        foreach (var c in value)
        {
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
                return false;
        }

        return true;
    }
}
