using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using AJCC.Core.Helpers;
using AJCC.Core.Links;
using AJCC.Core.Models;
using AJCC.Core.Services;

namespace AJCC.Core.Protocol;

public sealed class AppleJuiceCoreClient
{
    private readonly HttpClient _httpClient;

    public AppleJuiceCoreClient(CoreEndpoint endpoint, string password = "", HttpClient? httpClient = null)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        Password = password ?? string.Empty;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public CoreEndpoint Endpoint { get; private set; }
    public string Password { private get; set; }
    public string? LastRequestUrl { get; private set; }
    public string? LastRawResponse { get; private set; }

    public event Action<string>? DiagnosticLog;

    public void Configure(CoreEndpoint endpoint, string password)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        Password = password ?? string.Empty;
    }

    public Task<string> GetSettingsXmlAsync(CancellationToken cancellationToken = default)
        => GetXmlAsync(AjEndpoints.Settings, null, cancellationToken);

    public Task<string> GetInformationXmlAsync(CancellationToken cancellationToken = default)
        => GetXmlAsync(AjEndpoints.Information, null, cancellationToken);

    public Task<string> GetShareXmlAsync(CancellationToken cancellationToken = default)
        => GetXmlAsync(AjEndpoints.Share, null, cancellationToken);

    public Task<string> GetSessionXmlAsync(CancellationToken cancellationToken = default)
        => GetXmlAsync(AjEndpoints.GetSession, null, cancellationToken);

    public Task<string> GetDirectoryXmlAsync(string? directory = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string>? parameters = string.IsNullOrWhiteSpace(directory)
            ? null
            : new Dictionary<string, string> { ["directory"] = directory };

        return GetXmlAsync(AjEndpoints.Directory, parameters, cancellationToken);
    }

    public Task<string> GetDownloadPartListXmlAsync(long id, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.DownloadPartList,
            new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);

    public Task<string> GetUserPartListXmlAsync(long id, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.UserPartList,
            new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);

    public Task<string> GetModifiedXmlAsync(long timestamp, string? sessionId = null, string? filter = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> parameters = new()
        {
            ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
            parameters["session"] = sessionId;

        if (!string.IsNullOrWhiteSpace(filter))
            parameters["filter"] = filter;

        return GetXmlAsync(AjEndpoints.Modified, parameters, cancellationToken);
    }

    public Task<string> SearchAsync(string searchText, CancellationToken cancellationToken = default)
        => ExecuteFunctionPostAsync(
            AjEndpoints.Search,
            new Dictionary<string, string> { ["search"] = searchText ?? string.Empty },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search" },
            readResponseBody: false,
            cancellationToken);

    public Task<string> CancelSearchAsync(long id, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.CancelSearch,
            new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);

    public Task<string> PauseDownloadAsync(long id, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.PauseDownload,
            new Dictionary<string, string> { ["ID"] = id.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);

    public Task<string> ResumeDownloadAsync(long id, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.ResumeDownload,
            new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);

    public Task<string> RenameDownloadAsync(long id, string name, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.RenameDownload,
            new Dictionary<string, string>
            {
                ["id"] = id.ToString(CultureInfo.InvariantCulture),
                ["name"] = name ?? string.Empty
            },
            cancellationToken);

    public Task<string> SetTargetDirAsync(long id, string dir, CancellationToken cancellationToken = default)
        => GetXmlPreservingDirectorySeparatorsAsync(
            AjEndpoints.SetTargetDir,
            new Dictionary<string, string>
            {
                ["id"] = id.ToString(CultureInfo.InvariantCulture),
                ["dir"] = dir ?? string.Empty
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dir" },
            cancellationToken);

    public Task<string> SetPowerDownloadAsync(long id, int powerDownload, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.SetPowerDownload,
            new Dictionary<string, string>
            {
                ["id"] = id.ToString(CultureInfo.InvariantCulture),
                ["Powerdownload"] = powerDownload.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);

    public Task<string> SetSettingsAsync(
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        Dictionary<string, string> copiedParameters = new(parameters.Count, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> parameter in parameters)
            copiedParameters[parameter.Key] = parameter.Value;

        return GetXmlAsync(AjEndpoints.SetSettings, copiedParameters, cancellationToken);
    }

    public Task<string> SetShareDirectoriesAsync(
        IEnumerable<AjShareDirectory> directories,
        CancellationToken cancellationToken = default)
        => SetShareDirectoriesAsync(directories, previousShareCount: 0, cancellationToken: cancellationToken);

    public async Task<string> SetShareDirectoriesAsync(
        IEnumerable<AjShareDirectory> directories,
        int previousShareCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);

        List<AjShareDirectory> validDirectories = directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory.Name))
            .ToList();
        int desiredShareCount = validDirectories.Count;
        previousShareCount = Math.Max(0, previousShareCount);

        if (previousShareCount > desiredShareCount)
        {
            Dictionary<string, string> clearParameters = new();
            for (int index = 0; index < validDirectories.Count; index++)
            {
                AjShareDirectory directory = validDirectories[index];
                int slot = index + 1;
                clearParameters[$"sharedirectory{slot}"] = directory.Name;
                clearParameters[$"sharesub{slot}"] = directory.ShareMode.Equals("subdirectory", StringComparison.OrdinalIgnoreCase)
                    ? "true"
                    : "false";
            }

            for (int slot = desiredShareCount + 1; slot <= previousShareCount; slot++)
            {
                clearParameters[$"sharedirectory{slot}"] = string.Empty;
                clearParameters[$"sharesub{slot}"] = "false";
            }

            clearParameters["countshares"] = previousShareCount.ToString(CultureInfo.InvariantCulture);
            await GetXmlAsync(AjEndpoints.SetSettings, clearParameters, cancellationToken).ConfigureAwait(false);
        }

        Dictionary<string, string> parameters = new();
        for (int index = 0; index < validDirectories.Count; index++)
        {
            AjShareDirectory directory = validDirectories[index];
            int slot = index + 1;
            parameters[$"sharedirectory{slot}"] = directory.Name;
            parameters[$"sharesub{slot}"] = directory.ShareMode.Equals("subdirectory", StringComparison.OrdinalIgnoreCase)
                ? "true"
                : "false";
        }

        parameters["countshares"] = desiredShareCount.ToString(CultureInfo.InvariantCulture);
        return await GetXmlAsync(AjEndpoints.SetSettings, parameters, cancellationToken).ConfigureAwait(false);
    }

    public Task<string> SetPriorityAsync(long id, int priority, CancellationToken cancellationToken = default)
        => SetPriorityAsync(new[] { id }, priority, cancellationToken);

    public Task<string> SetPriorityAsync(IEnumerable<long> ids, int priority, CancellationToken cancellationToken = default)
    {
        List<long> idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            throw new ArgumentException("Keine ID übergeben.", nameof(ids));

        Dictionary<string, string> parameters = new()
        {
            ["id"] = idList[0].ToString(CultureInfo.InvariantCulture),
            ["priority"] = priority.ToString(CultureInfo.InvariantCulture)
        };

        for (int index = 1; index < idList.Count; index++)
            parameters[$"id{index}"] = idList[index].ToString(CultureInfo.InvariantCulture);

        return GetXmlAsync(AjEndpoints.SetPriority, parameters, cancellationToken);
    }

    public Task<string> CleanDownloadListAsync(CancellationToken cancellationToken = default)
        => GetXmlAsync(AjEndpoints.CleanDownloadList, null, cancellationToken);

    public Task<string> ServerLoginAsync(long id, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.ServerLogin,
            new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);

    public Task<string> RemoveServerAsync(long id, CancellationToken cancellationToken = default)
        => GetXmlAsync(
            AjEndpoints.RemoveServer,
            new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) },
            cancellationToken);

    public async Task<string> ProcessLinkAsync(
        string link,
        AjCoreCompatibilityProfile? compatibilityProfile = null,
        string subdir = "",
        CancellationToken cancellationToken = default)
    {
        AjProcessLinkResult result = await ProcessLinkDetailedAsync(link, compatibilityProfile, subdir, cancellationToken).ConfigureAwait(false);
        return result.RawResponse;
    }

    public async Task<AjProcessLinkResult> ProcessLinkDetailedAsync(
        string link,
        AjCoreCompatibilityProfile? compatibilityProfile = null,
        string subdir = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(link))
            throw new ArgumentException("AJFSP-Link fehlt.", nameof(link));

        AjCoreCompatibilityProfile effectiveProfile = compatibilityProfile ?? AjCoreCompatibilityProfile.FromCoreVersion(null);
        Dictionary<string, string> parameters = new()
        {
            ["link"] = link.Trim()
        };

        if (effectiveProfile.SupportsProcessLinkSubdir && !string.IsNullOrWhiteSpace(subdir))
            parameters["subdir"] = subdir.Trim();

        string response = await GetXmlAsync(AjEndpoints.ProcessLink, parameters, cancellationToken).ConfigureAwait(false);
        AjProcessLinkResult result = AjProcessLinkResult.FromResponse(response);
        Trace($"processlink fachliche Antwort: {result.StatusText}");
        return result;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string xml = await GetSettingsXmlAsync(cancellationToken).ConfigureAwait(false);
            return AnalyzeSettingsResponseForLogin(xml, LastRequestUrl ?? string.Empty, LastRawResponse ?? xml);
        }
        catch (HttpRequestException ex)
        {
            string response = LastRawResponse ?? string.Empty;
            string message = LooksLikePasswordRejection(response)
                || ex.Message.Contains("401", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase)
                    ? "Passwort wurde vom Core abgewiesen oder der Zugriff ist nicht erlaubt."
                    : "Core antwortet nicht korrekt oder weist die Verbindung ab: " + ex.Message;
            return new ConnectionTestResult(false, message, LastRequestUrl ?? string.Empty, response);
        }
        catch (TaskCanceledException ex)
        {
            return new ConnectionTestResult(false, "Zeitüberschreitung beim Verbindungstest: " + ex.Message, LastRequestUrl ?? string.Empty, LastRawResponse ?? string.Empty);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, "Keine Coreverbindung möglich: " + ex.Message, LastRequestUrl ?? string.Empty, LastRawResponse ?? string.Empty);
        }
    }

    public async Task<string> GetXmlAsync(
        string path,
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> allParameters = parameters is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(parameters);

        allParameters["password"] = SecurityHelper.ToMd5IfNeeded(Password);
        Uri requestUri = BuildUri(path, allParameters);

        LastRequestUrl = SecurityHelper.MaskSensitiveData(requestUri.ToString());
        Trace($"HTTP GET -> {LastRequestUrl}");

        using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        string body = TryDecodeBody(bytes, response);

        LastRawResponse = SecurityHelper.MaskSensitiveData(body);
        Trace($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} <- {path} | Antwortlänge: {body.Length:N0}");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        return body;
    }

    private async Task<string> GetXmlPreservingDirectorySeparatorsAsync(
        string path,
        Dictionary<string, string>? parameters,
        ISet<string> preserveSeparatorParameterNames,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> allParameters = parameters is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(parameters);

        allParameters["password"] = SecurityHelper.ToMd5IfNeeded(Password);
        Uri requestUri = BuildUri(path, allParameters, null, preserveSeparatorParameterNames);

        LastRequestUrl = SecurityHelper.MaskSensitiveData(requestUri.ToString());
        Trace($"HTTP GET -> {LastRequestUrl}");

        using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        string body = TryDecodeBody(bytes, response);

        LastRawResponse = SecurityHelper.MaskSensitiveData(body);
        Trace($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} <- {path} | Antwortlänge: {body.Length:N0}");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        return body;
    }

    private async Task<string> ExecuteFunctionPostAsync(
        string functionPath,
        Dictionary<string, string>? parameters,
        ISet<string>? javaGuiStyleEncodingParameterNames,
        bool readResponseBody,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> allParameters = parameters is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(parameters);

        allParameters["password"] = SecurityHelper.ToMd5IfNeeded(Password);
        Uri requestUri = BuildUri(functionPath, allParameters, javaGuiStyleEncodingParameterNames);

        LastRequestUrl = SecurityHelper.MaskSensitiveData(requestUri.ToString());
        Trace($"HTTP POST -> {LastRequestUrl}");

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        string body;
        if (readResponseBody)
        {
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            body = TryDecodeBody(bytes, response);
        }
        else
        {
            body = "OK";
        }

        LastRawResponse = SecurityHelper.MaskSensitiveData(body);
        Trace($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} <- {functionPath} | Antwortlänge: {body.Length:N0}");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        return body;
    }

    private Uri BuildUri(
        string path,
        IReadOnlyDictionary<string, string> parameters,
        ISet<string>? javaGuiStyleEncodingParameterNames = null,
        ISet<string>? preserveSeparatorParameterNames = null)
    {
        Uri resolved = Endpoint.Resolve(path);
        UriBuilder builder = new(resolved);
        StringBuilder query = new();

        foreach (KeyValuePair<string, string> pair in parameters)
        {
            if (query.Length > 0)
                query.Append('&');

            bool preserveSeparators = preserveSeparatorParameterNames?.Contains(pair.Key) == true;
            bool javaGuiStyle = javaGuiStyleEncodingParameterNames?.Contains(pair.Key) == true;
            query.Append(WebUtility.UrlEncode(pair.Key));
            query.Append('=');
            query.Append(preserveSeparators
                ? EncodeQueryValuePreservingDirectorySeparators(pair.Value)
                : javaGuiStyle
                    ? Uri.EscapeDataString((pair.Value ?? string.Empty).Trim())
                    : WebUtility.UrlEncode(pair.Value));
        }

        builder.Query = query.ToString();
        return builder.Uri;
    }

    private static string EncodeQueryValuePreservingDirectorySeparators(string? value)
    {
        string encoded = WebUtility.UrlEncode(value ?? string.Empty) ?? string.Empty;
        return encoded
            .Replace("%2f", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("%5c", "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static ConnectionTestResult AnalyzeSettingsResponseForLogin(string xml, string request, string responseForLog)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new ConnectionTestResult(false, "Der Core hat leer geantwortet.", request, responseForLog);

        if (LooksLikePasswordRejection(xml))
            return new ConnectionTestResult(false, "Passwort wurde vom Core abgewiesen.", request, responseForLog);

        try
        {
            XDocument document = XDocument.Parse(xml);
            XElement? root = document.Root;
            if (root is null)
                return new ConnectionTestResult(false, "Der Core hat keine gültige XML-Struktur geliefert.", request, responseForLog);

            bool rootLooksLikeSettings = root.Name.LocalName.Equals("settings", StringComparison.OrdinalIgnoreCase);
            bool hasCoreSettingsMarkers = root.DescendantsAndSelf().Any(e => e.Name.LocalName.Equals("xmlport", StringComparison.OrdinalIgnoreCase))
                && root.DescendantsAndSelf().Any(e => e.Name.LocalName.Equals("incomingdirectory", StringComparison.OrdinalIgnoreCase))
                && root.DescendantsAndSelf().Any(e => e.Name.LocalName.Equals("temporarydirectory", StringComparison.OrdinalIgnoreCase));

            if (rootLooksLikeSettings && hasCoreSettingsMarkers)
                return new ConnectionTestResult(true, "Verbindung erfolgreich. Core-Settings wurden gültig gelesen.", request, xml);

            return new ConnectionTestResult(false, "Antwort erhalten, aber keine gültige settings.xml des AppleJuice-Cores erkannt.", request, responseForLog);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, "Antwort erhalten, aber nicht als gültige Core-settings.xml lesbar: " + ex.Message, request, responseForLog);
        }
    }

    private static bool LooksLikePasswordRejection(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        string text = response.ToLowerInvariant();
        return text.Contains("wrong password")
            || text.Contains("bad password")
            || text.Contains("invalid password")
            || text.Contains("unauthorized")
            || text.Contains("forbidden")
            || text.Contains("access denied");
    }

    private static string TryDecodeBody(byte[] bytes, HttpResponseMessage response)
    {
        try
        {
            if (response.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                using MemoryStream input = new(bytes);
                using GZipStream gzip = new(input, CompressionMode.Decompress);
                using StreamReader reader = new(gzip, Encoding.UTF8);
                return reader.ReadToEnd();
            }

            if (response.Content.Headers.ContentEncoding.Contains("deflate"))
            {
                using MemoryStream input = new(bytes);
                using DeflateStream deflate = new(input, CompressionMode.Decompress);
                using StreamReader reader = new(deflate, Encoding.UTF8);
                return reader.ReadToEnd();
            }
        }
        catch
        {
            // Fall back to UTF-8 below.
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private void Trace(string message)
    {
        try
        {
            DiagnosticLog?.Invoke(SecurityHelper.MaskSensitiveData(message));
        }
        catch
        {
            // Diagnostics must never disturb Core communication.
        }
    }
}

public sealed record ConnectionTestResult(bool Success, string Message, string Request, string Response);
