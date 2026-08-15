using System.ComponentModel;
using System.Runtime.CompilerServices;
using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Protocol;
using AJCC.Core.Services;
using Avalonia.Threading;

namespace AJCC.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private HttpClient? _httpClient;
    private AppleJuiceCoreClient? _client;
    private AjPollingService? _polling;
    private AjState? _state;
    private string _endpointText = "http://127.0.0.1:8851/";
    private string _statusText = "Nicht verbunden";
    private string _coreVersion = "-";
    private string _searchText = string.Empty;
    private AjSearch? _selectedSearch;
    private bool _isBusy;
    private bool _isConnected;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string EndpointText
    {
        get => _endpointText;
        set => SetField(ref _endpointText, value ?? string.Empty);
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

    public AjSearch? SelectedSearch
    {
        get => _selectedSearch;
        set
        {
            if (!SetField(ref _selectedSearch, value))
                return;

            OnPropertyChanged(nameof(SelectedSearchEntries));
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
            OnPropertyChanged(nameof(CanSearch));
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetField(ref _isConnected, value))
                return;

            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ConnectionStateText));
            OnPropertyChanged(nameof(CanSearch));
        }
    }

    public bool CanToggleConnection => !IsBusy;
    public bool CanSearch => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(SearchText);
    public string ConnectButtonText => IsConnected ? "Trennen" : "Verbinden";
    public string ConnectionStateText => IsConnected ? "ONLINE" : "OFFLINE";

    public IEnumerable<AjDownload> Downloads => _state is null ? Array.Empty<AjDownload>() : _state.Downloads;
    public IEnumerable<AjUpload> Uploads => _state is null ? Array.Empty<AjUpload>() : _state.Uploads;
    public IEnumerable<AjServer> Servers => _state is null ? Array.Empty<AjServer>() : _state.Servers;
    public IEnumerable<AjSearch> Searches => _state is null ? Array.Empty<AjSearch>() : _state.Searches;
    public IEnumerable<AjSearchEntry> SelectedSearchEntries => SelectedSearch is null ? Array.Empty<AjSearchEntry>() : SelectedSearch.Entries;

    public string CoreNick => string.IsNullOrWhiteSpace(_state?.Settings.Nick) ? "-" : _state.Settings.Nick;
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

            if (SelectedSearch is null && _state.Searches.Count > 0)
                SelectedSearch = _state.Searches[^1];

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

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(Downloads));
        OnPropertyChanged(nameof(Uploads));
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(Searches));
        OnPropertyChanged(nameof(SelectedSearchEntries));
        OnPropertyChanged(nameof(CoreNick));
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
