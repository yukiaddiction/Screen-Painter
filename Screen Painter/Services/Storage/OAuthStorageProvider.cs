using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Authentication;
using Screen_Painter.Models;
using Screen_Painter.Services.Security;

namespace Screen_Painter.Services.Storage;

public class OAuthStorageProvider : IStorageProvider
{
    private readonly ISecureStorageService _secureStorage;
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

    public StorageType SupportedType => StorageType.OAuthCloud;

    public OAuthStorageProvider(ISecureStorageService secureStorage)
    {
        _secureStorage = secureStorage;
    }

    public async Task<string?> AuthenticateUserAsync(string authUrl, string callbackScheme)
    {
        try
        {
            var authResult = await WebAuthenticator.Default.AuthenticateAsync(new Uri(authUrl), new Uri($"{callbackScheme}://"));
            var token = authResult?.AccessToken ?? authResult?.Properties?.GetValueOrDefault("access_token");
            return token;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<string>> ListImageIdentifiersAsync(FolderSource folderSource)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(folderSource.PathOrUrl))
            return result;

        try
        {
            var token = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedPasswordOrToken);
            if (string.IsNullOrEmpty(token))
                return result;

            var request = new HttpRequestMessage(HttpMethod.Get, folderSource.PathOrUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await HttpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var identifiers = ParseIdentifiersFromJson(content);
                if (identifiers.Count > 0)
                    return identifiers;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OAuth ListImages Error]: {ex.Message}");
        }

        return result;
    }

    public Task<List<string>> ListSubfoldersAsync(FolderSource folderSource, string currentPath)
    {
        var result = new List<string>();
        return Task.FromResult(result);
    }

    public async Task<Stream?> DownloadImageStreamAsync(FolderSource folderSource, string imageIdentifier)
    {
        try
        {
            var token = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedPasswordOrToken);
            var request = new HttpRequestMessage(HttpMethod.Get, imageIdentifier);

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OAuth Download Error]: {ex.Message}");
        }

        return null;
    }

    private static List<string> ParseIdentifiersFromJson(string jsonContent)
    {
        var identifiers = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                identifiers.AddRange(root.EnumerateArray()
                    .Select(ExtractUrlFromElement)
                    .Where(u => !string.IsNullOrEmpty(u))!);
                return identifiers;
            }

            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                identifiers.AddRange(files.EnumerateArray()
                    .Select(ExtractUrlFromElement)
                    .Where(u => !string.IsNullOrEmpty(u))!);
                return identifiers;
            }

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                identifiers.AddRange(items.EnumerateArray()
                    .Select(ExtractUrlFromElement)
                    .Where(u => !string.IsNullOrEmpty(u))!);
                return identifiers;
            }
        }
        catch (JsonException)
        {
        }

        return identifiers;
    }

    private static string? ExtractUrlFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value) && IsImageUrl(value))
                return value;
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            {
                var value = url.GetString();
                if (!string.IsNullOrWhiteSpace(value) && IsImageUrl(value))
                    return value;
            }
            if (element.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
            {
                var value = path.GetString();
                if (!string.IsNullOrWhiteSpace(value) && IsImageUrl(value))
                    return value;
            }
        }

        return null;
    }

    private static bool IsImageUrl(string value)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".heic", ".heif"
        };

        foreach (var ext in extensions)
        {
            if (value.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
