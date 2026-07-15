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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.RefreshOverridesAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _ = _viewModel.SaveStateAndSuspendAsync();
    }

    private void OnGalleryScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        _viewModel.OnScrolled(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
    }

    private async void OnImageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not GalleryImageItem item)
            return;

        GalleryView.SelectedItem = null;
        await _viewModel.EditImageAsync(item);
    }
}
