using CommunityToolkit.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Screen_Painter.Services;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Imaging;
using Screen_Painter.Services.Logging;
using Screen_Painter.Services.Scheduling;
using Screen_Painter.Services.Security;
using Screen_Painter.Services.Storage;
using Screen_Painter.Services.Wallpaper;
using Screen_Painter.ViewModels;
using Screen_Painter.Views;

namespace Screen_Painter;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Load appsettings.json configuration
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        // Register typed AppSettings
        var appSettings = new AppSettings();
        builder.Configuration.Bind(appSettings);
        builder.Services.AddSingleton(appSettings);
        AppConstants.Initialize(appSettings);

        // Register Core Security & Storage Services
        builder.Services.AddSingleton<ISecureStorageService, SecureStorageService>();
        builder.Services.AddSingleton<ICloudAccountService, CloudAccountService>();
        builder.Services.AddSingleton<IStorageProvider, LocalStorageProvider>();
        builder.Services.AddSingleton<IStorageProvider, WebDavStorageProvider>();
        builder.Services.AddSingleton<IStorageProvider, OAuthStorageProvider>();
        
        // Register Cache Manager & Collection Scheduler Engine
        builder.Services.AddSingleton<ICacheManager, CloudCacheManager>();
        builder.Services.AddSingleton<ICollectionScheduler, CollectionScheduler>();
        builder.Services.AddSingleton<IStorageProviderResolver, StorageProviderResolver>();
        builder.Services.AddSingleton<IWallpaperRotationService, WallpaperRotationService>();
        builder.Services.AddSingleton<IFramingOverrideService, FramingOverrideService>();
        builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();
        builder.Services.AddSingleton<IGalleryManifestStore, GalleryManifestStore>();

        // Register Logging Services
        builder.Services.AddSingleton<LogService>();

        // Platform Wallpaper Service DI
#if ANDROID
        builder.Services.AddSingleton<IWallpaperService, Platforms.Android.WallpaperServiceAndroid>();
#else
        builder.Services.AddSingleton<IWallpaperService, WallpaperServiceStandard>();
#endif

        // Register ViewModels
        builder.Services.AddTransient<CollectionsViewModel>();
        builder.Services.AddTransient<CollectionDetailViewModel>();
        builder.Services.AddTransient<CollectionGalleryViewModel>();
        builder.Services.AddTransient<ImageEditorViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<CloudFolderPickerViewModel>();
        builder.Services.AddTransient<LogViewerViewModel>();

        // Register Views
        builder.Services.AddTransient<CollectionsPage>();
        builder.Services.AddTransient<CollectionDetailPage>();
        builder.Services.AddTransient<CollectionGalleryPage>();
        builder.Services.AddTransient<ImageEditorPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<CloudFolderPickerPage>();
        builder.Services.AddTransient<LogViewerPage>();

        builder.Services.AddLogging(logging =>
        {
            logging.AddDebug();
            logging.AddProvider(new FileLoggerProvider());
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        ServiceAccessor.Initialize(app.Services);
        return app;
    }
}
