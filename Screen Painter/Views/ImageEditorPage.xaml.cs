using Microsoft.Maui.Controls;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class ImageEditorPage : ContentPage
{
    private readonly ImageEditorViewModel _viewModel;

    public ImageEditorPage(ImageEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnPreviewPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _viewModel.BeginPan();
                break;
            case GestureStatus.Running:
                _viewModel.UpdatePan(e.TotalX, e.TotalY);
                break;
        }
    }

    private void OnPreviewPinchUpdated(object sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _viewModel.BeginPinch();
        }
        else if (e.Status == GestureStatus.Running)
        {
            _viewModel.UpdatePinch(e.Scale);
        }
    }
}
