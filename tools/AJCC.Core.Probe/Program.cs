using AJCC.Core.Parsers;
using AJCC.Core.Protocol;
using AJCC.Core.Services;

namespace AJCC.Core.Probe;

internal static class Program
{
    private const string DefaultPasswordEnvironmentVariable = "AJCC_CORE_PASSWORD";

    public static async Task<int> Main(string[] args)
    {
        if (!TryReadArguments(args, out Uri? endpointUri, out string passwordEnvironmentVariable, out string? argumentError))
        {
            Console.Error.WriteLine(argumentError);
            PrintUsage();
            return 1;
        }

        string password = Environment.GetEnvironmentVariable(passwordEnvironmentVariable) ?? string.Empty;

        CoreEndpoint endpoint;
        try
        {
            endpoint = CoreEndpoint.FromUri(endpointUri!);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Endpoint ungültig: " + ex.Message);
            return 1;
        }

        using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        AppleJuiceCoreClient client = new(endpoint, password, httpClient);

        Console.WriteLine("AJCC-X Core Probe");
        Console.WriteLine("Endpoint: " + endpoint.BaseUri);
        Console.WriteLine("Passwort: " + (string.IsNullOrEmpty(password) ? "nicht gesetzt" : $"aus ${passwordEnvironmentVariable}"));

        ConnectionTestResult connection = await client.TestConnectionAsync();
        if (!connection.Success)
        {
            Console.Error.WriteLine("Verbindung fehlgeschlagen: " + connection.Message);
            return 2;
        }

        Console.WriteLine("Verbindung: OK");

        CoreBootstrapResult bootstrap;
        try
        {
            bootstrap = await new CoreRuntimeBootstrapper(client).LoadAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Bootstrap fehlgeschlagen: " + ex.Message);
            return 3;
        }

        Console.WriteLine("Bootstrap: OK");
        Console.WriteLine("Core-Version: " + (string.IsNullOrWhiteSpace(bootstrap.CoreVersion) ? "unbekannt" : bootstrap.CoreVersion));
        Console.WriteLine("XML-Port aus settings.xml: " + bootstrap.State.Settings.XmlPort);
        Console.WriteLine($"Netzwerk: {bootstrap.State.NetworkInfo.Users:N0} Nutzer / {bootstrap.State.NetworkInfo.Files:N0} Dateien");
        Console.WriteLine($"State: {bootstrap.State.Downloads.Count:N0} Downloads / {bootstrap.State.Uploads.Count:N0} Uploads / {bootstrap.State.Servers.Count:N0} Server / {bootstrap.State.Searches.Count:N0} Suchen");
        Console.WriteLine("Session: " + (string.IsNullOrWhiteSpace(bootstrap.State.SessionId) ? "fehlt" : "OK"));
        Console.WriteLine("Core-Timestamp: " + bootstrap.State.LastTimestamp);

        AjPollingService polling = new(client);
        TaskCompletionSource<ModifiedParseResult> firstPoll = new(TaskCreationOptions.RunContinuationsAsynchronously);
        polling.ModifiedReceived += (result, _) => firstPoll.TrySetResult(result);

        try
        {
            await polling.StartAsync(bootstrap.State, intervalMs: 1000);
            ModifiedParseResult result = await firstPoll.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Console.WriteLine("modified.xml Polling: OK");
            Console.WriteLine($"Poll: timestamp={bootstrap.State.LastTimestamp}, downloads={result.Downloads.Count:N0}, uploads={result.Uploads.Count:N0}, server={result.Servers.Count:N0}, searches={result.Searches.Count:N0}");
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("modified.xml Polling: kein Ergebnis innerhalb des Probe-Zeitfensters.");
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("modified.xml Polling fehlgeschlagen: " + ex.Message);
            return 4;
        }
        finally
        {
            await polling.StopAsync();
        }

        Console.WriteLine("Foundation Core-Probe erfolgreich.");
        return 0;
    }

    private static bool TryReadArguments(string[] args, out Uri? endpointUri, out string passwordEnvironmentVariable, out string? error)
    {
        endpointUri = null;
        passwordEnvironmentVariable = DefaultPasswordEnvironmentVariable;
        error = null;

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (arg.Equals("--endpoint", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length || !Uri.TryCreate(args[index], UriKind.Absolute, out endpointUri))
                {
                    error = "Für --endpoint muss eine absolute http/https-URI angegeben werden.";
                    return false;
                }
                continue;
            }

            if (arg.Equals("--password-env", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    error = "Für --password-env muss ein Variablenname angegeben werden.";
                    return false;
                }

                passwordEnvironmentVariable = args[index].Trim();
                continue;
            }

            if (arg is "--help" or "-h" or "/?")
            {
                error = "AJCC-X Core Probe";
                return false;
            }

            error = "Unbekanntes Argument: " + arg;
            return false;
        }

        if (endpointUri is null)
        {
            error = "--endpoint fehlt.";
            return false;
        }

        if (endpointUri.Scheme is not ("http" or "https"))
        {
            error = "--endpoint unterstützt nur http oder https.";
            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Verwendung:");
        Console.WriteLine("  dotnet run --project tools/AJCC.Core.Probe -- --endpoint http://127.0.0.1:9851/");
        Console.WriteLine("  dotnet run --project tools/AJCC.Core.Probe -- --endpoint https://core.example.org/applejuice/ --password-env AJCC_CORE_PASSWORD");
        Console.WriteLine();
        Console.WriteLine($"Das Core-Passwort wird ausschließlich aus einer Umgebungsvariable gelesen (Standard: {DefaultPasswordEnvironmentVariable}).");
    }
}
