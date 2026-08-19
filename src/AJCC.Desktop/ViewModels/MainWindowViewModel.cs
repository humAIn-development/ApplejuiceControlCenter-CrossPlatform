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
    private readonly ServerReconnectRestrictionStore _serverReconnectRestrictionStore = new();
    private readonly ServerReconnectRestrictionState _serverReconnectRestriction = new();
    private readonly DispatcherTimer _serverReconnectRestrictionTimer = new();
    private readonly DispatcherTimer _serverReachabilityTimer = new();
    private static readonly TimeSpan ServerReachabilityProbeInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ServerReachabilityProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ServerReachabilityProbeFreshness = TimeSpan.FromMinutes(3);
    private HttpClient? _httpClient;
    private AppleJuiceCoreClient? _client;
    private AjPollingService? _polling;
    private AjState? _state;
    private string _endpointText = "http://127.0.0.1:8851/";
    private string _localIncomingMappingText = string.Empty;
    private string _serverReconnectRestrictionEndpoint = string.Empty;
    private string _statusText = "Nicht verbunden";
    private string _coreVersion = "-";
    private string _searchText = string.Empty;
    private AjDownload? _selectedDownload;
    private AjSearch? _selectedSearch;
    private AjSearchEntry? _selectedSearchEntry;
    private bool _isBusy;
    private bool _isConnected;
    private bool _serverReconnectAutoReconnectAttemptRunning;
    private bool _isServerReachabilityProbeRunning;
    private int _serverReachabilityNextIndex;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel()
    {
        _localIncomingMappingText = _mappingStore.Get(_endpointText);
        _serverReconnectRestrictionTimer.Interval = TimeSpan.FromSeconds(1);
        _serverReconnectRestrictionTimer.Tick += ServerReconnectRestrictionTimerOnTick;
        _serverReconnectRestrictionTimer.Start();
        _serverReachabilityTimer.Interval = ServerReachabilityProbeInterval;
        _serverReachabilityTimer.Tick += ServerReachabilityTimerOnTick;
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
            OnPropertyChanged(nameof(HasServerReconnectRestriction));
            OnPropertyChanged(nameof(ServerReconnectRestrictionText));
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
    public bool HasServerReconnectRestriction => IsConnected && _serverReconnectRestriction.IsActive(DateTimeOffset.UtcNow);
    public string ServerReconnectRestrictionText
    {
        get
        {
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            if (!IsConnected || !_serverReconnectRestriction.IsActive(nowUtc))
                return string.Empty;

            string countdown = FormatServerReconnectCountdown(_serverReconnectRestriction.GetRemaining(nowUtc));
            return _serverReconnectRestriction.HasExactCountdown
                ? $"Reconnect-Sperre {countdown}"
                : $"Reconnect-Sperre bis zu {countdown}";
        }
    }
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

    public async Task LoginServerAsync(AjServer server, bool rapidSwitchConfirmed = false)
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        if (!IsConnected || IsBusy || client is null)
            return;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        RefreshServerReconnectRestrictionState(nowUtc);
        if (_serverReconnectRestriction.IsActive(nowUtc))
        {
            TimeSpan remaining = _serverReconnectRestriction.GetRemaining(nowUtc);
            int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            string remainingText = _serverReconnectRestriction.HasExactCountdown
                ? $"noch ca. {minutes} Minuten"
                : $"noch bis zu ca. {minutes} Minuten";
            StatusText = $"Serverlogin blockiert: lokale Reconnect-Sperre ({remainingText}). Ziel: {server.Name}";
            return;
        }

        if (rapidSwitchConfirmed)
        {
            _serverReconnectRestriction.Mark(
                ServerReconnectPolicy.RestrictionWindow,
                hasExactCountdown: true,
                targetServerId: server.Id,
                nowUtc: nowUtc);
            PersistServerReconnectRestrictionState();
            RaiseServerReconnectRestrictionProperties();
        }

        IsBusy = true;
        StatusText = $"Fordere Serverlogin an: {server.Name}";

        try
        {
            string response = await client.ServerLoginAsync(server.Id).ConfigureAwait(true);
            if (ServerReconnectPolicy.LooksLikeRestrictionResponse(response))
            {
                TimeSpan remaining = ServerReconnectPolicy.ExtractRestrictionRemaining(response);
                _serverReconnectRestriction.Mark(
                    remaining,
                    hasExactCountdown: false,
                    targetServerId: server.Id,
                    nowUtc: DateTimeOffset.UtcNow);
                PersistServerReconnectRestrictionState();
                RaiseServerReconnectRestrictionProperties();
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
            _serverReconnectRestrictionEndpoint = endpoint.BaseUri.ToString();
            RestoreServerReconnectRestrictionState(DateTimeOffset.UtcNow);
            RefreshServerReconnectRestrictionState(DateTimeOffset.UtcNow);
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
            StartServerReachabilityTimer();
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

        StopServerReachabilityTimer();
        _client = null;
        _httpClient?.Dispose();
        _httpClient = null;
        IsConnected = false;

        if (!clearState)
            return;

        _state = null;
        _serverReconnectRestriction.Clear();
        _serverReconnectRestrictionEndpoint = string.Empty;
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

            RefreshServerReconnectRestrictionState(DateTimeOffset.UtcNow);

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

    private void RestoreServerReconnectRestrictionState(DateTimeOffset nowUtc)
    {
        _serverReconnectRestriction.Clear();
        if (string.IsNullOrWhiteSpace(_serverReconnectRestrictionEndpoint))
            return;

        if (!_serverReconnectRestrictionStore.TryLoad(
                _serverReconnectRestrictionEndpoint,
                out ServerReconnectRestrictionSnapshot snapshot,
                out _))
        {
            return;
        }

        if (!ServerReconnectRestrictionSnapshots.Restore(_serverReconnectRestriction, snapshot, nowUtc)
            && snapshot.IsMarked)
        {
            PersistServerReconnectRestrictionState();
        }
    }

    private void PersistServerReconnectRestrictionState()
    {
        if (string.IsNullOrWhiteSpace(_serverReconnectRestrictionEndpoint))
            return;

        _serverReconnectRestrictionStore.TrySave(
            _serverReconnectRestrictionEndpoint,
            ServerReconnectRestrictionSnapshots.Capture(_serverReconnectRestriction),
            out _);
    }

    private void RefreshServerReconnectRestrictionState(DateTimeOffset nowUtc)
    {
        AjState? state = _state;
        if (state is not null
            && _serverReconnectRestriction.ClearIfConnected(state.NetworkInfo.ConnectedWithServerId))
        {
            PersistServerReconnectRestrictionState();
            return;
        }

        if (!_serverReconnectRestriction.IsMarked || _serverReconnectRestriction.IsActive(nowUtc))
            return;

        long expiredTargetServerId = _serverReconnectRestriction.TargetServerId;
        bool shouldAutoReconnect = state?.Settings.AutoConnect == true && expiredTargetServerId > 0;

        if (!_serverReconnectRestriction.ClearIfExpired(nowUtc))
            return;

        PersistServerReconnectRestrictionState();

        if (shouldAutoReconnect)
            StartServerReconnectAfterRestriction(expiredTargetServerId);
    }

    private void StartServerReconnectAfterRestriction(long targetServerId)
    {
        if (_serverReconnectAutoReconnectAttemptRunning || !IsConnected || _state is null)
            return;

        AjServer? targetServer = _state.Servers.FirstOrDefault(server => server.Id == targetServerId);
        if (targetServer is null)
        {
            StatusText = $"Autoverbindung übersprungen: Zielserver ID {targetServerId} ist nicht mehr vorhanden.";
            return;
        }

        _ = AutoReconnectAfterRestrictionAsync(targetServer);
    }

    private async Task AutoReconnectAfterRestrictionAsync(AjServer targetServer)
    {
        if (_serverReconnectAutoReconnectAttemptRunning || !IsConnected || _client is null)
            return;

        _serverReconnectAutoReconnectAttemptRunning = true;
        StatusText = $"Autoverbindung nach Reconnect-Sperre: {targetServer.Name}";

        try
        {
            string response = await _client.ServerLoginAsync(targetServer.Id).ConfigureAwait(true);
            if (ServerReconnectPolicy.LooksLikeRestrictionResponse(response))
            {
                TimeSpan remaining = ServerReconnectPolicy.ExtractRestrictionRemaining(response);
                _serverReconnectRestriction.Mark(
                    remaining,
                    hasExactCountdown: false,
                    targetServerId: targetServer.Id,
                    nowUtc: DateTimeOffset.UtcNow);
                PersistServerReconnectRestrictionState();
                RaiseServerReconnectRestrictionProperties();

                int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                StatusText = $"Core meldet nach Autoverbindung erneut Reconnect-Sperre: noch ca. {minutes} Minuten. Ziel: {targetServer.Name}";
                return;
            }

            StatusText = $"Autoverbindung angefordert: {targetServer.Name}";
        }
        catch (Exception ex)
        {
            StatusText = "Autoverbindung nach Reconnect-Sperre fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _serverReconnectAutoReconnectAttemptRunning = false;
        }
    }

    private void ServerReconnectRestrictionTimerOnTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        RefreshServerReconnectRestrictionState(DateTimeOffset.UtcNow);
        RaiseServerReconnectRestrictionProperties();
    }

    private void RaiseServerReconnectRestrictionProperties()
    {
        OnPropertyChanged(nameof(HasServerReconnectRestriction));
        OnPropertyChanged(nameof(ServerReconnectRestrictionText));
    }

    private static string FormatServerReconnectCountdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";

        return $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private void ServerReachabilityTimerOnTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        _ = ProbeNextServerReachabilityAsync();
    }

    private void StartServerReachabilityTimer()
    {
        if (_disposed || !IsConnected)
            return;

        _serverReachabilityTimer.Start();
        _ = ProbeNextServerReachabilityAsync();
    }

    private void StopServerReachabilityTimer()
    {
        _serverReachabilityTimer.Stop();
        _serverReachabilityNextIndex = 0;
    }

    private async Task ProbeNextServerReachabilityAsync()
    {
        AjState? state = _state;
        if (_isServerReachabilityProbeRunning || !IsConnected || state is null || state.Servers.Count == 0)
            return;

        long connectedId = state.NetworkInfo.ConnectedWithServerId;
        List<AjServer> servers = state.Servers.ToList();
        AjServer? server = null;

        for (int i = 0; i < servers.Count; i++)
        {
            int index = (_serverReachabilityNextIndex + i) % servers.Count;
            AjServer candidate = servers[index];
            if (candidate.Id == connectedId)
                continue;
            if (string.IsNullOrWhiteSpace(candidate.Host) || candidate.Port <= 0)
                continue;

            server = candidate;
            _serverReachabilityNextIndex = (index + 1) % servers.Count;
            break;
        }

        if (server is null)
            return;

        _isServerReachabilityProbeRunning = true;
        server.ReachabilityProbeRunning = true;
        UpdateServerCoreStates();

        bool reachable = false;
        try
        {
            reachable = await TcpReachabilityProbe.TestAsync(
                server.Host,
                server.Port,
                ServerReachabilityProbeTimeout).ConfigureAwait(true);
        }
        finally
        {
            bool currentState = !_disposed && ReferenceEquals(_state, state) && IsConnected;
            if (currentState)
            {
                server.ReachabilityProbeSucceeded = reachable;
                server.ReachabilityProbeUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            server.ReachabilityProbeRunning = false;
            _isServerReachabilityProbeRunning = false;

            if (currentState)
                UpdateServerCoreStates();
        }
    }

    private void UpdateServerCoreStates()
    {
        AjState? state = _state;
        if (state is null)
            return;

        long connectedId = IsConnected ? state.NetworkInfo.ConnectedWithServerId : 0;
        long tryingId = IsConnected ? state.NetworkInfo.TryConnectToServer : 0;
        long connectedSince = IsConnected ? state.NetworkInfo.ConnectedSince : 0;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (AjServer server in state.Servers)
        {
            bool probeFresh = server.ReachabilityProbeUtc > 0
                && Math.Abs(now - server.ReachabilityProbeUtc) <= ServerReachabilityProbeFreshness.TotalMilliseconds;

            if (connectedId > 0 && server.Id == connectedId)
            {
                server.ServerStatusKind = "connected";
                server.ServerStatusText = "Verbunden";
                server.ConnectionDurationText = connectedSince > 0
                    ? FormatServerConnectionElapsed(now - connectedSince)
                    : "verbunden";
            }
            else if (tryingId > 0 && server.Id == tryingId)
            {
                server.ServerStatusKind = "connecting";
                server.ServerStatusText = "Verbindungsversuch";
                server.ConnectionDurationText = "-";
            }
            else if (server.ReachabilityProbeRunning)
            {
                server.ServerStatusKind = "checking";
                server.ServerStatusText = "Prüfung";
                server.ConnectionDurationText = "-";
            }
            else if (probeFresh && server.ReachabilityProbeSucceeded == true)
            {
                server.ServerStatusKind = "reachable";
                server.ServerStatusText = "erreichbar";
                server.ConnectionDurationText = "-";
            }
            else if (probeFresh && server.ReachabilityProbeSucceeded == false)
            {
                server.ServerStatusKind = "unreachable";
                server.ServerStatusText = "nicht erreichbar";
                server.ConnectionDurationText = "-";
            }
            else
            {
                server.ServerStatusKind = "unknown";
                server.ServerStatusText = "ungeprüft";
                server.ConnectionDurationText = "-";
            }
        }
    }

    private static string FormatServerConnectionElapsed(long milliseconds)
    {
        if (milliseconds <= 0)
            return "0m";

        TimeSpan elapsed = TimeSpan.FromMilliseconds(milliseconds);
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m";
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        return $"{Math.Max(0, (int)elapsed.TotalMinutes)}m {elapsed.Seconds}s";
    }

    private void PollingOnConnectionDegraded(int errors, string message)
        => Dispatcher.UIThread.Post(() => StatusText = $"Core-Verbindung gestört ({errors}/6): {message}");

    private void PollingOnConnectionRestored(int errors)
        => Dispatcher.UIThread.Post(() => StatusText = $"Core-Verbindung wiederhergestellt nach {errors} Fehlversuch(en).");

    private void PollingOnConnectionLost(string message)
        => Dispatcher.UIThread.Post(() =>
        {
            StopServerReachabilityTimer();
            IsConnected = false;
            UpdateServerCoreStates();
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
        UpdateServerCoreStates();
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
        RaiseServerReconnectRestrictionProperties();
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
        _serverReconnectRestrictionTimer.Stop();
        _serverReconnectRestrictionTimer.Tick -= ServerReconnectRestrictionTimerOnTick;
        _serverReachabilityTimer.Stop();
        _serverReachabilityTimer.Tick -= ServerReachabilityTimerOnTick;
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
