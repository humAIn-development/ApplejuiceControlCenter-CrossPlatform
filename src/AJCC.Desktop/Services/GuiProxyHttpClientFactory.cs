using System.Net;

namespace AJCC.Desktop.Services;

public static class GuiProxySessionCredentials
{
    private static readonly object SyncRoot = new();
    private static string _password = string.Empty;

    public static string Password
    {
        get
        {
            lock (SyncRoot)
                return _password;
        }
    }

    public static void SetPassword(string? password)
    {
        lock (SyncRoot)
            _password = password ?? string.Empty;
    }

    public static void ClearPassword()
        => SetPassword(string.Empty);
}

public static class GuiProxyHttpClientFactory
{
    public static HttpClient Create(TimeSpan timeout)
    {
        GuiProxyConfiguration configuration = new GuiProxyConfigurationStore().Load();
        HttpClient client = new(
            CreateHandler(configuration, GuiProxySessionCredentials.Password),
            disposeHandler: true)
        {
            Timeout = timeout
        };
        return client;
    }

    public static HttpClientHandler CreateHandler(
        GuiProxyConfiguration configuration,
        string? sessionPassword = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        GuiProxyConfiguration normalized = configuration.Normalize();
        HttpClientHandler handler = new();

        if (!normalized.Enabled)
        {
            handler.UseProxy = false;
            return handler;
        }

        Uri proxyUri = BuildProxyUri(normalized.Host, normalized.Port);
        WebProxy proxy = new(proxyUri);

        if (string.Equals(
                normalized.Mode,
                GuiProxyConfiguration.PrivateMode,
                StringComparison.Ordinal))
        {
            proxy.Credentials = new NetworkCredential(
                normalized.UserName,
                sessionPassword ?? string.Empty);
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;
        return handler;
    }

    private static Uri BuildProxyUri(string host, int port)
    {
        string value = (host ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Proxy-Adresse ist leer.");

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "http://" + value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? proxyUri))
            throw new InvalidOperationException("Proxy-Adresse ist ungültig.");

        UriBuilder builder = new(proxyUri);
        if (port > 0)
            builder.Port = port;

        return builder.Uri;
    }
}
