using System;
using Microsoft.Maui.Controls;
using Screen_Painter.Services;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class CollectionsPage : ContentPage
{
    private readonly CollectionsViewModel _viewModel;
    private bool _isInitialLoading = false;
    private static bool _startupUpdateCheckDone;

    public CollectionsPage(CollectionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _isInitialLoading = true;
            await _viewModel.LoadCollectionsAsync();
        }
        finally
        {
            _isInitialLoading = false;
        }

        if (!_startupUpdateCheckDone)
        {
            _startupUpdateCheckDone = true;
            _ = TriggerAutoUpdateCheckAsync();
        }
    }

    private static async System.Threading.Tasks.Task TriggerAutoUpdateCheckAsync()
    {
        try
        {
            if (!Microsoft.Maui.Storage.Preferences.Default.Get("AutoUpdateCheck", true))
                return;

            var updateService = ServiceAccessor.GetService<IUpdateCheckService>();
            if (updateService == null)
                return;

            var result = await updateService.CheckForUpdateAsync(bypassCooldown: false);

            if (result.UpdateAvailable)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    bool open = await Shell.Current.DisplayAlert(
                        "Update Available",
                        $"Screen Painter v{result.LatestVersion} is available on GitHub.\n\n{result.ReleaseNotes}",
                        "Open Release Page",
                        "Later");
                    if (open && !string.IsNullOrEmpty(result.ReleaseUrl))
                        await Microsoft.Maui.ApplicationModel.Browser.Default.OpenAsync(result.ReleaseUrl, Microsoft.Maui.ApplicationModel.BrowserLaunchMode.SystemPreferred);
                });
            }
        }
        catch
        {
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.CancelPreviewLoading();
    }

    private async void OnSwitchToggled(object sender, ToggledEventArgs e)
    {
        if (_isInitialLoading) return;
        try
        {
            if (sender is Switch switchControl && switchControl.BindingContext is Models.WallpaperCollection collection)
            {
                if (collection.IsEnabled != e.Value)
                {
                    await _viewModel.ToggleCollectionAsync(collection, e.Value);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectionsPage Toggle Error]: {ex}");
        }
    }

    private async void OnEditClicked(object sender, EventArgs e)
    {
        try
        {
            if (sender is Button button && button.CommandParameter is string collectionId)
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync($"CollectionDetailPage?id={collectionId}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectionsPage Edit Error]: {ex}");
        }
    }
}
