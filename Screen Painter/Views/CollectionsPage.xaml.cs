using System;
using Microsoft.Maui.Controls;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class CollectionsPage : ContentPage
{
    private readonly CollectionsViewModel _viewModel;
    private bool _isInitialLoading = false;

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
