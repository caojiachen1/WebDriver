using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WebDriver.Services;

public class WebDavItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

public class WebDavClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _baseUrl = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;

    public WebDavClient()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public void SetCredentials(string baseUrl, string username, string password)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _username = username;
        _password = password;

        // Set basic authentication
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Options, _baseUrl);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<WebDavItem>> ListDirectoryAsync(string path = "/")
    {
        try
        {
            var url = $"{_baseUrl}{path.TrimStart('/')}";
            if (!url.EndsWith("/") && path != "/")
                url += "/";

            var propfindXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:propfind xmlns:D=""DAV:"">
    <D:prop>
        <D:displayname/>
        <D:getcontentlength/>
        <D:getcontenttype/>
        <D:getlastmodified/>
        <D:resourcetype/>
    </D:prop>
</D:propfind>";

            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Content = new StringContent(propfindXml, Encoding.UTF8, "application/xml");
            request.Headers.Add("Depth", "1");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParsePropfindResponse(responseContent, path);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to list directory: {ex.Message}", ex);
        }
    }

    public async Task<Stream> DownloadFileAsync(string filePath)
    {
        try
        {
            var url = $"{_baseUrl}{filePath.TrimStart('/')}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to download file: {ex.Message}", ex);
        }
    }

    public async Task UploadFileAsync(string remotePath, Stream fileStream)
    {
        try
        {
            var url = $"{_baseUrl}{remotePath.TrimStart('/')}";
            var content = new StreamContent(fileStream);
            var response = await _httpClient.PutAsync(url, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to upload file: {ex.Message}", ex);
        }
    }

    public async Task CreateDirectoryAsync(string directoryPath)
    {
        try
        {
            var url = $"{_baseUrl}{directoryPath.TrimStart('/')}";
            if (!url.EndsWith("/"))
                url += "/";

            var request = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to create directory: {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(string path)
    {
        try
        {
            var url = $"{_baseUrl}{path.TrimStart('/')}";
            var response = await _httpClient.DeleteAsync(url);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to delete: {ex.Message}", ex);
        }
    }

    public async Task MoveAsync(string sourcePath, string destinationPath)
    {
        try
        {
            var sourceUrl = $"{_baseUrl}{sourcePath.TrimStart('/')}";
            var destinationUrl = $"{_baseUrl}{destinationPath.TrimStart('/')}";

            var request = new HttpRequestMessage(new HttpMethod("MOVE"), sourceUrl);
            request.Headers.Add("Destination", destinationUrl);
            request.Headers.Add("Overwrite", "T");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to move: {ex.Message}", ex);
        }
    }

    private List<WebDavItem> ParsePropfindResponse(string xml, string currentPath)
    {
        var items = new List<WebDavItem>();
        
        try
        {
            var doc = XDocument.Parse(xml);
            var ns = XNamespace.Get("DAV:");
            
            var responses = doc.Descendants(ns + "response");
            
            foreach (var response in responses)
            {
                var href = response.Element(ns + "href")?.Value;
                if (string.IsNullOrEmpty(href))
                    continue;

                // Skip the current directory itself
                var decodedHref = Uri.UnescapeDataString(href);
                if (decodedHref.TrimEnd('/') == currentPath.TrimEnd('/'))
                    continue;

                var propstat = response.Element(ns + "propstat");
                var prop = propstat?.Element(ns + "prop");
                
                if (prop == null)
                    continue;

                var displayName = prop.Element(ns + "displayname")?.Value ?? 
                                 Path.GetFileName(decodedHref.TrimEnd('/'));
                
                var resourceType = prop.Element(ns + "resourcetype");
                var isDirectory = resourceType?.Element(ns + "collection") != null;
                
                var contentLength = prop.Element(ns + "getcontentlength")?.Value;
                var size = long.TryParse(contentLength, out var parsedSize) ? parsedSize : 0;
                
                var lastModified = prop.Element(ns + "getlastmodified")?.Value;
                var modifiedDate = DateTime.TryParse(lastModified, out var parsedDate) ? parsedDate : DateTime.MinValue;
                
                var contentType = prop.Element(ns + "getcontenttype")?.Value ?? "";

                items.Add(new WebDavItem
                {
                    Name = displayName,
                    FullPath = decodedHref,
                    IsDirectory = isDirectory,
                    Size = size,
                    LastModified = modifiedDate,
                    ContentType = contentType
                });
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse WebDAV response: {ex.Message}", ex);
        }

        return items.OrderBy(x => !x.IsDirectory).ThenBy(x => x.Name).ToList();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}