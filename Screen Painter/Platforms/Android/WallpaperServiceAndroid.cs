#if ANDROID
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Microsoft.Maui.ApplicationModel;
using Screen_Painter.Models;

namespace Screen_Painter.Platforms.Android;

public class WallpaperServiceAndroid : Services.Wallpaper.IWallpaperService
{
    private static readonly SemaphoreSlim WallpaperSemaphore = new(1, 1);

    public async Task<bool> ApplyWallpaperAsync(string imagePath, TargetScreen targetScreen, ImageFramingConfig framingConfig, bool skipPostApplyDelay = false)
    {
        if (string.IsNullOrEmpty(imagePath))
            return false;

        var taskResult = false;

        await WallpaperSemaphore.WaitAsync();
        try
        {
            taskResult = await Task.Run(() => ApplyWallpaperInternal(imagePath, targetScreen, framingConfig));
        }
        finally
        {
            WallpaperSemaphore.Release();
        }

        if (!skipPostApplyDelay)
        {
            await Task.Delay(AppConstants.WallpaperPostApplyDelayMs);
        }

        return taskResult;
    }

    private static bool ApplyWallpaperInternal(string imagePath, TargetScreen targetScreen, ImageFramingConfig framingConfig)
    {
        try
        {
            var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
            var wallpaperManager = WallpaperManager.GetInstance(context);
            if (wallpaperManager == null)
                return false;

            var (screenWidth, screenHeight) = GetDisplaySize(context);

            using var originalBitmap = DecodeBitmapWithSubsampling(context, imagePath, screenWidth, screenHeight);
            if (originalBitmap == null)
                return false;

            using var framedBitmap = ApplyDeviceFullScreenFraming(context, originalBitmap, framingConfig);

            SetWallpaperOnManager(wallpaperManager, framedBitmap, targetScreen);
            BroadcastWallpaperChanged(context);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WallpaperServiceAndroid Error]: {ex.Message}");
            TryFallbackWallpaperChooser();
            return false;
        }
    }

    private static (int width, int height) GetDisplaySize(Context context)
    {
        var metrics = context.Resources?.DisplayMetrics;
        return (
            metrics?.WidthPixels ?? AppConstants.FallbackDisplayWidth,
            metrics?.HeightPixels ?? AppConstants.FallbackDisplayHeight
        );
    }

    private static void SetWallpaperOnManager(WallpaperManager wallpaperManager, Bitmap bitmap, TargetScreen targetScreen)
    {
#pragma warning disable CA1416
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            switch (targetScreen)
            {
                case TargetScreen.Home:
                    try { wallpaperManager.SetBitmap(bitmap, null, true, WallpaperManagerFlags.System); }
                    catch { wallpaperManager.SetBitmap(bitmap); }
                    break;
                case TargetScreen.Lock:
                    wallpaperManager.SetBitmap(bitmap, null, true, WallpaperManagerFlags.Lock);
                    break;
                case TargetScreen.Both:
                    try
                    {
                        wallpaperManager.SetBitmap(bitmap, null, true, WallpaperManagerFlags.System | WallpaperManagerFlags.Lock);
                    }
                    catch
                    {
                        try { wallpaperManager.SetBitmap(bitmap, null, true, WallpaperManagerFlags.Lock); } catch { }
                        try { wallpaperManager.SetBitmap(bitmap, null, true, WallpaperManagerFlags.System); } catch { }
                        try { wallpaperManager.SetBitmap(bitmap); } catch { }
                    }
                    break;
                default:
                    try { wallpaperManager.SetBitmap(bitmap, null, true, WallpaperManagerFlags.System); }
                    catch { wallpaperManager.SetBitmap(bitmap); }
                    break;
            }
        }
        else
        {
            wallpaperManager.SetBitmap(bitmap);
        }
#pragma warning restore CA1416
    }

    private static void BroadcastWallpaperChanged(Context context)
    {
        try
        {
#pragma warning disable CS0618
            var changeIntent = new Intent(Intent.ActionWallpaperChanged);
#pragma warning restore CS0618
            changeIntent.AddFlags(ActivityFlags.NewTask);
            context.SendBroadcast(changeIntent);
        }
        catch
        {
        }
    }

    private static void TryFallbackWallpaperChooser()
    {
        try
        {
            var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;
            var intent = new Intent(Intent.ActionSetWallpaper);
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(Intent.CreateChooser(intent, "Set Wallpaper via Screen Painter"));
        }
        catch
        {
        }
    }

    private static int CalculateInSampleSize(BitmapFactory.Options options, int reqWidth, int reqHeight)
    {
        int height = options.OutHeight;
        int width = options.OutWidth;
        int inSampleSize = 1;

        if (height > reqHeight || width > reqWidth)
        {
            int halfHeight = height / 2;
            int halfWidth = width / 2;

            while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
            {
                inSampleSize *= 2;
            }
        }

        return inSampleSize;
    }

    private static Bitmap? DecodeBitmapWithSubsampling(Context context, string imagePath, int reqWidth, int reqHeight)
    {
        try
        {
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            using (var stream = OpenStream(context, imagePath))
            {
                if (stream == null) return null;
                BitmapFactory.DecodeStream(stream, null, options);
            }

            options.InSampleSize = CalculateInSampleSize(options, reqWidth, reqHeight);
            options.InJustDecodeBounds = false;
            options.InPreferredConfig = Bitmap.Config.Argb8888;
            options.InScaled = false;
#pragma warning disable CA1422
            options.InDither = false;
#pragma warning restore CA1422

            using (var stream = OpenStream(context, imagePath))
            {
                if (stream == null) return null;
                return BitmapFactory.DecodeStream(stream, null, options);
            }
        }
        catch
        {
            return null;
        }
    }

    private static Stream? OpenStream(Context context, string imagePath)
    {
        try
        {
            if (imagePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = global::Android.Net.Uri.Parse(imagePath);
                if (uri != null)
                {
                    return context.ContentResolver?.OpenInputStream(uri);
                }
            }

            if (File.Exists(imagePath))
            {
                return File.OpenRead(imagePath);
            }
        }
        catch
        {
        }

        return null;
    }

    private static Bitmap ApplyDeviceFullScreenFraming(Context context, Bitmap source, ImageFramingConfig config)
    {
        var metrics = context.Resources?.DisplayMetrics;
        int targetWidth = metrics?.WidthPixels ?? source.Width;
        int targetHeight = metrics?.HeightPixels ?? source.Height;

        if (targetWidth <= 0) targetWidth = source.Width;
        if (targetHeight <= 0) targetHeight = source.Height;

        var bitmap = Bitmap.CreateBitmap(targetWidth, targetHeight, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(bitmap);
        using var paint = new global::Android.Graphics.Paint { AntiAlias = true, FilterBitmap = true, Dither = true };

        canvas.DrawColor(global::Android.Graphics.Color.Black);

        float scaleX = (float)targetWidth / source.Width;
        float scaleY = (float)targetHeight / source.Height;
        float baseScale = Math.Max(scaleX, scaleY);

        float userScale = (float)config.Scale;
        float finalScale = baseScale * userScale;

        var matrix = new Matrix();
        float dx = (targetWidth - (source.Width * finalScale)) / 2f + (float)config.OffsetX;
        float dy = (targetHeight - (source.Height * finalScale)) / 2f + (float)config.OffsetY;

        matrix.PostScale(finalScale, finalScale);
        matrix.PostTranslate(dx, dy);

        canvas.DrawBitmap(source, matrix, paint);
        return bitmap;
    }
}
#endif
