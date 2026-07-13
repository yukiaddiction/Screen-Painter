using Microsoft.Maui.Controls;
using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class CloudFolderPickerPage : ContentPage
{
    public CloudFolderPickerPage(CloudFolderPickerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
