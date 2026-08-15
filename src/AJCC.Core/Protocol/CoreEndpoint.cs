namespace AJCC.Core.Protocol;

public sealed class CoreEndpoint
{
    public CoreEndpoint(string scheme, string host, int? port = null, string? basePath = null, string? displayName = null)
    {
        Scheme = NormalizeScheme(scheme);
        Host = string.IsNullOrWhiteSpace(host)
            ? throw new ArgumentException("Core host must not be empty.", nameof(host))
            : host.Trim();

        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Core port must be between 1 and 65535.");

        Port = port;
        BasePath = NormalizeBasePath(basePath);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Host : displayName.Trim();
        BaseUri = BuildBaseUri();
    }

    public string Scheme { get; }
    public string Host { get; }
    public int? Port { get; }
    public string BasePath { get; }
    public string DisplayName { get; }
    public Uri BaseUri { get; }

    public static CoreEndpoint Parse(string value, string? displayName = null)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri))
            throw new ArgumentException("Core endpoint must be an absolute http/https URI.", nameof(value));

        return FromUri(uri, displayName);
    }

    public static CoreEndpoint FromUri(Uri uri, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Core endpoint must be an absolute URI.", nameof(uri));

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Credentials must not be embedded in the Core endpoint URI.", nameof(uri));

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Core endpoint must not contain a query or fragment.", nameof(uri));

        int? port = uri.IsDefaultPort ? null : uri.Port;
        return new CoreEndpoint(uri.Scheme, uri.Host, port, uri.AbsolutePath, displayName);
    }

    public Uri Resolve(string endpointPath)
    {
        if (string.IsNullOrWhiteSpace(endpointPath))
            return BaseUri;

        return new Uri(BaseUri, endpointPath.Trim().TrimStart('/'));
    }

    public override string ToString() => BaseUri.ToString();

    private Uri BuildBaseUri()
    {
        UriBuilder builder = new(Scheme, Host)
        {
            Path = BasePath
        };

        builder.Port = Port ?? -1;

        string absolute = builder.Uri.AbsoluteUri;
        if (!absolute.EndsWith("/", StringComparison.Ordinal))
            absolute += "/";

        return new Uri(absolute, UriKind.Absolute);
    }

    private static string NormalizeScheme(string scheme)
    {
        string normalized = (scheme ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "http" => "http",
            "https" => "https",
            _ => throw new ArgumentException("Only http and https Core endpoints are supported.", nameof(scheme))
        };
    }

    private static string NormalizeBasePath(string? basePath)
    {
        string normalized = (basePath ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        return normalized.Length == 0 ? "/" : "/" + normalized + "/";
    }
}
