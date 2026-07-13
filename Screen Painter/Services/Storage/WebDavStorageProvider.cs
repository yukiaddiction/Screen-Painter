using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Screen_Painter.Models;
using Screen_Painter.Services.Security;

namespace Screen_Painter.Services.Storage;

public class WebDavDiagnosticResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ItemsFound { get; set; }
    public List<string> Details { get; set; } = new();
}

public class WebDavStorageProvider : IStorageProvider
{
    private readonly ISecureStorageService _secureStorage;
    private static readonly HttpClient HttpClient;

    private static XDocument ParseWebDavXml(string xml)
    {
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null
        });
        return XDocument.Load(reader);
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ServerSemaphores = new();
    private const int MaxConcurrentPerServer = 3;

    private static SemaphoreSlim GetServerSemaphore(string url)
    {
        var uri = new Uri(url);
        var baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return ServerSemaphores.GetOrAdd(baseUrl, _ => new SemaphoreSlim(MaxConcurrentPerServer, MaxConcurrentPerServer));
    }
    private static readonly string PropfindBody = 
        "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n" +
        "<d:propfind xmlns:d=\"DAV:\">\r\n" +
        "  <d:prop>\r\n" +
        "    <d:resourcetype/>\r\n" +
        "    <d:displayname/>\r\n" +
        "  </d:prop>\r\n" +
        "</d:propfind>";

    static WebDavStorageProvider()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        HttpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(AppConstants.HttpRequestTimeoutSeconds),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 ScreenPainter/1.0");
    }

    public StorageType SupportedType => StorageType.WebDav;

    public WebDavStorageProvider(ISecureStorageService secureStorage)
    {
        _secureStorage = secureStorage;
    }

    public async Task<WebDavDiagnosticResult> TestWebDavConnectionAsync(string serverUrl, string username, string password)
    {
        var result = new WebDavDiagnosticResult();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            result.Message = "Server URL is empty.";
            return result;
        }

        string normalizedUrl = NormalizeWebDavUrl(serverUrl);
        result.Details.Add($"Normalized URL: {normalizedUrl}");

        try
        {
            var (response, contentString, finalUrl) = await SendWebDavPropfindAsync(normalizedUrl, username, password);
            result.StatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MultiStatus)
            {
                result.Success = true;
                result.Message = $"Connected successfully (HTTP {(int)response.StatusCode} {response.ReasonPhrase})";

                if (!string.IsNullOrEmpty(contentString))
                {
                    try
                    {
                        var doc = ParseWebDavXml(contentString);
                        var responseNodes = doc.Descendants().Where(x => x.Name.LocalName.Equals("response", StringComparison.OrdinalIgnoreCase));
                        result.ItemsFound = responseNodes.Count();
                        result.Details.Add($"Discovered {result.ItemsFound} XML response items from WebDAV server.");
                    }
                    catch (Exception xmlEx)
                    {
                        result.Details.Add($"XML Parsing warning: {xmlEx.Message}");
                    }
                }
            }
            else
            {
                result.Success = false;
                result.Message = $"HTTP Error {(int)response.StatusCode} ({response.ReasonPhrase})";
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    result.Details.Add("401 Unauthorized: Check username and password/token.");
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    result.Details.Add("404 Not Found: WebDAV URI path does not exist on server.");
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Connection failed: {ex.Message}";
            result.Details.Add(ex.ToString());
        }

        return result;
    }

    public async Task<string> ScanWebDavImagesDiagnosticAsync(FolderSource folderSource)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== WebDAV Image Scanner Diagnostics ===");
        sb.AppendLine($"Folder Name: {folderSource.Name}");
        sb.AppendLine($"Folder Path/URL: {folderSource.PathOrUrl}");

        try
        {
            var username = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedUsername) ?? string.Empty;
            var password = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedPasswordOrToken) ?? string.Empty;

            string normalizedUrl = NormalizeWebDavUrl(folderSource.PathOrUrl);
            var (response, xmlContent, finalUrl) = await SendWebDavPropfindAsync(normalizedUrl, username, password);

            sb.AppendLine($"HTTP Status: {(int)response.StatusCode} {response.ReasonPhrase}");
            sb.AppendLine($"Final Resolved URL: {finalUrl}");

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
            {
                sb.AppendLine($"[ERROR]: Server rejected PROPFIND with HTTP {(int)response.StatusCode}");
                return sb.ToString();
            }

            var doc = ParseWebDavXml(xmlContent);
            var responseNodes = doc.Descendants().Where(x => x.Name.LocalName.Equals("response", StringComparison.OrdinalIgnoreCase)).ToList();

            sb.AppendLine($"Total XML <response> nodes found: {responseNodes.Count}");

            int sampleCount = 0;
            int imageMatchCount = 0;

            foreach (var resp in responseNodes)
            {
                var hrefNode = resp.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase));
                var href = hrefNode?.Value;
                if (string.IsNullOrEmpty(href)) continue;

                var baseUri = new Uri(finalUrl.EndsWith('/') ? finalUrl : finalUrl + "/");
                var fullUri = new Uri(baseUri, href).ToString();
                var cleanPath = Uri.UnescapeDataString(new Uri(fullUri).AbsolutePath);
                var ext = Path.GetExtension(cleanPath).ToLowerInvariant();

                bool isCollection = resp.Descendants().Any(x => x.Name.LocalName.Equals("collection", StringComparison.OrdinalIgnoreCase));

                if (sampleCount < 5)
                {
                    sb.AppendLine($"Sample [{sampleCount + 1}]: Href='{href}' | IsCollection={isCollection} | Ext='{ext}'");
                    sampleCount++;
                }

                if (!isCollection && ImageExtensions.Valid.Contains(ext))
                {
                    imageMatchCount++;
                }
            }

            sb.AppendLine($"----------------------------------------");
            sb.AppendLine($"TOTAL VALID IMAGE FILES MATCHED: {imageMatchCount}");
            if (imageMatchCount == 0 && responseNodes.Count > 0)
            {
                sb.AppendLine("WARNING: Server returned XML response nodes, but 0 matched valid image extensions (.jpg, .png, .webp). Check sample hrefs above.");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[EXCEPTION]: {ex.Message}");
            sb.AppendLine(ex.ToString());
        }

        return sb.ToString();
    }

    private static string NormalizeWebDavUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return string.Empty;

        var url = rawUrl.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!url.EndsWith('/'))
        {
            url += "/";
        }

        return url;
    }

    private static void AddBasicAuth(HttpRequestMessage request, string username, string password)
    {
        if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password))
            return;

        var authBytes = Encoding.UTF8.GetBytes($"{username}:{password}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        Array.Clear(authBytes, 0, authBytes.Length);
    }

    private static async Task<(HttpResponseMessage response, string content, string finalUrl)> SendWebDavPropfindAsync(
        string targetUrl,
        string username,
        string password)
    {
        var semaphore = GetServerSemaphore(targetUrl);
        await semaphore.WaitAsync();
        try
        {
            string currentUrl = targetUrl;
            int maxRedirects = AppConstants.MaxHttpRedirects;

            for (int i = 0; i < maxRedirects; i++)
            {
                HttpResponseMessage? response = null;
                try
                {
                    var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
                    request.Headers.Add("Depth", "1");
                    request.Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml");

                    AddBasicAuth(request, username, password);

                    response = await HttpClient.SendAsync(request);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebDav Propfind Bodied Error]: {ex.Message}. Falling back to bodyless PROPFIND.");

                    var bodylessRequest = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
                    bodylessRequest.Headers.Add("Depth", "1");

                    AddBasicAuth(bodylessRequest, username, password);

                    response = await HttpClient.SendAsync(bodylessRequest);
                }

                // Handle manual redirect to preserve Basic Auth header
                if (response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.SeeOther ||
                    response.StatusCode == HttpStatusCode.TemporaryRedirect ||
                    (int)response.StatusCode == 308)
                {
                    var redirectLocation = response.Headers.Location;
                    if (redirectLocation != null)
                    {
                        var baseUri = new Uri(currentUrl);
                        currentUrl = new Uri(baseUri, redirectLocation).ToString();
                        if (!currentUrl.EndsWith('/')) currentUrl += "/";
                        continue;
                    }
                }

                // Fallback for servers picky about XML body
                if (response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                    response.StatusCode == HttpStatusCode.UnsupportedMediaType)
                {
                    var bodylessRequest = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
                    bodylessRequest.Headers.Add("Depth", "1");
                    AddBasicAuth(bodylessRequest, username, password);

                    var bodylessResponse = await HttpClient.SendAsync(bodylessRequest);
                    var bodylessContent = await bodylessResponse.Content.ReadAsStringAsync();
                    return (bodylessResponse, bodylessContent, currentUrl);
                }

                var contentString = await response.Content.ReadAsStringAsync();
                return (response, contentString, currentUrl);
            }

            throw new Exception("Too many HTTP redirects.");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<List<string>> ListImageIdentifiersAsync(FolderSource folderSource)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(folderSource.PathOrUrl))
            return result;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resultSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await ScanWebDavDirectoryRecursiveAsync(folderSource, NormalizeWebDavUrl(folderSource.PathOrUrl), result, visited, resultSet, currentDepth: 0, maxDepth: AppConstants.MaxWebDavRecursionDepth);
        return result;
    }

    private async Task ScanWebDavDirectoryRecursiveAsync(
        FolderSource folderSource,
        string currentUrl,
        List<string> resultList,
        HashSet<string> visitedUrls,
        HashSet<string> resultSet,
        int currentDepth,
        int maxDepth)
    {
        if (currentDepth > maxDepth || visitedUrls.Contains(currentUrl))
            return;

        visitedUrls.Add(currentUrl);

        try
        {
            var username = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedUsername) ?? string.Empty;
            var password = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedPasswordOrToken) ?? string.Empty;

            var (response, xmlContent, finalUrl) = await SendWebDavPropfindAsync(currentUrl, username, password);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
                return;

            var doc = ParseWebDavXml(xmlContent);
            var responseNodes = doc.Descendants().Where(x => x.Name.LocalName.Equals("response", StringComparison.OrdinalIgnoreCase));

            var subfolderUrls = new List<string>();

            foreach (var resp in responseNodes)
            {
                var hrefNode = resp.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase));
                var href = hrefNode?.Value;
                if (string.IsNullOrEmpty(href)) continue;

                var baseUri = new Uri(finalUrl.EndsWith('/') ? finalUrl : finalUrl + "/");
                var fullUri = new Uri(baseUri, href).ToString();

                bool isCollection = resp.Descendants().Any(x => x.Name.LocalName.Equals("collection", StringComparison.OrdinalIgnoreCase));

                if (isCollection)
                {
                    if (!string.Equals(fullUri.TrimEnd('/'), finalUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    {
                        subfolderUrls.Add(fullUri);
                    }
                }
                else
                {
                    var cleanPath = Uri.UnescapeDataString(new Uri(fullUri).AbsolutePath);
                    var ext = Path.GetExtension(cleanPath).ToLowerInvariant();
                    if (ImageExtensions.Valid.Contains(ext))
                    {
                        if (resultSet.Add(fullUri))
                        {
                            resultList.Add(fullUri);
                        }
                    }
                }
            }

            foreach (var subUrl in subfolderUrls)
            {
                await ScanWebDavDirectoryRecursiveAsync(folderSource, subUrl, resultList, visitedUrls, resultSet, currentDepth + 1, maxDepth);
            }
        }
        catch
        {
            // Suppress
        }
    }

    public async Task<List<string>> ListSubfoldersAsync(FolderSource folderSource, string currentPath)
    {
        var result = new List<string>();
        var targetUrl = string.IsNullOrEmpty(currentPath) ? NormalizeWebDavUrl(folderSource.PathOrUrl) : NormalizeWebDavUrl(currentPath);

        if (string.IsNullOrEmpty(targetUrl))
            return result;

        try
        {
            var username = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedUsername) ?? string.Empty;
            var password = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedPasswordOrToken) ?? string.Empty;

            var (response, xmlContent, finalUrl) = await SendWebDavPropfindAsync(targetUrl, username, password);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
                return result;

            var doc = ParseWebDavXml(xmlContent);
            var responseNodes = doc.Descendants().Where(x => x.Name.LocalName.Equals("response", StringComparison.OrdinalIgnoreCase));

            foreach (var resp in responseNodes)
            {
                var hrefNode = resp.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase));
                var href = hrefNode?.Value;

                bool isCollection = resp.Descendants().Any(x => x.Name.LocalName.Equals("collection", StringComparison.OrdinalIgnoreCase));

                if (isCollection && !string.IsNullOrEmpty(href))
                {
                    var baseUri = new Uri(finalUrl.EndsWith('/') ? finalUrl : finalUrl + "/");
                    var fullUri = new Uri(baseUri, href).ToString();

                    if (!string.Equals(fullUri.TrimEnd('/'), targetUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    {
                        if (!result.Contains(fullUri))
                        {
                            result.Add(fullUri);
                        }
                    }
                }
            }
        }
        catch
        {
            // Suppress
        }

        return result;
    }

    public async Task<Stream?> DownloadImageStreamAsync(FolderSource folderSource, string imageIdentifier)
    {
        var semaphore = GetServerSemaphore(imageIdentifier);
        await semaphore.WaitAsync();
        try
        {
            var username = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedUsername) ?? string.Empty;
            var password = await _secureStorage.DecryptAndGetAsync(folderSource.EncryptedPasswordOrToken) ?? string.Empty;

            string currentUrl = imageIdentifier;
            int maxRedirects = AppConstants.MaxHttpRedirects;

            for (int i = 0; i < maxRedirects; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                AddBasicAuth(request, username, password);

                var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode == HttpStatusCode.MovedPermanently ||
                    response.StatusCode == HttpStatusCode.Found ||
                    response.StatusCode == HttpStatusCode.SeeOther ||
                    response.StatusCode == HttpStatusCode.TemporaryRedirect ||
                    (int)response.StatusCode == 308)
                {
                    var loc = response.Headers.Location;
                    if (loc != null)
                    {
                        currentUrl = new Uri(new Uri(currentUrl), loc).ToString();
                        continue;
                    }
                }

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStreamAsync();
                }
            }
        }
        catch
        {
            // Suppress
        }
        finally
        {
            semaphore.Release();
        }

        return null;
    }
}
