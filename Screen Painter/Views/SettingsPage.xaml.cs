using System;
using Microsoft.Maui.Controls;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _viewModel.CheckBatteryStatus();
            await _viewModel.LoadAccountsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsPage OnAppearing Error]: {ex}");
        }
    }
}
