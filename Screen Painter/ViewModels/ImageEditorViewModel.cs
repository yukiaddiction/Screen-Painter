using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Screen_Painter.Models;
using Screen_Painter.Services.Scheduling;

namespace Screen_Painter.ViewModels;

[QueryProperty(nameof(CollectionId), "collectionId")]
public class ImageEditorViewModel : BaseViewModel
{
    private readonly ICollectionScheduler _scheduler;
    private string _collectionId = string.Empty;
    private WallpaperCollection _currentCollection = new();
    private ImageFramingConfig _framing = new();

    public string CollectionId
    {
        get => _collectionId;
        set
        {
            _collectionId = value;
            _ = LoadFramingConfigAsync(value).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    System.Diagnostics.Debug.WriteLine($"[ImageEditor Load Error]: {t.Exception}");
            });
        }
    }

    public ImageFramingConfig Framing
    {
        get => _framing;
        set => SetProperty(ref _framing, value);
    }

    public double Scale
    {
        get => _framing.Scale;
        set
        {
            if (_framing.Scale != value)
            {
                _framing.Scale = value;
                OnPropertyChanged();
            }
        }
    }

    public double OffsetX
    {
        get => _framing.OffsetX;
        set
        {
            if (_framing.OffsetX != value)
            {
                _framing.OffsetX = value;
                OnPropertyChanged();
            }
        }
    }

    public double OffsetY
    {
        get => _framing.OffsetY;
        set
        {
            if (_framing.OffsetY != value)
            {
                _framing.OffsetY = value;
                OnPropertyChanged();
            }
        }
    }

    public Models.AspectRatioMode AspectRatioMode
    {
        get => _framing.AspectRatioMode;
        set
        {
            if (_framing.AspectRatioMode != value)
            {
                _framing.AspectRatioMode = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand SaveFramingCommand { get; }
    public ICommand ResetFramingCommand { get; }

    public ImageEditorViewModel(ICollectionScheduler scheduler)
    {
        _scheduler = scheduler;
        Title = "Non-Destructive Image Framing Editor";

        SaveFramingCommand = new Command(async () => await SaveFramingAsync());
        ResetFramingCommand = new Command(ResetFraming);
    }

    private async Task LoadFramingConfigAsync(string collectionId)
    {
        var list = await _scheduler.GetAllCollectionsAsync();
        var match = list.FirstOrDefault(c => System.String.Equals(c.Id, collectionId, System.StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _currentCollection = match;
            Framing = match.FramingConfig ?? new ImageFramingConfig();
            Scale = Framing.Scale;
            OffsetX = Framing.OffsetX;
            OffsetY = Framing.OffsetY;
            AspectRatioMode = Framing.AspectRatioMode;
        }
    }

    private async Task SaveFramingAsync()
    {
        _currentCollection.FramingConfig = Framing;
        await _scheduler.SaveCollectionAsync(_currentCollection);
        await ShellHelper.GoToAsync("..");
    }

    private void ResetFraming()
    {
        Scale = 1.0;
        OffsetX = 0.0;
        OffsetY = 0.0;
        AspectRatioMode = Models.AspectRatioMode.AspectFill;
    }
}
