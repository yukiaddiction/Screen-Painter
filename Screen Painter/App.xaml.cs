using Microsoft.Extensions.Logging;

namespace Screen_Painter
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

#if WINDOWS
            Resources.MergedDictionaries.Add(new Resources.Styles.LayoutsWindows());
#endif
            // To add platform-specific layouts, create Layouts.Android.xaml / Layouts.iOS.xaml
            // with x:Class="Screen_Painter.Resources.Styles.LayoutsAndroid" etc.,
            // then add corresponding #if blocks here following the same pattern.

            var themePref = Microsoft.Maui.Storage.Preferences.Default.Get("AppTheme", AppConstants.DefaultAppTheme);
            UserAppTheme = themePref == "Dark" ? AppTheme.Dark : AppTheme.Light;

            Screen_Painter.Services.Logging.FileLogger.PurgeOldLogs();
            ServiceAccessor.GetService<Microsoft.Extensions.Logging.ILogger<App>>()
                ?.LogInformation("App started — theme: {Theme}", themePref);
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}