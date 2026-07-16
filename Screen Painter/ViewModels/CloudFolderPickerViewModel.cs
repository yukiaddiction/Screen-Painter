using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Screen_Painter.Models;
using Screen_Painter.Services.Security;
using Screen_Painter.Services.Storage;

namespace Screen_Painter.ViewModels;

public class CloudFolderPickerViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IEnumerable<IStorageProvider> _storageProviders;
    private readonly ISecureStorageService _secureStorage;
    private readonly WebDavStorageProvider _webDavTester;

    private FolderSource _pendingFolderSource = new();
    private string _currentUrl = string.Empty;
    private string _statusMessage = "Connecting to cloud server...";

    public ObservableCollection<CloudFolderItem> Subfolders { get; } = new();

    public string CurrentUrl
    {
        get => _currentUrl;
        set
        {
            if (SetProperty(ref _currentUrl, value))
            {
                OnPropertyChanged(nameof(CurrentFolderName));
                OnPropertyChanged(nameof(SelectFolderButtonText));
            }
        }
    }

    public string CurrentFolderName => string.IsNullOrEmpty(CurrentUrl) ? "Root" : System.IO.Path.GetFileName(CurrentUrl.TrimEnd('/'));
    public string SelectFolderButtonText => $"✓ Select Folder: {CurrentFolderName}";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public TaskCompletionSource<FolderSource?>? TaskCompletionSource { get; set; }

    public ICommand SelectFolderCommand { get; }
    public ICommand OpenSubfolderCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand TestConnectionCommand { get; }

    public CloudFolderPickerViewModel(
        IEnumerable<IStorageProvider> storageProviders,
        ISecureStorageService secureStorage)
    {
        _storageProviders = storageProviders;
        _secureStorage = secureStorage;
        _webDavTester = storageProviders.OfType<WebDavStorageProvider>().First()
            ?? throw new InvalidOperationException("WebDavStorageProvider not registered in DI");
        Title = "Browse Cloud Directory";

        SelectFolderCommand = new Command(SelectFolder);
        OpenSubfolderCommand = new Command<string>(async (path) => await OpenSubfolderAsync(path));
        NavigateUpCommand = new Command(async () => await NavigateUpAsync());
        TestConnectionCommand = new Command(async () => await RunDiagnosticsAsync());
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("serverUrl", out var urlObj) && urlObj is string url)
        {
            _pendingFolderSource.PathOrUrl = Uri.UnescapeDataString(url);
            CurrentUrl = _pendingFolderSource.PathOrUrl;
        }

        if (query.TryGetValue("type", out var typeObj) && typeObj is string typeStr)
        {
            if (Enum.TryParse<StorageType>(typeStr, out var parsed))
                _pendingFolderSource.Type = parsed;
        }

        if (query.TryGetValue("userKey", out var userKeyObj) && userKeyObj is string userKey)
        {
            _pendingFolderSource.EncryptedUsername = userKey;
        }

        if (query.TryGetValue("passKey", out var passKeyObj) && passKeyObj is string passKey)
        {
            _pendingFolderSource.EncryptedPasswordOrToken = passKey;
        }

        // Trigger folder scan ONLY after all query parameters are fully populated!
        if (!string.IsNullOrEmpty(CurrentUrl))
        {
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    await LoadFoldersAsync(CurrentUrl);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudFolderPicker Load Error]: {ex}");
                }
            });
        }
    }

    public async Task RunDiagnosticsAsync()
    {
        IsBusy = true;
        try
        {
            var user = await _secureStorage.DecryptAndGetAsync(_pendingFolderSource.EncryptedUsername) ?? string.Empty;
            var pass = await _secureStorage.DecryptAndGetAsync(_pendingFolderSource.EncryptedPasswordOrToken) ?? string.Empty;

            var result = await _webDavTester.TestWebDavConnectionAsync(CurrentUrl, user, pass);

            if (result.Success)
            {
                StatusMessage = $"Status: {result.Message} • Discovered {result.ItemsFound} items.";
                await ShellHelper.DisplayAlert("WebDAV Connection Success", $"{result.Message}\n\nDiscovered {result.ItemsFound} items.", "OK");
            }
            else
            {
                StatusMessage = $"Status Error: {result.Message}";
                string details = string.Join("\n• ", result.Details);
                await ShellHelper.DisplayAlert("WebDAV Diagnostics Failed", $"{result.Message}\n\nDetails:\n• {details}", "OK");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Diagnostics Error: {ex.Message}";
            await ShellHelper.DisplayAlert("WebDAV Error", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadFoldersAsync(string path)
    {
        IsBusy = true;
        StatusMessage = "Querying remote WebDAV server...";
        try
        {
            Subfolders.Clear();
            var provider = _storageProviders.FirstOrDefault(p => p.SupportedType == _pendingFolderSource.Type);
            if (provider != null)
            {
                var list = await provider.ListSubfoldersAsync(_pendingFolderSource, path);
                foreach (var folder in list)
                {
                    var name = System.IO.Path.GetFileName(folder.TrimEnd('/'));
                    if (string.IsNullOrEmpty(name)) name = folder;
                    Subfolders.Add(new CloudFolderItem { Name = name, FullPath = folder });
                }

                if (list.Any())
                {
                    StatusMessage = $"Connected • Found {list.Count} subfolders at this path";
                }
                else
                {
                    // Run quick diagnostic to inform user why 0 folders were returned
                    var user = await _secureStorage.DecryptAndGetAsync(_pendingFolderSource.EncryptedUsername) ?? string.Empty;
                    var pass = await _secureStorage.DecryptAndGetAsync(_pendingFolderSource.EncryptedPasswordOrToken) ?? string.Empty;
                    var diag = await _webDavTester.TestWebDavConnectionAsync(path, user, pass);

                    if (diag.Success)
                    {
                        StatusMessage = $"Connected (HTTP {diag.StatusCode}) • 0 subfolders found at this directory (Select this folder to scan images)";
                    }
                    else
                    {
                        StatusMessage = $"Connection Error (HTTP {diag.StatusCode}): {diag.Message}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Folder Scan Exception: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenSubfolderAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        CurrentUrl = path;
        await LoadFoldersAsync(path);
    }

    private async Task NavigateUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentUrl) || string.Equals(CurrentUrl.TrimEnd('/'), _pendingFolderSource.PathOrUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var uri = new Uri(CurrentUrl);
            var parent = uri.GetLeftPart(UriPartial.Authority) + string.Join("/", uri.Segments.Take(uri.Segments.Length - 1));
            CurrentUrl = parent;
            await LoadFoldersAsync(parent);
        }
        catch
        {
            // Fallback to root if parent resolution fails
            CurrentUrl = _pendingFolderSource.PathOrUrl;
            await LoadFoldersAsync(CurrentUrl);
        }
    }

    private void SelectFolder()
    {
        var finalSource = new FolderSource
        {
            Name = System.IO.Path.GetFileName(CurrentUrl.TrimEnd('/')),
            PathOrUrl = CurrentUrl,
            Type = _pendingFolderSource.Type,
            EncryptedUsername = _pendingFolderSource.EncryptedUsername,
            EncryptedPasswordOrToken = _pendingFolderSource.EncryptedPasswordOrToken
        };
        if (string.IsNullOrEmpty(finalSource.Name))
            finalSource.Name = "Cloud Root";

        var parameters = new Dictionary<string, object>
        {
            { "SelectedFolderSource", finalSource }
        };

        TaskCompletionSource?.TrySetResult(finalSource);
        System.Diagnostics.Debug.WriteLine($"[CloudFolderPicker] Folder selected: {finalSource.Name} — path: {CurrentUrl}");
        ShellHelper.GoToAsync("..", parameters);
    }
}

public class CloudFolderItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
}
