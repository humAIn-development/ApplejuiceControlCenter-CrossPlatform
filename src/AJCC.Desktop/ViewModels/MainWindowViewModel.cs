using System.ComponentModel;
using System.Runtime.CompilerServices;
using AJCC.Core.Helpers;
using AJCC.Core.Links;
using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Protocol;
using AJCC.Core.Services;
using AJCC.Desktop.Platform;
using AJCC.Desktop.Services;
using Avalonia.Threading;

namespace AJCC.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly HostIncomingDirectoryPreparer _incomingDirectoryPreparer = new();
    private readonly LocalIncomingMappingStore _mappingStore = new();
    private HttpClient? _httpClient;
    private AppleJuiceCoreClient? _client;
    private AjPollingService? _polling;
    private AjState? _state;
    private string _endpointText = "http://127.0.0.1:8851/";
    private string _localIncomingMappingText = string.Empty;
    private string _statusText = "Nicht verbunden";
    private string _coreVersion = "-";
    private string _searchText = string.Empty;
    private AjDownload? _selectedDownload;
    private AjSearch? _selectedSearch;
    private AjSearchEntry? _selectedSearchEntry;
    private bool _isBusy;
    private bool _isConnected;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel()
    {
        _localIncomingMappingText = _mappingStore.Get(_endpointText);
    }

    public string EndpointText
    {
        get => _endpointText;
        set
        {
            string next = value ?? string.Empty;
            if (!SetField(ref _endpointText, next))
                return;

            LocalIncomingMappingText = _mappingStore.Get(next);
        }
    }

    public string LocalIncomingMappingText
    {
        get => _localIncomingMappingText;
        set => SetField(ref _localIncomingMappingText, value ?? string.Empty);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string CoreVersion
    {
        get => _coreVersion;
        private set => SetField(ref _coreVersion, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
                OnPropertyChanged(nameof(CanSearch));
        }
    }

    public AjDownload? SelectedDownload
    {
        get => _selectedDownload;
        set
        {
            if (!SetField(ref _selectedDownload, value))
                return;

            OnPropertyChanged(nameof(SelectedDownloadText));
            OnPropertyChanged(nameof(CanPauseSelectedDownload));
            OnPropertyChanged(nameof(CanResumeSelectedDownload));
            OnPropertyChanged(nameof(CanCancelSelectedDownload));
            OnPropertyChanged(nameof(CanRenameSelectedDownload));
            OnPropertyChanged(nameof(CanSetTargetDirectorySelectedDownload));
        }
    }

    public AjSearch? SelectedSearch
    {
        get => _selectedSearch;
        set
        {
            if (!SetField(ref _selectedSearch, value))
                return;

            SelectedSearchEntry = null;
            OnPropertyChanged(nameof(SelectedSearchEntries));
        }
    }

    public AjSearchEntry? SelectedSearchEntry
    {
        get => _selectedSearchEntry;
        set
        {
            if (!SetField(ref _selectedSearchEntry, value))
                return;

            OnPropertyChanged(nameof(SelectedSearchEntryText));
            OnPropertyChanged(nameof(CanDownloadSelectedSearchEntry));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(CanToggleConnection));
            OnPropertyChanged(nameof(CanEditConnectionSettings));
            OnPropertyChanged(nameof(CanSearch));
            OnPropertyChanged(nameof(CanPauseSelectedDownload));
            OnPropertyChanged(nameof(CanResumeSelectedDownload));
            OnPropertyChanged(nameof(CanCancelSelectedDownload));
            OnPropertyChanged(nameof(CanRenameSelectedDownload));
            OnPropertyChanged(nameof(CanSetTargetDirectorySelectedDownload));
            OnPropertyChanged(nameof(CanDownloadSelectedSearchEntry));
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetField(ref _isConnected, value))
                return;

            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ConnectionStateText));
            OnPropertyChanged(nameof(CanEditConnectionSettings));
            OnPropertyChanged(nameof(CanSearch));
            OnPropertyChanged(nameof(CanPauseSelectedDownload));
            OnPropertyChanged(nameof(CanResumeSelectedDownload));
            OnPropertyChanged(nameof(CanCancelSelectedDownload));
            OnPropertyChanged(nameof(CanRenameSelectedDownload));
            OnPropertyChanged(nameof(CanSetTargetDirectorySelectedDownload));
            OnPropertyChanged(nameof(CanDownloadSelectedSearchEntry));
        }
    }

    public bool IsDisconnected => !IsConnected;
    public bool CanToggleConnection => !IsBusy;
    public bool CanEditConnectionSettings => !IsConnected && !IsBusy;
    public bool CanSearch => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(SearchText);
    public bool CanPauseSelectedDownload => IsConnected && !IsBusy && DownloadActionSemantics.CanPause(SelectedDownload);
    public bool CanResumeSelectedDownload => IsConnected && !IsBusy && DownloadActionSemantics.CanResume(SelectedDownload);
    public bool CanCancelSelectedDownload => IsConnected && !IsBusy && DownloadActionSemantics.CanCancel(SelectedDownload);
    public bool CanRenameSelectedDownload => IsConnected && !IsBusy && DownloadActionSemantics.CanChangeMetadata(SelectedDownload);
    public bool CanSetTargetDirectorySelectedDownload => IsConnected && !IsBusy && DownloadActionSemantics.CanChangeMetadata(SelectedDownload);
    public bool CanDownloadSelectedSearchEntry => IsConnected && !IsBusy && IsValidSearchEntryForDownload(SelectedSearchEntry);
    public string ConnectButtonText => IsConnected ? "Trennen" : "Verbinden";
    public string ConnectionStateText => IsConnected ? "ONLINE" : "OFFLINE";
    public string SelectedDownloadText => SelectedDownload is null ? "Kein Download ausgewählt" : SelectedDownload.DisplayFilename;
    public string SelectedSearchEntryText => SelectedSearchEntry is null ? "Kein Treffer ausgewählt" : SelectedSearchEntry.Filename;

    public IEnumerable<AjDownload> Downloads => _state is null ? Array.Empty<AjDownload>() : _state.Downloads;
    public IEnumerable<AjUpload> Uploads => _state is null ? Array.Empty<AjUpload>() : _state.Uploads;
    public IEnumerable<AjServer> Servers => _state is null ? Array.Empty<AjServer>() : _state.Servers;
    public IEnumerable<AjSearch> Searches => _state is null ? Array.Empty<AjSearch>() : _state.Searches;
    public IEnumerable<AjSearchEntry> SelectedSearchEntries => SelectedSearch is null ? Array.Empty<AjSearchEntry>() : SelectedSearch.Entries;

    public string CoreNick => string.IsNullOrWhiteSpace(_state?.Settings.Nick) ? "-" : _state.Settings.Nick;
    public string CoreIncomingDirectory => string.IsNullOrWhiteSpace(_state?.Settings.IncomingDirectory) ? "-" : _state.Settings.IncomingDirectory;
    public string NetworkUsersText => _state is null ? "-" : _state.NetworkInfo.Users.ToString("N0");
    public string NetworkFilesText => _state is null ? "-" : _state.NetworkInfo.Files.ToString("N0");
    public string CreditsText => _state?.Information.CreditsText ?? "-";
    public string DownloadSpeedText => _state?.Information.DownloadSpeedText ?? "-";
    public string UploadSpeedText => _state?.Information.UploadSpeedText ?? "-";
    public string DownloadCountText => _state?.Downloads.Count.ToString("N0") ?? "0";
    public string UploadCountText => _state?.Uploads.Count.ToString("N0") ?? "0";
    public string ServerCountText => _state?.Servers.Count.ToString("N0") ?? "0";
    public string SearchCountText => _state?.Searches.Count.ToString("N0") ?? "0";
    public string CoreTimestampText => _state is null || _state.LastTimestamp <= 0 ? "-" : _state.LastTimestamp.ToString();

    public async Task<AjDirectoryListResult> LoadCoreDirectoryAsync(string? directory)
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        if (!IsConnected || client is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");

        string xml = await client.GetDirectoryXmlAsync(directory).ConfigureAwait(true);
        return AjXmlParser.ParseDirectoryList(xml);
    }
    public async Task ToggleConnectionAsync(string password)
    {
        ThrowIfDisposed();
        if (IsBusy)
            return;

        if (IsConnected)
            await DisconnectAsync().ConfigureAwait(true);
        else
            await ConnectAsync(password).ConfigureAwait(true);
    }

    public ServerReconnectEvaluation EvaluateServerLogin(AjServer server)
    {
        ThrowIfDisposed();
        AjState? state = _state;
        if (!IsConnected || state is null)
            return default;

        return ServerReconnectPolicy.EvaluateLogin(
            state.NetworkInfo.ConnectedWithServerId,
            server.Id,
            state.NetworkInfo.ConnectedSince,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public async Task LoginServerAsync(AjServer server)
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        if (!IsConnected || IsBusy || client is null)
            return;

        IsBusy = true;
        StatusText = $"Fordere Serverlogin an: {server.Name}";

        try
        {
            string response = await client.ServerLoginAsync(server.Id).ConfigureAwait(true);
            if (ServerReconnectPolicy.LooksLikeRestrictionResponse(response))
            {
                TimeSpan remaining = ServerReconnectPolicy.ExtractRestrictionRemaining(response);
                int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                StatusText = $"Core meldet Reconnect-Sperre: noch ca. {minutes} Minuten. Ziel: {server.Name}";
                return;
            }

            StatusText = $"Serverlogin angefordert: {server.Name}";
        }
        catch (Exception ex)
        {
            StatusText = "Serverlogin fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RemoveServerAsync(AjServer server)
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        if (!IsConnected || IsBusy || client is null)
            return;

        IsBusy = true;
        StatusText = $"Entferne Server: {server.Name}";

        try
        {
            await client.RemoveServerAsync(server.Id).ConfigureAwait(true);
            StatusText = $"Server entfernen angefordert: {server.Name}";
        }
        catch (Exception ex)
        {
            StatusText = "Server konnte nicht entfernt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PauseSelectedDownloadAsync()
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        if (download is null || client is null || !CanPauseSelectedDownload)
            return;

        IsBusy = true;
        StatusText = $"Pausiere Download: {download.DisplayFilename}";

        try
        {
            await client.PauseDownloadAsync(download.Id).ConfigureAwait(true);
            StatusText = $"Pause angefordert: {download.DisplayFilename}";
        }
        catch (Exception ex)
        {
            StatusText = "Download konnte nicht pausiert werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResumeSelectedDownloadAsync()
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        if (download is null || client is null || !CanResumeSelectedDownload)
            return;

        IsBusy = true;
        StatusText = $"Setze Download fort: {download.DisplayFilename}";

        try
        {
            await client.ResumeDownloadAsync(download.Id).ConfigureAwait(true);
            StatusText = $"Fortsetzen angefordert: {download.DisplayFilename}";
        }
        catch (Exception ex)
        {
            StatusText = "Download konnte nicht fortgesetzt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CancelSelectedDownloadAsync()
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        if (download is null || client is null || !CanCancelSelectedDownload)
            return;

        IsBusy = true;
        StatusText = $"Breche Download ab: {download.DisplayFilename}";

        try
        {
            await client.CancelDownloadAsync(download.Id).ConfigureAwait(true);
            StatusText = $"Abbruch angefordert: {download.DisplayFilename}";
        }
        catch (Exception ex)
        {
            StatusText = "Download konnte nicht abgebrochen werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RenameSelectedDownloadAsync(string newName)
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        string name = (newName ?? string.Empty).Trim();
        if (download is null || client is null || !CanRenameSelectedDownload || name.Length == 0)
            return;

        IsBusy = true;
        StatusText = $"Benenne Download um: {download.DisplayFilename}";

        try
        {
            await client.RenameDownloadAsync(download.Id, name).ConfigureAwait(true);
            StatusText = $"Umbenennen angefordert: {name}";
        }
        catch (Exception ex)
        {
            StatusText = "Download konnte nicht umbenannt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetSelectedDownloadTargetDirectoryAsync(string targetDirectory, bool existingCoreDirectory = false)
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (download is null || client is null || state is null || !CanSetTargetDirectorySelectedDownload)
            return;

        char separator = CoreTargetDirectory.DetermineSeparator(
            state.Settings.IncomingDirectory,
            download.TargetDirectory,
            targetDirectory);
        CoreTargetDirectoryNormalizationResult normalization = existingCoreDirectory
            ? CoreTargetDirectory.NormalizeExistingRelative(targetDirectory, separator)
            : CoreTargetDirectory.NormalizeRelative(targetDirectory, separator);
        if (!normalization.Success)
        {
            StatusText = "Zielverzeichnis ungültig: " + normalization.ErrorMessage;
            return;
        }

        string displayTarget = normalization.Value.Length == 0 ? "Incoming" : normalization.Value;
        bool needsDeepPreparation = !existingCoreDirectory
            && normalization.Value
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length > 1;

        IsBusy = true;

        try
        {
            if (needsDeepPreparation)
            {
                StatusText = $"Bereite lokale Zielordnerstruktur vor: {displayTarget}";
                HostIncomingDirectoryPreparationResult preparation = _incomingDirectoryPreparer.Prepare(
                    LocalIncomingMappingText,
                    normalization.Value);
                if (!preparation.Success)
                {
                    StatusText = "Zielverzeichnis konnte nicht vorbereitet werden: " + preparation.ErrorMessage;
                    return;
                }
            }

            StatusText = $"Setze Core-Zielverzeichnis: {displayTarget}";
            await client.SetTargetDirAsync(download.Id, normalization.Value).ConfigureAwait(true);
            StatusText = $"Zielverzeichnis angefordert: {displayTarget}";
        }
        catch (Exception ex)
        {
            StatusText = "Zielverzeichnis konnte nicht gesetzt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadSelectedSearchEntryAsync()
    {
        ThrowIfDisposed();
        AjSearchEntry? entry = SelectedSearchEntry;
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (entry is null || client is null || state is null || !CanDownloadSelectedSearchEntry)
            return;

        IsBusy = true;
        StatusText = $"Übernehme Suchtreffer: {entry.Filename}";

        try
        {
            ValidateSearchEntryForDownload(entry);
            string link = AjfspLinkBuilder.BuildFileLink(entry.Filename, entry.Checksum, entry.Size);
            AjCoreCompatibilityProfile profile = AjCoreCompatibilityProfile.FromCoreVersion(CoreVersion);
            AjProcessLinkResult result = await client.ProcessLinkDetailedAsync(link, profile, string.Empty).ConfigureAwait(true);

            if (result.IsRejected)
            {
                StatusText = $"Core hat den Suchtreffer abgelehnt: {result.StatusText}";
                return;
            }

            string checksum = entry.Checksum.Trim();
            StatusText = result.IsAlreadyDownloaded
                ? $"Download bereits bekannt; gleiche Downloadliste ab: {entry.Filename}"
                : $"Core hat Download angenommen; warte auf Downloadliste: {entry.Filename}";

            AjDownload? download = await WaitForDownloadByHashAsync(client, state, checksum).ConfigureAwait(true);
            if (download is null)
            {
                StatusText = result.IsAlreadyDownloaded
                    ? $"Download ist laut Core bereits vorhanden, wurde aber noch nicht in FirstLight gefunden: {entry.Filename}"
                    : $"Core hat den Download angenommen, aber er ist noch nicht in FirstLight sichtbar: {entry.Filename}";
                return;
            }

            SelectedDownload = download;
            StatusText = result.IsAlreadyDownloaded
                ? $"Bereits vorhandener Download gefunden: {download.DisplayFilename}"
                : $"Download übernommen: {download.DisplayFilename}";
        }
        catch (Exception ex)
        {
            StatusText = "Suchtreffer konnte nicht als Download übernommen werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartSearchAsync()
    {
        ThrowIfDisposed();
        if (!CanSearch || _client is null)
            return;

        string text = SearchText.Trim();
        IsBusy = true;
        StatusText = $"Sende Suche: {text}";

        try
        {
            await _client.SearchAsync(text).ConfigureAwait(true);
            SearchText = string.Empty;
            StatusText = $"Suchauftrag gesendet: {text}";
        }
        catch (Exception ex)
        {
            StatusText = "Suche fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await DisconnectInternalAsync(clearState: true).ConfigureAwait(true);
            StatusText = "Nicht verbunden";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectAsync(string password)
    {
        IsBusy = true;
        StatusText = "Prüfe Core-Endpunkt ...";

        try
        {
            await DisconnectInternalAsync(clearState: true).ConfigureAwait(true);

            CoreEndpoint endpoint = CoreEndpoint.Parse(EndpointText);
            _mappingStore.TrySave(endpoint.BaseUri.ToString(), LocalIncomingMappingText, out _);
            HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
            AppleJuiceCoreClient client = new(endpoint, password ?? string.Empty, httpClient);

            ConnectionTestResult connection = await client.TestConnectionAsync().ConfigureAwait(true);
            if (!connection.Success)
            {
                httpClient.Dispose();
                StatusText = connection.Message;
                return;
            }

            StatusText = "Core antwortet. Lade Laufzeitstatus ...";
            CoreBootstrapResult bootstrap = await new CoreRuntimeBootstrapper(client).LoadAsync().ConfigureAwait(true);

            _httpClient = httpClient;
            _client = client;
            _state = bootstrap.State;
            CoreVersion = string.IsNullOrWhiteSpace(bootstrap.CoreVersion) ? "unbekannt" : bootstrap.CoreVersion;

            _polling = new AjPollingService(client);
            _polling.ModifiedReceived += PollingOnModifiedReceived;
            _polling.ConnectionDegraded += PollingOnConnectionDegraded;
            _polling.ConnectionRestored += PollingOnConnectionRestored;
            _polling.ConnectionLost += PollingOnConnectionLost;
            _polling.FullResyncRequested += PollingOnFullResyncRequested;

            if (_state.Searches.Count > 0)
                SelectedSearch = _state.Searches[^1];

            IsConnected = true;
            RaiseStateProperties();

            await _polling.StartAsync(_state, intervalMs: 2000).ConfigureAwait(true);
            StatusText = $"Verbunden mit {endpoint.BaseUri}";
        }
        catch (Exception ex)
        {
            await DisconnectInternalAsync(clearState: true).ConfigureAwait(true);
            StatusText = "Verbindung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<AjDownload?> WaitForDownloadByHashAsync(
        AppleJuiceCoreClient client,
        AjState state,
        string checksum,
        int timeoutMs = 8000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await RefreshDownloadsFromCoreAsync(client, state).ConfigureAwait(true);

            AjDownload? found = state.Downloads
                .Where(download => string.Equals(download.Hash?.Trim(), checksum, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(download => download.Id)
                .FirstOrDefault();
            if (found is not null)
                return found;

            await Task.Delay(300).ConfigureAwait(true);
        }

        return null;
    }

    private async Task RefreshDownloadsFromCoreAsync(AppleJuiceCoreClient client, AjState state)
    {
        string xml = await client.GetModifiedXmlAsync(
            timestamp: 0,
            sessionId: state.SessionId,
            filter: "down;user;search;informations").ConfigureAwait(false);
        ModifiedParseResult result = AjXmlParser.ParseModified(xml);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_state, state) || !IsConnected)
                return;

            AjStateUpdater.Apply(state, result);
            if (result.CoreTimestamp > 0)
                state.LastTimestamp = result.CoreTimestamp;

            if (SelectedDownload is not null && state.Downloads.All(download => download.Id != SelectedDownload.Id))
                SelectedDownload = null;

            RaiseStateProperties();
        });
    }

    private async Task DisconnectInternalAsync(bool clearState)
    {
        AjPollingService? polling = _polling;
        _polling = null;

        if (polling is not null)
        {
            polling.ModifiedReceived -= PollingOnModifiedReceived;
            polling.ConnectionDegraded -= PollingOnConnectionDegraded;
            polling.ConnectionRestored -= PollingOnConnectionRestored;
            polling.ConnectionLost -= PollingOnConnectionLost;
            polling.FullResyncRequested -= PollingOnFullResyncRequested;
            await polling.StopAsync().ConfigureAwait(true);
        }

        _client = null;
        _httpClient?.Dispose();
        _httpClient = null;
        IsConnected = false;

        if (!clearState)
            return;

        _state = null;
        SelectedDownload = null;
        SelectedSearchEntry = null;
        SelectedSearch = null;
        CoreVersion = "-";
        RaiseStateProperties();
    }

    private void PollingOnModifiedReceived(ModifiedParseResult result, string rawXml)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_state is null)
                return;

            AjStateUpdater.Apply(_state, result);
            if (result.CoreTimestamp > 0)
                _state.LastTimestamp = result.CoreTimestamp;

            if (SelectedDownload is not null && !_state.Downloads.Any(download => download.Id == SelectedDownload.Id))
                SelectedDownload = null;

            if (SelectedSearch is null && _state.Searches.Count > 0)
                SelectedSearch = _state.Searches[^1];

            if (SelectedSearchEntry is not null
                && (SelectedSearch is null || SelectedSearch.Entries.All(entry => entry.Id != SelectedSearchEntry.Id)))
            {
                SelectedSearchEntry = null;
            }

            RaiseStateProperties();
        });
    }

    private void PollingOnConnectionDegraded(int errors, string message)
        => Dispatcher.UIThread.Post(() => StatusText = $"Core-Verbindung gestört ({errors}/6): {message}");

    private void PollingOnConnectionRestored(int errors)
        => Dispatcher.UIThread.Post(() => StatusText = $"Core-Verbindung wiederhergestellt nach {errors} Fehlversuch(en).");

    private void PollingOnConnectionLost(string message)
        => Dispatcher.UIThread.Post(() =>
        {
            IsConnected = false;
            StatusText = "Core-Verbindung verloren: " + message;
        });

    private void PollingOnFullResyncRequested(int missingTimestamps, string reason)
        => Dispatcher.UIThread.Post(() => StatusText = $"Core fordert Neuabgleich an ({missingTimestamps}): {reason}");

    private static bool IsValidSearchEntryForDownload(AjSearchEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Filename) || entry.Size <= 0)
            return false;

        string checksum = entry.Checksum?.Trim() ?? string.Empty;
        return checksum.Length == 32;
    }

    private static void ValidateSearchEntryForDownload(AjSearchEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Filename))
            throw new InvalidOperationException("Das Suchergebnis enthält keinen Dateinamen.");

        string checksum = entry.Checksum?.Trim() ?? string.Empty;
        if (checksum.Length != 32)
            throw new InvalidOperationException("Das Suchergebnis enthält keine gültige Checksum.");

        if (entry.Size <= 0)
            throw new InvalidOperationException("Das Suchergebnis enthält keine gültige Dateigröße.");
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(Downloads));
        OnPropertyChanged(nameof(Uploads));
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(Searches));
        OnPropertyChanged(nameof(SelectedDownloadText));
        OnPropertyChanged(nameof(CanPauseSelectedDownload));
        OnPropertyChanged(nameof(CanResumeSelectedDownload));
        OnPropertyChanged(nameof(CanCancelSelectedDownload));
        OnPropertyChanged(nameof(CanRenameSelectedDownload));
        OnPropertyChanged(nameof(CanSetTargetDirectorySelectedDownload));
        OnPropertyChanged(nameof(SelectedSearchEntries));
        OnPropertyChanged(nameof(SelectedSearchEntryText));
        OnPropertyChanged(nameof(CanDownloadSelectedSearchEntry));
        OnPropertyChanged(nameof(CoreNick));
        OnPropertyChanged(nameof(CoreIncomingDirectory));
        OnPropertyChanged(nameof(NetworkUsersText));
        OnPropertyChanged(nameof(NetworkFilesText));
        OnPropertyChanged(nameof(CreditsText));
        OnPropertyChanged(nameof(DownloadSpeedText));
        OnPropertyChanged(nameof(UploadSpeedText));
        OnPropertyChanged(nameof(DownloadCountText));
        OnPropertyChanged(nameof(UploadCountText));
        OnPropertyChanged(nameof(ServerCountText));
        OnPropertyChanged(nameof(SearchCountText));
        OnPropertyChanged(nameof(CoreTimestampText));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_polling is not null)
        {
            _polling.ModifiedReceived -= PollingOnModifiedReceived;
            _polling.ConnectionDegraded -= PollingOnConnectionDegraded;
            _polling.ConnectionRestored -= PollingOnConnectionRestored;
            _polling.ConnectionLost -= PollingOnConnectionLost;
            _polling.FullResyncRequested -= PollingOnFullResyncRequested;
            _polling.Stop();
            _polling = null;
        }

        _client = null;
        _httpClient?.Dispose();
        _httpClient = null;
    }
}
