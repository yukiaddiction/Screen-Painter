using Microsoft.Maui.Controls;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class ImageEditorPage : ContentPage
{
    public ImageEditorPage(ImageEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
