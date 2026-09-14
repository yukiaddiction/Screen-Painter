using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Screen_Painter.Models;
using Screen_Painter.Services;
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
    private readonly IUpdateCheckService _updateCheckService;
    private readonly Screen_Painter.Services.Imaging.FaceDetectionCache _faceDetectionCache;

    public ObservableCollection<CloudAccount> CloudAccounts { get; } = new();

    public ICommand LoadAccountsCommand { get; }
    public ICommand AddWebDavAccountCommand { get; }
    public ICommand AddOAuthAccountCommand { get; }
    public ICommand DeleteAccountCommand { get; }
    public ICommand TestWebDavAccountCommand { get; }
    public ICommand EditAccountCommand { get; }
    public ICommand RequestBatteryExemptionCommand { get; }
    public ICommand ViewLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand OpenGitHubCommand { get; }

    private string _logSummary = string.Empty;
    public string LogSummary
    {
        get => _logSummary;
        set => SetProperty(ref _logSummary, value);
    }

    private string _updateStatusText = string.Empty;
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetProperty(ref _updateStatusText, value);
    }

    public string AppVersionText => $"Version {Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString}";

    private bool _isAutoUpdateCheckEnabled;
    public bool IsAutoUpdateCheckEnabled
    {
        get => _isAutoUpdateCheckEnabled;
        set
        {
            if (SetProperty(ref _isAutoUpdateCheckEnabled, value))
                Microsoft.Maui.Storage.Preferences.Default.Set(AppConstants.AutoUpdateCheckPreferenceKey, value);
        }
    }

    private bool _isAutoFramingEnabledByDefault;
    /// <summary>
    /// Default smart auto-framing state applied to newly created collections. Each collection
    /// carries its own switch, so this is a template rather than a global override.
    /// </summary>
    public bool IsAutoFramingEnabledByDefault
    {
        get => _isAutoFramingEnabledByDefault;
        set
        {
            if (SetProperty(ref _isAutoFramingEnabledByDefault, value))
                Microsoft.Maui.Storage.Preferences.Default.Set(AppConstants.AutoFramingPreferenceKey, value);
        }
    }

    /// <summary>
    /// Auto-framing needs on-device face detection, which only the Android target ships. Hiding the
    /// switch elsewhere avoids offering a setting that cannot do anything.
    /// </summary>
    public bool IsAutoFramingSupported => AppConstants.IsAutoFramingSupported;

    private string _autoFramingCacheSummary = string.Empty;
    public string AutoFramingCacheSummary
    {
        get => _autoFramingCacheSummary;
        set => SetProperty(ref _autoFramingCacheSummary, value);
    }

    public ICommand ClearAutoFramingCacheCommand { get; }

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
                    Microsoft.Maui.Storage.Preferences.Default.Set(AppConstants.AppThemePreferenceKey, value ? AppConstants.DarkThemeValue : AppConstants.LightThemeValue);
                });
            }
        }
    }

    public SettingsViewModel(
        ICloudAccountService cloudAccountService,
        ISecureStorageService secureStorage,
        IEnumerable<IStorageProvider> storageProviders,
        LogService logService,
        ILogger<SettingsViewModel> logger,
        IUpdateCheckService updateCheckService,
        Screen_Painter.Services.Imaging.FaceDetectionCache faceDetectionCache)
    {
        _cloudAccountService = cloudAccountService;
        _secureStorage = secureStorage;
        _webDavTester = storageProviders.OfType<WebDavStorageProvider>().First()
            ?? throw new InvalidOperationException("WebDavStorageProvider not registered in DI");
        _logService = logService;
        _logger = logger;
        _updateCheckService = updateCheckService;
        _faceDetectionCache = faceDetectionCache;
        Title = "Cloud Accounts & Settings";

        var themePref = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.AppThemePreferenceKey, AppConstants.DefaultAppTheme);
        _isDarkMode = themePref == AppConstants.DarkThemeValue;

        _isAutoUpdateCheckEnabled = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.AutoUpdateCheckPreferenceKey, true);
        _isAutoFramingEnabledByDefault = Microsoft.Maui.Storage.Preferences.Default.Get(
            AppConstants.AutoFramingPreferenceKey, AppConstants.AutoFramingEnabledByDefault);

        LoadAccountsCommand = new AsyncCommand(async () => await LoadAccountsAsync());
        AddWebDavAccountCommand = new AsyncCommand(async () => await AddWebDavAccountAsync());
        AddOAuthAccountCommand = new AsyncCommand(async () => await AddOAuthAccountAsync());
        DeleteAccountCommand = new AsyncCommand<CloudAccount>(async (a) => await DeleteAccountAsync(a));
        TestWebDavAccountCommand = new AsyncCommand<CloudAccount>(async (a) => await TestWebDavAccountAsync(a));
        EditAccountCommand = new AsyncCommand<CloudAccount>(async (a) => await EditAccountAsync(a));
        RequestBatteryExemptionCommand = new AsyncCommand(async () => await RequestBatteryExemptionAsync());
        ViewLogsCommand = new AsyncCommand(async () => await ShellHelper.GoToAsync(nameof(Views.LogViewerPage)));
        CopyLogsCommand = new AsyncCommand(async () => await _logService.CopyLogsToClipboardAsync());
        CheckForUpdatesCommand = new AsyncCommand(async () => await CheckForUpdatesAsync());
        OpenGitHubCommand = new AsyncCommand(async () => await OpenGitHubAsync());
        ClearAutoFramingCacheCommand = new AsyncCommand(async () => await ClearAutoFramingCacheAsync());

        LogSummary = _logService.GetLogSummary();

        CheckBatteryStatus();
        _ = RefreshAutoFramingCacheSummaryAsync();
    }

    private async Task RefreshAutoFramingCacheSummaryAsync()
    {
        try
        {
            var count = await _faceDetectionCache.CountAsync();
            AutoFramingCacheSummary = count == 0
                ? "No wallpapers analysed yet. Each wallpaper is analysed once, on the device."
                : $"{count} wallpaper{(count == 1 ? "" : "s")} analysed and remembered on this device.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read auto-framing cache summary");
            AutoFramingCacheSummary = "Analysis history unavailable.";
        }
    }

    private async Task ClearAutoFramingCacheAsync()
    {
        try
        {
            await _faceDetectionCache.ClearAsync();
            await RefreshAutoFramingCacheSummaryAsync();
            await ShellHelper.DisplayAlert("Analysis Cleared",
                "Screen Painter will analyse wallpapers again the next time they are applied.", "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear auto-framing cache");
            await ShellHelper.DisplayAlert("Clear Failed", ex.Message, "OK");
        }
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

        var encryptedUsername = await _secureStorage.EncryptAndSaveAsync(Guid.NewGuid().ToString(), username ?? string.Empty);
        var encryptedPassword = await _secureStorage.EncryptAndSaveAsync(Guid.NewGuid().ToString(), password ?? string.Empty);

        if (string.IsNullOrEmpty(encryptedUsername) || string.IsNullOrEmpty(encryptedPassword))
        {
            await ShellHelper.DisplayAlert("Security Error", "Failed to encrypt the account credentials. The account was not saved.", "OK");
            return;
        }

        var account = new CloudAccount
        {
            Name = name,
            ServerUrl = url,
            Type = StorageType.WebDav,
            EncryptedUsername = encryptedUsername,
            EncryptedPasswordOrToken = encryptedPassword
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

    /// <summary>
    /// Updates an existing account in place. Adding a second account with the same name used to
    /// be the only way to fix credentials: it left two indistinguishable records behind and the
    /// folder picker could keep resolving the stale one. Reusing the Id makes the save below an
    /// in-place replace, so every collection that points at this account picks up the new
    /// credentials.
    /// </summary>
    private async Task EditAccountAsync(CloudAccount? account)
    {
        if (account == null) return;

        string? name = await ShellHelper.DisplayPromptAsync("Edit Account", "Account Name:", initialValue: account.Name);
        if (string.IsNullOrEmpty(name)) return;

        string? url = await ShellHelper.DisplayPromptAsync("Edit Account", "Server URL:", initialValue: account.ServerUrl);
        if (string.IsNullOrEmpty(url)) return;

        string credentialLabel = account.Type == StorageType.OAuthCloud ? "Access Token or Auth Key:" : "Password:";
        string? credential = await ShellHelper.DisplayPromptAsync(
            "Edit Account",
            $"Enter new {credentialLabel} Leave empty to keep the current one.",
            keyboard: Keyboard.Password);

        // Null means "keep the envelope already stored on the account".
        string? encryptedPasswordOrToken = null;

        if (!string.IsNullOrEmpty(credential))
        {
            // Same envelope format for a password and for an OAuth token, so one write covers both.
            encryptedPasswordOrToken = await _secureStorage.EncryptAndSaveAsync(Guid.NewGuid().ToString(), credential);

            if (string.IsNullOrEmpty(encryptedPasswordOrToken))
            {
                await ShellHelper.DisplayAlert("Security Error", "Failed to encrypt the new credential. The account was not changed.", "OK");
                return;
            }
        }

        var updated = CloudAccountResolver.ApplyEdit(account, name, url, encryptedPasswordOrToken);

        await _cloudAccountService.SaveAccountAsync(updated);

        var index = CloudAccounts.IndexOf(account);
        if (index >= 0)
            CloudAccounts[index] = updated;

        _logger.LogInformation("Cloud account updated — name: {Name}, url: {Url}", updated.Name, updated.ServerUrl);

        await ShellHelper.DisplayAlert("Account Updated",
            $"'{updated.Name}' now uses the saved credentials. Collections already using it will pick them up on the next access.",
            "OK");
    }

    private async Task AddOAuthAccountAsync()
    {
        string? name = await ShellHelper.DisplayPromptAsync("Permanent OAuth Account", "Enter Account Name (e.g. Google Drive):");
        if (string.IsNullOrEmpty(name)) return;

        string? url = await ShellHelper.DisplayPromptAsync("Cloud OAuth API", "Enter API Endpoint URL:");
        if (string.IsNullOrEmpty(url)) return;

        string? token = await ShellHelper.DisplayPromptAsync("OAuth Auth Token", "Enter Access Token or Auth Key:", keyboard: Keyboard.Password);
        var encryptedToken = await _secureStorage.EncryptAndSaveAsync(Guid.NewGuid().ToString(), token ?? string.Empty);

        if (string.IsNullOrEmpty(encryptedToken))
        {
            await ShellHelper.DisplayAlert("Security Error", "Failed to encrypt the access token. The account was not saved.", "OK");
            return;
        }

        var account = new CloudAccount
        {
            Name = name,
            ServerUrl = url,
            Type = StorageType.OAuthCloud,
            EncryptedPasswordOrToken = encryptedToken
        };

        await _cloudAccountService.SaveAccountAsync(account);
        CloudAccounts.Add(account);
        _logger.LogInformation("OAuth account created — name: {Name}, url: {Url}", account.Name, account.ServerUrl);
    }

    private async Task DeleteAccountAsync(CloudAccount? account)
    {
        if (account == null) return;
        await _cloudAccountService.DeleteAccountAsync(account.Id);

        // Clean up any legacy platform-SecureStorage entries (no-op for the new
        // ciphertext-envelope format, which lives in the account JSON itself).
        await _secureStorage.RemoveAsync(account.EncryptedUsername);
        await _secureStorage.RemoveAsync(account.EncryptedPasswordOrToken);

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

    private async Task CheckForUpdatesAsync()
    {
        IsBusy = true;
        UpdateStatusText = "Checking…";
        try
        {
            var result = await _updateCheckService.CheckForUpdateAsync(bypassCooldown: true);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                UpdateStatusText = "Could not check for updates.";
                await ShellHelper.DisplayAlert("Update Check Failed", $"Could not reach GitHub: {result.ErrorMessage}", "OK");
                return;
            }

            if (result.UpdateAvailable)
            {
                UpdateStatusText = $"Update available: v{result.LatestVersion}";
                bool open = await ShellHelper.DisplayAlert(
                    "Update Available",
                    $"Version {result.LatestVersion} is available.\n\n{result.ReleaseNotes}",
                    "Open Release Page",
                    "Later");
                if (open && !string.IsNullOrEmpty(result.ReleaseUrl))
                    await Browser.Default.OpenAsync(result.ReleaseUrl, BrowserLaunchMode.SystemPreferred);
            }
            else
            {
                UpdateStatusText = "You're up to date!";
                await ShellHelper.DisplayAlert("Up to Date", "You are running the latest version.", "OK");
            }
        }
        catch (Exception)
        {
            UpdateStatusText = "Update check failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PerformAutoCheckAsync()
    {
        if (!IsAutoUpdateCheckEnabled)
            return;

        try
        {
            var result = await _updateCheckService.CheckForUpdateAsync(bypassCooldown: false);

            if (result.UpdateAvailable)
            {
                UpdateStatusText = $"Update available: v{result.LatestVersion}";
            }
        }
        catch
        {
        }
    }

    private static async Task OpenGitHubAsync()
    {
        await Browser.Default.OpenAsync(
            "https://github.com/yukiaddiction/Screen-Painter",
            BrowserLaunchMode.SystemPreferred);
    }
}
