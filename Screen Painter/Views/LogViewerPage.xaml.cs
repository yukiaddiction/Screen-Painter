using Screen_Painter.ViewModels;

namespace Screen_Painter.Views;

public partial class LogViewerPage : ContentPage
{
    private readonly LogViewerViewModel _viewModel;

    public LogViewerPage(LogViewerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.LoadTailAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LogViewer OnAppearing Error]: {ex}");
        }
    }
}
