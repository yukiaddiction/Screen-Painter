using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Screen_Painter.Models;
using Screen_Painter.Services.Logging;
using Screen_Painter.Services.Security;
using Screen_Painter.Services.Storage;

namespace Screen_Painter.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly ICloudAccountService _cloudAccountService;
    private readonly ISecureStorageService _secureStorage;
    private readonly WebDavStorageProvider _webDavTester;
    private readonly LogService _logService;
    private readonly ILogger<SettingsViewModel> _logger;

    public ObservableCollection<CloudAccount> CloudAccounts { get; } = new();

    public ICommand LoadAccountsCommand { get; }
    public ICommand AddWebDavAccountCommand { get; }
    public ICommand AddOAuthAccountCommand { get; }
    public ICommand DeleteAccountCommand { get; }
    public ICommand TestWebDavAccountCommand { get; }
    public ICommand RequestBatteryExemptionCommand { get; }
    public ICommand ViewLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }

    private string _logSummary = string.Empty;
    public string LogSummary
    {
        get => _logSummary;
        set => SetProperty(ref _logSummary, value);
    }

    private bool _isBatteryExempt;
    public bool IsBatteryExempt
    {
        get => _isBatteryExempt;
        set
        {
            if (SetProperty(ref _isBatteryExempt, value))
            {
                OnPropertyChanged(nameof(BatteryStatusText));
                OnPropertyChanged(nameof(BatteryStatusColor));
            }
        }
    }

    public string BatteryStatusText => IsBatteryExempt ? "Stably Unrestricted" : "Optimized (May stop)";
    public Color BatteryStatusColor => IsBatteryExempt ? Colors.Green : Colors.Orange;

    private bool _isDarkMode;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (Application.Current != null)
                        Application.Current.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
                    Microsoft.Maui.Storage.Preferences.Default.Set("AppTheme", value ? "Dark" : "Light");
                });
            }
        }
    }

    public SettingsViewModel(
        ICloudAccountService cloudAccountService,
        ISecureStorageService secureStorage,
        IEnumerable<IStorageProvider> storageProviders,
        LogService logService,
        ILogger<SettingsViewModel> logger)
    {
        _cloudAccountService = cloudAccountService;
        _secureStorage = secureStorage;
        _webDavTester = storageProviders.OfType<WebDavStorageProvider>().First()
            ?? throw new InvalidOperationException("WebDavStorageProvider not registered in DI");
        _logService = logService;
        _logger = logger;
        Title = "Cloud Accounts & Settings";

        var themePref = Microsoft.Maui.Storage.Preferences.Default.Get("AppTheme", AppConstants.DefaultAppTheme);
        _isDarkMode = themePref == "Dark";

        LoadAccountsCommand = new Command(async () => await LoadAccountsAsync());
        AddWebDavAccountCommand = new Command(async () => await AddWebDavAccountAsync());
        AddOAuthAccountCommand = new Command(async () => await AddOAuthAccountAsync());
        DeleteAccountCommand = new Command<CloudAccount>(async (a) => await DeleteAccountAsync(a));
        TestWebDavAccountCommand = new Command<CloudAccount>(async (a) => await TestWebDavAccountAsync(a));
        RequestBatteryExemptionCommand = new Command(async () => await RequestBatteryExemptionAsync());
        ViewLogsCommand = new Command(async () => await ShellHelper.GoToAsync(nameof(Views.LogViewerPage)));
        CopyLogsCommand = new Command(async () => await _logService.CopyLogsToClipboardAsync());

        LogSummary = _logService.GetLogSummary();

        CheckBatteryStatus();
    }

    public async Task LoadAccountsAsync()
    {
        IsBusy = true;
        try
        {
            CloudAccounts.Clear();
            var list = await _cloudAccountService.GetAllAccountsAsync();
            foreach (var acc in list)
            {
                CloudAccounts.Add(acc);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestWebDavAccountAsync(CloudAccount? account)
    {
        if (account == null) return;

        IsBusy = true;
        try
        {
            var user = await _secureStorage.DecryptAndGetAsync(account.EncryptedUsername) ?? string.Empty;
            var pass = await _secureStorage.DecryptAndGetAsync(account.EncryptedPasswordOrToken) ?? string.Empty;

            var result = await _webDavTester.TestWebDavConnectionAsync(account.ServerUrl, user, pass);

            _logger.LogInformation("WebDAV connection test — name: {Name}, result: {Result}, status: {Status}, items: {Items}",
                account.Name, result.Success ? "Success" : "Failed", result.StatusCode, result.ItemsFound);

            if (result.Success)
            {
                await ShellHelper.DisplayAlert("WebDAV Connected", $"{result.Message}\n\nDiscovered {result.ItemsFound} XML response items from server.", "OK");
            }
            else
            {
                string details = string.Join("\n• ", result.Details);
                await ShellHelper.DisplayAlert("WebDAV Diagnostic Failed", $"{result.Message}\n\nDiagnostic Details:\n• {details}", "OK");
            }
        }
        catch (Exception ex)
        {
            await ShellHelper.DisplayAlert("WebDAV Diagnostic Error", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddWebDavAccountAsync()
    {
        string? name = await ShellHelper.DisplayPromptAsync("Permanent WebDAV Account", "Enter Account Name (e.g. My Nextcloud):");
        if (string.IsNullOrEmpty(name)) return;

        string? url = await ShellHelper.DisplayPromptAsync("WebDAV Server URL", "Enter Server URL (e.g. https://dav.example.com/photos):");
        if (string.IsNullOrEmpty(url)) return;

        string? username = await ShellHelper.DisplayPromptAsync("WebDAV Auth", "Enter Username:");
        string? password = await ShellHelper.DisplayPromptAsync("WebDAV Auth", "Enter Password:", keyboard: Keyboard.Password);

        var encryptedUserKey = Guid.NewGuid().ToString();
        var encryptedPassKey = Guid.NewGuid().ToString();

        await _secureStorage.EncryptAndSaveAsync(encryptedUserKey, username ?? string.Empty);
        await _secureStorage.EncryptAndSaveAsync(encryptedPassKey, password ?? string.Empty);

        var account = new CloudAccount
        {
            Name = name,
            ServerUrl = url,
            Type = StorageType.WebDav,
            EncryptedUsername = encryptedUserKey,
            EncryptedPasswordOrToken = encryptedPassKey
        };

        // Test connection immediately upon creating
        var testResult = await _webDavTester.TestWebDavConnectionAsync(url, username ?? string.Empty, password ?? string.Empty);
        if (!testResult.Success)
        {
            bool proceed = await ShellHelper.DisplayAlert(
                "WebDAV Test Failed",
                $"Could not connect to WebDAV server: {testResult.Message}\n\nDo you still want to save this account?",
                "Save Anyway",
                "Cancel");

            if (!proceed) return;
        }
        else
        {
            await ShellHelper.DisplayAlert("WebDAV Connected", $"Successfully verified WebDAV server! Discovered {testResult.ItemsFound} remote items.", "OK");
        }

        await _cloudAccountService.SaveAccountAsync(account);
        CloudAccounts.Add(account);
        _logger.LogInformation("WebDAV account created — name: {Name}, url: {Url}", account.Name, account.ServerUrl);
    }

    private async Task AddOAuthAccountAsync()
    {
        string? name = await ShellHelper.DisplayPromptAsync("Permanent OAuth Account", "Enter Account Name (e.g. Google Drive):");
        if (string.IsNullOrEmpty(name)) return;

        string? url = await ShellHelper.DisplayPromptAsync("Cloud OAuth API", "Enter API Endpoint URL:");
        if (string.IsNullOrEmpty(url)) return;

        string? token = await ShellHelper.DisplayPromptAsync("OAuth Auth Token", "Enter Access Token or Auth Key:", keyboard: Keyboard.Password);
        var tokenKey = Guid.NewGuid().ToString();
        await _secureStorage.EncryptAndSaveAsync(tokenKey, token ?? string.Empty);

        var account = new CloudAccount
        {
            Name = name,
            ServerUrl = url,
            Type = StorageType.OAuthCloud,
            EncryptedPasswordOrToken = tokenKey
        };

        await _cloudAccountService.SaveAccountAsync(account);
        CloudAccounts.Add(account);
        _logger.LogInformation("OAuth account created — name: {Name}, url: {Url}", account.Name, account.ServerUrl);
    }

    private async Task DeleteAccountAsync(CloudAccount? account)
    {
        if (account == null) return;
        await _cloudAccountService.DeleteAccountAsync(account.Id);
        CloudAccounts.Remove(account);
        _logger.LogInformation("Cloud account deleted — name: {Name}, type: {Type}", account.Name, account.Type);
    }

    public void CheckBatteryStatus()
    {
#if ANDROID
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                var context = global::Android.App.Application.Context;
                var powerManager = (global::Android.OS.PowerManager?)context.GetSystemService(global::Android.Content.Context.PowerService);
                IsBatteryExempt = powerManager?.IsIgnoringBatteryOptimizations(context.PackageName) ?? false;
            }
            else
            {
                IsBatteryExempt = true;
            }
        }
        catch
        {
            IsBatteryExempt = false;
        }
#else
        IsBatteryExempt = true;
#endif
    }

    private async Task RequestBatteryExemptionAsync()
    {
#if ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                await ShellHelper.DisplayAlert("Not Required", "Battery exemptions are not required on your version of Android.", "OK");
                IsBatteryExempt = true;
                return;
            }

            var context = global::Android.App.Application.Context;
            var powerManager = (global::Android.OS.PowerManager?)context.GetSystemService(global::Android.Content.Context.PowerService);
            bool alreadyExempt = powerManager?.IsIgnoringBatteryOptimizations(context.PackageName) ?? false;

            if (alreadyExempt)
            {
                await ShellHelper.DisplayAlert("Battery Optimization", "App is already set to Unrestricted (ignoring battery optimization). It will run stably in the background!", "OK");
                IsBatteryExempt = true;
                return;
            }

            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);

            // Re-check after a brief moment
            await Task.Delay(2000);
            CheckBatteryStatus();
        }
        catch (Exception ex)
        {
            await ShellHelper.DisplayAlert("Request Failed", $"Could not open Battery settings directly. Please manually set Battery to 'Unrestricted' in App Info: {ex.Message}", "OK");
        }
#else
        await ShellHelper.DisplayAlert("Battery Optimization", "Not required on this platform.", "OK");
#endif
    }
}
