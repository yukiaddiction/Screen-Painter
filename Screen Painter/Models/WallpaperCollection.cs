using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Screen_Painter.Models;

public class WallpaperCollection : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [JsonPropertyName("id")]
    public string Id { get => _id; set { if (_id != value) { _id = value; OnPropertyChanged(); } } }
    private string _id = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
    private string _name = "My Collection";

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get => _isEnabled; set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } } }
    private bool _isEnabled = true;

    [JsonPropertyName("target")]
    public TargetScreen Target { get => _target; set { if (_target != value) { _target = value; OnPropertyChanged(); } } }
    private TargetScreen _target = TargetScreen.Both;

    [JsonPropertyName("trigger")]
    public TriggerType Trigger { get => _trigger; set { if (_trigger != value) { _trigger = value; OnPropertyChanged(); } } }
    private TriggerType _trigger = TriggerType.Timer;

    [JsonPropertyName("isTimerEnabled")]
    public bool IsTimerEnabled { get => _isTimerEnabled; set { if (_isTimerEnabled != value) { _isTimerEnabled = value; OnPropertyChanged(); } } }
    private bool _isTimerEnabled;

    [JsonPropertyName("isScreenAwakeEnabled")]
    public bool IsScreenAwakeEnabled { get => _isScreenAwakeEnabled; set { if (_isScreenAwakeEnabled != value) { _isScreenAwakeEnabled = value; OnPropertyChanged(); } } }
    private bool _isScreenAwakeEnabled;

    [JsonPropertyName("triggersMigrated")]
    public bool TriggersMigrated { get => _triggersMigrated; set { if (_triggersMigrated != value) { _triggersMigrated = value; OnPropertyChanged(); } } }
    private bool _triggersMigrated;

    public void MigrateTriggerIfNeeded()
    {
        if (_triggersMigrated)
            return;

        if (!_isTimerEnabled && !_isScreenAwakeEnabled)
        {
            if (_trigger == TriggerType.ScreenAwake)
                _isScreenAwakeEnabled = true;
            else
                _isTimerEnabled = true;
        }

        _triggersMigrated = true;
    }

    [JsonPropertyName("timerIntervalMinutes")]
    public int TimerIntervalMinutes { get => _timerIntervalMinutes; set { if (_timerIntervalMinutes != value) { _timerIntervalMinutes = value; OnPropertyChanged(); } } }
    private int _timerIntervalMinutes = 15;

    [JsonPropertyName("isScheduleEnabled")]
    public bool IsScheduleEnabled { get => _isScheduleEnabled; set { if (_isScheduleEnabled != value) { _isScheduleEnabled = value; OnPropertyChanged(); } } }
    private bool _isScheduleEnabled = false;

    [JsonPropertyName("scheduleStartTime")]
    public TimeSpan ScheduleStartTime { get => _scheduleStartTime; set { if (_scheduleStartTime != value) { _scheduleStartTime = value; OnPropertyChanged(); } } }
    private TimeSpan _scheduleStartTime = new TimeSpan(0, 0, 0);

    [JsonPropertyName("scheduleEndTime")]
    public TimeSpan ScheduleEndTime { get => _scheduleEndTime; set { if (_scheduleEndTime != value) { _scheduleEndTime = value; OnPropertyChanged(); } } }
    private TimeSpan _scheduleEndTime = new TimeSpan(23, 59, 0);

    [JsonPropertyName("scheduleDays")]
    public List<DayOfWeek> ScheduleDays { get => _scheduleDays; set { if (_scheduleDays != value) { _scheduleDays = value; OnPropertyChanged(); } } }
    private List<DayOfWeek> _scheduleDays = new();

    [JsonPropertyName("folders")]
    public List<FolderSource> Folders { get => _folders; set { if (_folders != value) { _folders = value; OnPropertyChanged(); } } }
    private List<FolderSource> _folders = new();

    [JsonPropertyName("framingConfig")]
    public ImageFramingConfig FramingConfig { get => _framingConfig; set { if (_framingConfig != value) { _framingConfig = value; OnPropertyChanged(); } } }
    private ImageFramingConfig _framingConfig = new();

    [JsonIgnore]
    public string? PreviewImagePath { get => _previewImagePath; set { if (_previewImagePath != value) { _previewImagePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewImageSource)); } } }
    private string? _previewImagePath;

    [JsonIgnore]
    public string? PreviewImageSource => PreviewImagePath;

    [JsonIgnore]
    public List<string> PreviewImagePaths
    {
        get => _previewImagePaths;
        set
        {
            _previewImagePaths = value ?? new List<string>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(ShowPlaceholder));
        }
    }
    private List<string> _previewImagePaths = new();

    [JsonIgnore]
    public bool IsPreviewLoading { get => _isPreviewLoading; set { if (_isPreviewLoading != value) { _isPreviewLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowPlaceholder)); } } }
    private bool _isPreviewLoading;

    [JsonIgnore]
    public bool HasPreview => _previewImagePaths.Count > 0;

    [JsonIgnore]
    public bool ShowPlaceholder => !_isPreviewLoading && _previewImagePaths.Count == 0;
}
