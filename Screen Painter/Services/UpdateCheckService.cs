using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Screen_Painter.Models;

namespace Screen_Painter.Services;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckForUpdateAsync(bool bypassCooldown = false);
}

public class UpdateCheckService : IUpdateCheckService
{
    private const string RepoOwner = "yukiaddiction";
    private const string RepoName = "Screen-Painter";
    private const string LastAutoCheckPrefKey = "LastAutoUpdateCheckUtc";
    private static readonly string ApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly TimeSpan ManualCheckCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AutoCheckCooldown = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private DateTime _lastManualCheckUtc = DateTime.MinValue;

    public UpdateCheckService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{RepoOwner}/{RepoName} App Update Checker (net9.0-maui)");
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(bool bypassCooldown = false)
    {
        var now = DateTime.UtcNow;

        if (!bypassCooldown)
        {
            var lastAutoTicks = Preferences.Default.Get(LastAutoCheckPrefKey, 0L);
            var lastAutoCheckUtc = lastAutoTicks > 0 ? new DateTime(lastAutoTicks, DateTimeKind.Utc) : DateTime.MinValue;
            if (now - lastAutoCheckUtc < AutoCheckCooldown)
                return new UpdateCheckResult { UpdateAvailable = false };
        }

        if (bypassCooldown && now - _lastManualCheckUtc < ManualCheckCooldown)
            return new UpdateCheckResult { UpdateAvailable = false };

        _lastManualCheckUtc = now;
        Preferences.Default.Set(LastAutoCheckPrefKey, now.Ticks);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.GetAsync(ApiUrl, cts.Token);

            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult { ErrorMessage = $"Server returned {response.StatusCode}" };

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latestTag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var releaseUrl = root.GetProperty("html_url").GetString() ?? string.Empty;
            var body = root.GetProperty("body").GetString() ?? string.Empty;

            var latestVersion = ExtractNumericVersion(latestTag);
            var currentVersion = ExtractNumericVersion(Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString);

            // The GitHub /releases/latest endpoint automatically excludes drafts and
            // pre-releases, so we don't need to filter those fields here.

            bool updateAvailable = false;

            if (latestVersion == null || currentVersion == null)
            {
                // Either version string cannot be parsed reliably:
                //  - currentVersion == null  → dev / sideloaded build with a non-standard
                //    version; never suggest an update (dev phone always appears "latest").
                //  - latestVersion == null   → GitHub tag is malformed; skip.
                updateAvailable = false;
            }
            else
            {
                // Only suggest an update if the remote version is strictly greater.
                // When the local dev build has a higher version (common during active
                // development), this correctly returns false.
                updateAvailable = latestVersion > currentVersion;
            }

            return new UpdateCheckResult
            {
                UpdateAvailable = updateAvailable,
                LatestVersion = latestVersion?.ToString() ?? latestTag,
                ReleaseUrl = releaseUrl,
                ReleaseNotes = TruncateReleaseNotes(body)
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Extracts a clean <see cref="Version"/> from a tag or display-version string,
    /// stripping prefixes like "v" and suffixes like "-beta", "-dev", "+build", etc.
    /// Returns null if the result cannot be parsed into a valid Version, which acts as
    /// a safe-guard: unparseable versions (common with dev/sideloaded builds) never
    /// trigger a false-positive update notification.
    /// </summary>
    private static Version? ExtractNumericVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Match the first valid version-like segment: digits and dots, optionally
        // followed by a pre-release suffix or build metadata (which we discard).
        // Examples matched:
        //   "v1.2.3"         → "1.2.3"
        //   "1.1.6-beta.2"   → "1.1.6"
        //   "1.1.6+dev123"   → "1.1.6"
        //   "1.1.6.10"       → "1.1.6.10"
        //   "v1.0"           → "1.0.0" (TryParse normalises to 1.0.0.0)
        var match = Regex.Match(raw, @"(\d+(?:\.\d+){1,3})");
        if (!match.Success)
            return null;

        return Version.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
    }

    private static string TruncateReleaseNotes(string body)
    {
        if (string.IsNullOrEmpty(body))
            return string.Empty;

        const int maxLength = 500;
        if (body.Length <= maxLength)
            return body;

        var truncated = body[..maxLength];
        var lastNewline = truncated.LastIndexOf('\n');
        if (lastNewline >= maxLength - 100)
            truncated = truncated[..lastNewline];

        return truncated + "\n\n…";
    }
}
