using System;
using System.Threading;
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Controls;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class CollectionDetailPage : ContentPage
{
    private readonly CollectionDetailViewModel _viewModel;

    public CollectionDetailPage(CollectionDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.CheckCloudAccountsStatusAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectionDetailPage OnAppearing Error]: {ex}");
        }
    }

    private async void OnAddLocalFolderClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
            if (result.IsSuccessful && result.Folder != null && !string.IsNullOrEmpty(result.Folder.Path))
            {
                if (_viewModel.AddLocalFolderCommand.CanExecute(result.Folder.Path))
                {
                    _viewModel.AddLocalFolderCommand.Execute(result.Folder.Path);
                }
            }
        }
        catch
        {
            string fallbackPath = await DisplayPromptAsync("Local Folder Path", "Enter local directory path on device:");
            if (!string.IsNullOrEmpty(fallbackPath))
            {
                _viewModel.AddLocalFolderCommand.Execute(fallbackPath);
            }
        }
    }
}
