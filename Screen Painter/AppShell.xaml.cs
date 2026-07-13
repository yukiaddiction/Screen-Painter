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
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(CloudFolderPickerPage), typeof(CloudFolderPickerPage));
        Routing.RegisterRoute(nameof(LogViewerPage), typeof(LogViewerPage));
    }

    private async void OnSettingsMenuItemClicked(object sender, System.EventArgs e)
    {
        FlyoutIsPresented = false; // Close drawer
        await GoToAsync(nameof(SettingsPage));
    }
}
