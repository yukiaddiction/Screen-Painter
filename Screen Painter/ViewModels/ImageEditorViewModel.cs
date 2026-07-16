using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Screen_Painter.Models;
using Screen_Painter.Services;
using Screen_Painter.Services.Cache;
using Screen_Painter.Services.Imaging;
using Screen_Painter.Services.Scheduling;
using Screen_Painter.Services.Storage;

namespace Screen_Painter.ViewModels;

public class ImageEditorViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ICollectionScheduler _scheduler;
    private readonly IFramingOverrideService _framingOverrides;
    private readonly IThumbnailService _thumbnailService;
    private readonly ICacheManager _cacheManager;
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private string _collectionId = string.Empty;
    private string _targetImageKey = string.Empty;
    private string _identifier = string.Empty;
    private string _folderId = string.Empty;
    private WallpaperCollection _currentCollection = new();
    private ImageFramingConfig _framing = new();
    private readonly double _screenWidthPx;
    private readonly double _screenHeightPx;

    public string CollectionId => _collectionId;

    public bool IsPerImageMode => !string.IsNullOrEmpty(_targetImageKey);

    public string? PreviewImageSource
    {
        get => _previewImageSource;
        set
        {
            if (SetProperty(ref _previewImageSource, value))
            {
                OnPropertyChanged(nameof(HasPreviewImage));
                OnPropertyChanged(nameof(ShowPlaceholder));
                UpdateGhostMetrics();
            }
        }
    }
    private string? _previewImageSource;

    public bool HasPreviewImage => !string.IsNullOrEmpty(_previewImageSource);

    public bool ShowPlaceholder => string.IsNullOrEmpty(_previewImageSource);

    public bool HasGhost
    {
        get => _hasGhost;
        private set
        {
            if (SetProperty(ref _hasGhost, value))
            {
                OnPropertyChanged(nameof(ShowFallbackFramedImage));
            }
        }
    }
    private bool _hasGhost;

    public bool ShowFallbackFramedImage => HasPreviewImage && !_hasGhost;

    public double GhostWidth => _ghostBaseWidth * _framing.Scale;

    public double GhostHeight => _ghostBaseHeight * _framing.Scale;

    private double _ghostBaseWidth;
    private double _ghostBaseHeight;
    private double _imageWidthPx;
    private double _imageHeightPx;

    public double ScaleMinimum
    {
        get => _scaleMinimum;
        private set => SetProperty(ref _scaleMinimum, value);
    }
    private double _scaleMinimum = 0.5;

    public double OffsetXMinimum
    {
        get => _offsetXMinimum;
        private set => SetProperty(ref _offsetXMinimum, value);
    }
    private double _offsetXMinimum = -200;

    public double OffsetXMaximum
    {
        get => _offsetXMaximum;
        private set => SetProperty(ref _offsetXMaximum, value);
    }
    private double _offsetXMaximum = 200;

    public double OffsetYMinimum
    {
        get => _offsetYMinimum;
        private set => SetProperty(ref _offsetYMinimum, value);
    }
    private double _offsetYMinimum = -200;

    public double OffsetYMaximum
    {
        get => _offsetYMaximum;
        private set => SetProperty(ref _offsetYMaximum, value);
    }
    private double _offsetYMaximum = 200;

    private double _panStartOffsetX;
    private double _panStartOffsetY;
    private double _pinchRunningScale = 1.0;

    public void BeginPan()
    {
        _panStartOffsetX = _framing.OffsetX;
        _panStartOffsetY = _framing.OffsetY;
    }

    public void UpdatePan(double totalX, double totalY)
    {
        if (PreviewFrameWidth <= 0 || PreviewFrameHeight <= 0)
            return;

        double factorX = _screenWidthPx / PreviewFrameWidth;
        double factorY = _screenHeightPx / PreviewFrameHeight;

        OffsetX = Math.Clamp(_panStartOffsetX + totalX * factorX, OffsetXMinimum, OffsetXMaximum);
        OffsetY = Math.Clamp(_panStartOffsetY + totalY * factorY, OffsetYMinimum, OffsetYMaximum);
    }

    public void BeginPinch()
    {
        _pinchRunningScale = _framing.Scale;
    }

    public void UpdatePinch(double delta)
    {
        if (delta <= 0)
            return;

        _pinchRunningScale *= delta;
        Scale = Math.Clamp(_pinchRunningScale, ScaleMinimum, 3.0);
    }

    private void UpdateGhostMetrics()
    {
        _ghostBaseWidth = 0;
        _ghostBaseHeight = 0;
        _imageWidthPx = 0;
        _imageHeightPx = 0;

        var dims = string.IsNullOrEmpty(_previewImageSource)
            ? null
            : _thumbnailService.GetImageDimensions(_previewImageSource);

        if (dims is { Width: > 0, Height: > 0 })
        {
            _imageWidthPx = dims.Value.Width;
            _imageHeightPx = dims.Value.Height;
            double baseScale = Math.Max(PreviewFrameWidth / dims.Value.Width, PreviewFrameHeight / dims.Value.Height);
            _ghostBaseWidth = dims.Value.Width * baseScale;
            _ghostBaseHeight = dims.Value.Height * baseScale;
        }

        HasGhost = _ghostBaseWidth > 0 && HasPreviewImage;
        OnPropertyChanged(nameof(GhostWidth));
        OnPropertyChanged(nameof(GhostHeight));
        OnPropertyChanged(nameof(ShowFallbackFramedImage));
        UpdateFramingLimits();
    }

    private void UpdateFramingLimits()
    {
        if (_imageWidthPx <= 0 || _imageHeightPx <= 0 || _screenWidthPx <= 0 || _screenHeightPx <= 0)
        {
            ScaleMinimum = 0.5;
            OffsetXMinimum = -200;
            OffsetXMaximum = 200;
            OffsetYMinimum = -200;
            OffsetYMaximum = 200;
            return;
        }

        // AspectFill is the smallest scale that fully covers the screen — below it black shows
        ScaleMinimum = 1.0;
        if (_framing.Scale < 1.0)
        {
            _framing.Scale = 1.0;
            OnPropertyChanged(nameof(Scale));
            OnPropertyChanged(nameof(GhostWidth));
            OnPropertyChanged(nameof(GhostHeight));
        }

        // Same math as the applied wallpaper, in device pixels
        double baseScale = Math.Max(_screenWidthPx / _imageWidthPx, _screenHeightPx / _imageHeightPx);
        double displayedW = _imageWidthPx * baseScale * _framing.Scale;
        double displayedH = _imageHeightPx * baseScale * _framing.Scale;
        double maxOffX = Math.Max(0.5, (displayedW - _screenWidthPx) / 2.0);
        double maxOffY = Math.Max(0.5, (displayedH - _screenHeightPx) / 2.0);

        OffsetXMinimum = -maxOffX;
        OffsetXMaximum = maxOffX;
        OffsetYMinimum = -maxOffY;
        OffsetYMaximum = maxOffY;

        var clampedX = Math.Clamp(_framing.OffsetX, -maxOffX, maxOffX);
        if (clampedX != _framing.OffsetX)
        {
            OffsetX = clampedX;
        }

        var clampedY = Math.Clamp(_framing.OffsetY, -maxOffY, maxOffY);
        if (clampedY != _framing.OffsetY)
        {
            OffsetY = clampedY;
        }
    }

    public double PreviewFrameWidth { get; }

    public double PreviewFrameHeight { get; }

    public double PreviewOffsetX => _screenWidthPx > 0 ? _framing.OffsetX * (PreviewFrameWidth / _screenWidthPx) : 0;

    public double PreviewOffsetY => _screenHeightPx > 0 ? _framing.OffsetY * (PreviewFrameHeight / _screenHeightPx) : 0;

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
                OnPropertyChanged(nameof(GhostWidth));
                OnPropertyChanged(nameof(GhostHeight));
                UpdateFramingLimits();
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
                OnPropertyChanged(nameof(PreviewOffsetX));
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
                OnPropertyChanged(nameof(PreviewOffsetY));
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
    public ICommand RemoveOverrideCommand { get; }

    public ImageEditorViewModel(
        ICollectionScheduler scheduler,
        IFramingOverrideService framingOverrides,
        IThumbnailService thumbnailService,
        ICacheManager cacheManager,
        IEnumerable<IStorageProvider> storageProviders)
    {
        _scheduler = scheduler;
        _framingOverrides = framingOverrides;
        _thumbnailService = thumbnailService;
        _cacheManager = cacheManager;
        _storageProviders = storageProviders;
        Title = "Non-Destructive Image Framing Editor";

        double screenW = AppConstants.FallbackDisplayWidth;
        double screenH = AppConstants.FallbackDisplayHeight;
        try
        {
            var info = DeviceDisplay.Current.MainDisplayInfo;
            if (info.Width > 0 && info.Height > 0)
            {
                screenW = info.Width;
                screenH = info.Height;
            }
        }
        catch
        {
            // Fall back to configured dimensions
        }
        _screenWidthPx = screenW;
        _screenHeightPx = screenH;

        // Preview frame mirrors the device screen aspect ratio within a 220x390 box
        const double maxFrameWidth = 220.0;
        const double maxFrameHeight = 390.0;
        double aspect = screenH / screenW;
        double frameWidth = maxFrameWidth;
        double frameHeight = maxFrameWidth * aspect;
        if (frameHeight > maxFrameHeight)
        {
            frameHeight = maxFrameHeight;
            frameWidth = maxFrameHeight / aspect;
        }
        PreviewFrameWidth = frameWidth;
        PreviewFrameHeight = frameHeight;

        SaveFramingCommand = new Command(async () => await SaveFramingAsync());
        ResetFramingCommand = new Command(ResetFraming);
        RemoveOverrideCommand = new Command(async () => await RemoveOverrideAsync());
    }

    private static string DecodeQueryValue(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('%'))
            return value;

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("collectionId", out var idObj) && idObj is string id)
        {
            _collectionId = DecodeQueryValue(id);
        }

        if (query.TryGetValue("imageKey", out var keyObj) && keyObj is string key)
        {
            _targetImageKey = DecodeQueryValue(key);
        }

        if (query.TryGetValue("identifier", out var identifierObj) && identifierObj is string identifier)
        {
            _identifier = DecodeQueryValue(identifier);
        }

        if (query.TryGetValue("folderId", out var folderObj) && folderObj is string folderId)
        {
            _folderId = DecodeQueryValue(folderId);
        }

        string? decodedPreview = null;
        if (query.TryGetValue("previewPath", out var previewObj) && previewObj is string preview && !string.IsNullOrEmpty(preview))
        {
            decodedPreview = DecodeQueryValue(preview);
        }

        OnPropertyChanged(nameof(IsPerImageMode));
        Title = IsPerImageMode ? "Per-Image Framing Editor" : "Non-Destructive Image Framing Editor";

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (!string.IsNullOrEmpty(decodedPreview))
                {
                    var exists = await Task.Run(() => File.Exists(decodedPreview));
                    if (exists)
                        PreviewImageSource = decodedPreview;
                }

                await LoadFramingConfigAsync(_collectionId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageEditor Load Error]: {ex}");
            }
        });
    }

    private static ImageFramingConfig CloneConfig(ImageFramingConfig source) => new()
    {
        Scale = source.Scale,
        OffsetX = source.OffsetX,
        OffsetY = source.OffsetY,
        AspectRatioMode = source.AspectRatioMode,
        CustomAspectRatio = source.CustomAspectRatio
    };

    private async Task LoadFramingConfigAsync(string collectionId)
    {
        var list = await _scheduler.GetAllCollectionsAsync();
        var match = list.FirstOrDefault(c => System.String.Equals(c.Id, collectionId, System.StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return;

        _currentCollection = match;

        if (IsPerImageMode)
        {
            var overrideConfig = await _framingOverrides.GetOverrideAsync(collectionId, _targetImageKey);
            Framing = overrideConfig != null
                ? CloneConfig(overrideConfig)
                : CloneConfig(match.FramingConfig ?? new ImageFramingConfig());
        }
        else
        {
            Framing = match.FramingConfig ?? new ImageFramingConfig();
        }

        Scale = Framing.Scale;
        OffsetX = Framing.OffsetX;
        OffsetY = Framing.OffsetY;
        AspectRatioMode = Framing.AspectRatioMode;
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(OffsetX));
        OnPropertyChanged(nameof(OffsetY));
        OnPropertyChanged(nameof(AspectRatioMode));
        OnPropertyChanged(nameof(PreviewOffsetX));
        OnPropertyChanged(nameof(PreviewOffsetY));
        OnPropertyChanged(nameof(GhostWidth));
        OnPropertyChanged(nameof(GhostHeight));
        UpdateFramingLimits();

        if (IsPerImageMode)
        {
            await EnsurePreviewAsync();
        }
        else
        {
            await EnsureCollectionPreviewAsync();
        }
    }

    private async Task EnsurePreviewAsync()
    {
        if (string.IsNullOrEmpty(_identifier))
            return;

        // Instant low-res preview while the high-res version is prepared
        if (string.IsNullOrEmpty(PreviewImageSource) || !File.Exists(PreviewImageSource))
        {
            var thumb = _thumbnailService.GetExistingThumbnailPath(_identifier);
            if (thumb != null)
            {
                PreviewImageSource = thumb;
            }
        }

        var folder = _currentCollection.Folders?.FirstOrDefault(f => string.Equals(f.Id, _folderId, StringComparison.OrdinalIgnoreCase));
        if (folder == null)
            return;

        try
        {
            var hiRes = await _thumbnailService.GetOrCreatePreviewAsync(folder, _identifier);
            if (!string.IsNullOrEmpty(hiRes))
            {
                PreviewImageSource = hiRes;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageEditor Preview Error]: {ex.Message}");
        }
    }

    private async Task EnsureCollectionPreviewAsync()
    {
        if (HasPreviewImage)
            return;

        try
        {
            var cached = await _cacheManager.GetCachedImagesAsync(_currentCollection.Id);
            var firstCached = cached?.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstCached) && File.Exists(firstCached))
            {
                PreviewImageSource = firstCached;
                return;
            }

            var localProvider = _storageProviders.FirstOrDefault(p => p.SupportedType == StorageType.Local);
            if (localProvider == null)
                return;

            foreach (var folder in _currentCollection.Folders?.Where(f => f.IsActive && f.Type == StorageType.Local) ?? Enumerable.Empty<FolderSource>())
            {
                var files = await localProvider.ListImageIdentifiersAsync(folder);
                var first = files?.FirstOrDefault();
                if (!string.IsNullOrEmpty(first))
                {
                    PreviewImageSource = first;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageEditor Collection Preview Error]: {ex.Message}");
        }
    }

    private async Task SaveFramingAsync()
    {
        if (IsPerImageMode)
        {
            await _framingOverrides.SetOverrideAsync(_collectionId, _targetImageKey, Framing);
        }
        else
        {
            _currentCollection.FramingConfig = Framing;
            await _scheduler.SaveCollectionAsync(_currentCollection);
        }

        await ShellHelper.GoToAsync("..");
    }

    private async Task RemoveOverrideAsync()
    {
        if (!IsPerImageMode)
            return;

        await _framingOverrides.RemoveOverrideAsync(_collectionId, _targetImageKey);
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
