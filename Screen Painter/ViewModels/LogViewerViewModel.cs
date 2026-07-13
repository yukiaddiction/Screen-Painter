using System.Threading.Tasks;
using System.Windows.Input;
using Screen_Painter.Services.Logging;

namespace Screen_Painter.ViewModels;

public class LogViewerViewModel : BaseViewModel
{
    private readonly LogService _logService;
    private string _logContent = string.Empty;
    private bool _showingAll;
    private string _modeLabel = "Last 500 lines (tap for all)";

    public string LogContent
    {
        get => _logContent;
        set => SetProperty(ref _logContent, value);
    }

    public string ModeLabel
    {
        get => _modeLabel;
        set => SetProperty(ref _modeLabel, value);
    }

    public ICommand CopyCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleModeCommand { get; }
    public ICommand OpenFileCommand { get; }

    public LogViewerViewModel(LogService logService)
    {
        _logService = logService;
        Title = "Application Logs";

        CopyCommand = new Command(async () => await CopyAsync());
        RefreshCommand = new Command(async () => await RefreshAsync());
        ToggleModeCommand = new Command(async () => await ToggleModeAsync());
        OpenFileCommand = new Command(async () => await _logService.OpenLogFileAsync());
    }

    public async Task LoadTailAsync()
    {
        IsBusy = true;
        try
        {
            _showingAll = false;
            ModeLabel = "Last 500 lines (tap for all)";
            LogContent = await _logService.ReadTailAsync(500);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleModeAsync()
    {
        IsBusy = true;
        try
        {
            if (_showingAll)
            {
                await LoadTailAsync();
            }
            else
            {
                _showingAll = true;
                ModeLabel = "Showing all logs (tap for recent)";
                LogContent = await _logService.ReadAllLogsAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_showingAll)
        {
            IsBusy = true;
            LogContent = await _logService.ReadAllLogsAsync();
            IsBusy = false;
        }
        else
        {
            await LoadTailAsync();
        }
    }

    private async Task CopyAsync()
    {
        await _logService.CopyLogsToClipboardAsync();
        await ShellHelper.DisplayAlert("Copied", "Log content copied to clipboard.", "OK");
    }
}
