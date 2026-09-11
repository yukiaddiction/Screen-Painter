namespace Screen_Painter.Services.Imaging;

/// <summary>
/// The wallpaper surface (launcher + lock screen) is always authored in the device's natural
/// portrait orientation. Display metrics report the <i>current</i> orientation, which can be
/// landscape while a landscape-locked app is foregrounded; using those numbers directly would
/// produce a landscape bitmap that overflows once the user returns to a portrait screen.
///
/// Pure and platform-free so both the Android wallpaper service and the auto-framing math agree
/// on the same numbers, and so the rule is unit-testable.
/// </summary>
public static class WallpaperSurfaceSize
{
    /// <summary>
    /// Normalizes a measured display size to the natural portrait wallpaper surface
    /// (width &lt;= height). Non-positive input yields <c>(0, 0)</c> so callers can bail out.
    /// </summary>
    public static (int width, int height) NormalizeToPortrait(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return (0, 0);

        return width > height ? (height, width) : (width, height);
    }
}
