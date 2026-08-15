using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Protocol;

namespace AJCC.Core.Services;

public sealed class AjPollingService
{
    private readonly AppleJuiceCoreClient _client;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private long _timestamp;
    private int _consecutiveErrors;
    private bool _connectionLostRaised;
    private int _pollSequence;
    private int _missingCoreTimestampCount;

    private const int RuntimeConnectionLostErrorThreshold = 6;
    private const int MissingCoreTimestampFullResyncThreshold = 3;

    public AjPollingService(AppleJuiceCoreClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public bool IsRunning => _cts is not null;
    public event Action<ModifiedParseResult, string>? ModifiedReceived;
    public event Action<string>? Log;
    public event Action<int, string>? ConnectionDegraded;
    public event Action<int>? ConnectionRestored;
    public event Action<string>? ConnectionLost;
    public event Action<int, string>? FullResyncRequested;

    public async Task StartAsync(AjState state, int intervalMs = 2000)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_cts is not null)
            return;
        if (intervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMs));

        _cts = new CancellationTokenSource();
        _consecutiveErrors = 0;
        _connectionLostRaised = false;
        _pollSequence = 0;
        _missingCoreTimestampCount = 0;
        _timestamp = Math.Max(0, state.LastTimestamp);

        if (string.IsNullOrWhiteSpace(state.SessionId))
        {
            try
            {
                string sessionXml = await _client.GetSessionXmlAsync(_cts.Token).ConfigureAwait(false);
                state.SessionId = AjXmlParser.ParseSessionId(sessionXml);
                Log?.Invoke("Session erzeugt: " + state.SessionId);
            }
            catch (Exception ex)
            {
                Log?.Invoke("Session konnte nicht erzeugt werden: " + ex.Message);
            }
        }

        _runTask = RunAsync(state, intervalMs, _cts.Token);
    }

    public void Stop()
    {
        CancellationTokenSource? cts = _cts;
        _cts = null;
        _runTask = null;
        cts?.Cancel();
        Log?.Invoke("Live-Polling gestoppt.");
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts = _cts;
        Task? runTask = _runTask;
        _cts = null;
        _runTask = null;

        if (cts is null)
            return;

        cts.Cancel();
        try
        {
            if (runTask is not null)
                await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }

        Log?.Invoke("Live-Polling kontrolliert gestoppt.");
    }

    private async Task RunAsync(AjState state, int intervalMs, CancellationToken token)
    {
        Log?.Invoke($"Live-Polling gestartet: Java-Style-Zentraltakt {intervalMs:N0} ms, kein Refresh-Boost.");

        while (!token.IsCancellationRequested)
        {
            try
            {
                string filter = GetJavaStyleModifiedFilter();
                string xml = await _client.GetModifiedXmlAsync(_timestamp, state.SessionId, filter, token).ConfigureAwait(false);
                ModifiedParseResult result = AjXmlParser.ParseModified(xml);

                if (result.CoreTimestamp > 0)
                {
                    _timestamp = result.CoreTimestamp;
                    _missingCoreTimestampCount = 0;
                }
                else
                {
                    _missingCoreTimestampCount++;
                    Log?.Invoke($"modified.xml enthielt keinen Core-Zeitstempel ({_missingCoreTimestampCount:N0}/{MissingCoreTimestampFullResyncThreshold:N0}); letzter Core-Zeitstempel bleibt erhalten.");

                    if (_missingCoreTimestampCount >= MissingCoreTimestampFullResyncThreshold)
                    {
                        int missingCount = _missingCoreTimestampCount;
                        _missingCoreTimestampCount = 0;
                        FullResyncRequested?.Invoke(missingCount, "modified.xml liefert wiederholt keinen Core-Zeitstempel.");
                    }
                }

                state.LastTimestamp = _timestamp;

                if (_consecutiveErrors > 0)
                {
                    int recoveredAfter = _consecutiveErrors;
                    _consecutiveErrors = 0;
                    Log?.Invoke($"Polling-Verbindung erholt: Core antwortet wieder nach {recoveredAfter:N0} Fehlversuch(en).");
                    ConnectionRestored?.Invoke(recoveredAfter);
                }
                else
                {
                    _consecutiveErrors = 0;
                }

                ModifiedReceived?.Invoke(result, xml);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    break;

                _consecutiveErrors++;
                Log?.Invoke($"Polling-Fehler ({_consecutiveErrors:N0}/{RuntimeConnectionLostErrorThreshold:N0}): " + ex.Message);
                ConnectionDegraded?.Invoke(_consecutiveErrors, ex.Message);

                if (_consecutiveErrors >= RuntimeConnectionLostErrorThreshold && !_connectionLostRaised)
                {
                    _connectionLostRaised = true;
                    ConnectionLost?.Invoke($"Core antwortet seit {_consecutiveErrors:N0} aufeinanderfolgenden Polling-Versuchen nicht. Letzter Fehler: {ex.Message}");
                    break;
                }
            }

            try
            {
                await Task.Delay(intervalMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private string GetJavaStyleModifiedFilter()
    {
        int sequence = Interlocked.Increment(ref _pollSequence);

        return sequence switch
        {
            1 => "down;uploads;server;informations;search",
            2 => "informations;user;search",
            _ => "ids;down;server;uploads;search;user;informations"
        };
    }
}
