using Microsoft.Maui.Controls;
using Screen_Painter.Views;

namespace Screen_Painter;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(CollectionDetailPage), typeof(CollectionDetailPage));
        Routing.RegisterRoute(nameof(ImageEditorPage), typeof(ImageEditorPage));
        Routing.RegisterRoute(nameof(CollectionGalleryPage), typeof(CollectionGalleryPage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(CloudFolderPickerPage), typeof(CloudFolderPickerPage));
        Routing.RegisterRoute(nameof(LogViewerPage), typeof(LogViewerPage));
    }

    private async void OnSettingsMenuItemClicked(object sender, System.EventArgs e)
    {
        FlyoutIsPresented = false; // Close drawer
        await GoToAsync(nameof(SettingsPage));
    }

    private bool _backNavigationInProgress;

    protected override bool OnBackButtonPressed()
    {
        var stack = Navigation?.NavigationStack;
        if (stack != null && stack.Count > 1)
        {
            if (_backNavigationInProgress)
                return true; // Swallow rapid repeat presses while navigating

            _backNavigationInProgress = true;
            Dispatcher.Dispatch(async () =>
            {
                try
                {
                    await GoToAsync("..");
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BackNavigation Error]: {ex.Message}");
                }
                finally
                {
                    _backNavigationInProgress = false;
                }
            });
            return true; // Handled — go to previous page instead of quitting
        }

        return base.OnBackButtonPressed(); // Root page — default behavior (exit)
    }
}
