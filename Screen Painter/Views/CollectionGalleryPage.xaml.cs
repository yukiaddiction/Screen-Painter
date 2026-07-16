using System.Linq;
using Microsoft.Maui.Controls;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class CollectionGalleryPage : ContentPage
{
    private readonly CollectionGalleryViewModel _viewModel;

    public CollectionGalleryPage(CollectionGalleryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.RefreshOverridesAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery OnAppearing Error]: {ex}");
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        try
        {
            await _viewModel.SaveStateAndSuspendAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery OnDisappearing Error]: {ex}");
        }
    }

    private void OnGalleryScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        _viewModel.OnScrolled(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
    }

    private async void OnImageSelected(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.FirstOrDefault() is not GalleryImageItem item)
                return;

            GalleryView.SelectedItem = null;
            await _viewModel.EditImageAsync(item);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gallery ImageSelected Error]: {ex}");
        }
    }
}
