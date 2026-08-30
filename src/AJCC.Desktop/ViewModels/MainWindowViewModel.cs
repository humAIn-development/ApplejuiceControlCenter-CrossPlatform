using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
    private readonly DispatcherTimer _externalCorePortTestTimer = new();
    private static readonly TimeSpan ServerReachabilityProbeInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ServerReachabilityProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ServerReachabilityProbeFreshness = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ExternalCorePortTestInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExternalCorePortTestTimeout = TimeSpan.FromMilliseconds(2500);
    private const int UploadSpeedHistoryLength = 48;
    private static readonly TimeSpan UploadSpeedHistoryMinimumSampleDistance = TimeSpan.FromMilliseconds(1500);
    private const int SearchAdoptionExistingFallbackPollCount = 2;
    private const int SearchAdoptionMaximumPollCount = 5;
    private readonly Dictionary<long, Queue<long>> _uploadSpeedHistory = new();
    private readonly Dictionary<long, UploadSpeedSampleSignature> _lastUploadSpeedSamples = new();
    private readonly Dictionary<long, DateTime> _lastUploadSpeedSampleTimesUtc = new();
    private HttpClient? _httpClient;
    private AppleJuiceCoreClient? _client;
    private AjPollingService? _polling;
    private AjState? _state;
    private IReadOnlyList<AjShareFile>? _visibleSharesOverride;
    private string _endpointText = "http://192.168.178.25:9851/";
    private string _localIncomingMappingText = string.Empty;
    private string _serverReconnectRestrictionEndpoint = string.Empty;
    private string _statusText = "Nicht verbunden";
    private string _coreVersion = "-";
    private string _searchText = string.Empty;
    private string _pendingSearchText = string.Empty;
    private long _pendingSearchPreviousMaxId;
    private int _pendingSearchPollCount;
    private AjDownload? _selectedDownload;
    private AjSearch? _selectedSearch;
    private AjSearchEntry? _selectedSearchEntry;
    private bool _isBusy;
    private bool _isConnected;
    private bool _serverReconnectAutoReconnectAttemptRunning;
    private bool _isServerReachabilityProbeRunning;
    private int _serverReachabilityNextIndex;
    private bool _externalCorePortTestRunning;
    private bool _disposed;

    private readonly record struct UploadSpeedSampleSignature(
        int Status,
        long Speed,
        long UploadFrom,
        long UploadTo,
        long ActualUploadPosition);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? CoreConnectionLost;

    public MainWindowViewModel()
    {
        _localIncomingMappingText = _mappingStore.Get(_endpointText);
        _serverReconnectRestrictionTimer.Interval = TimeSpan.FromSeconds(1);
        _serverReconnectRestrictionTimer.Tick += ServerReconnectRestrictionTimerOnTick;
        _serverReconnectRestrictionTimer.Start();
        _serverReachabilityTimer.Interval = ServerReachabilityProbeInterval;
        _serverReachabilityTimer.Tick += ServerReachabilityTimerOnTick;
        _externalCorePortTestTimer.Interval = ExternalCorePortTestInterval;
        _externalCorePortTestTimer.Tick += ExternalCorePortTestTimerOnTick;
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

    public void SetStatusMessage(string message)
        => StatusText = message ?? string.Empty;

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
            OnPropertyChanged(nameof(SelectedDownloadSources));
            OnPropertyChanged(nameof(SelectedDownloadSourcesText));
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
    public bool CanCleanDownloadList => IsConnected && !IsBusy && _state is not null && _state.Downloads.Any(DownloadActionSemantics.IsTerminal);
    public bool CanSetPowerDownloadSelectedDownload => IsConnected && !IsBusy && DownloadActionSemantics.CanChangeMetadata(SelectedDownload);
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
    public IEnumerable<AjUserSource> SelectedDownloadSources
        => _state is null || SelectedDownload is null
            ? Array.Empty<AjUserSource>()
            : _state.Users
                .Where(user => user.DownloadId == SelectedDownload.Id)
                .Where(IsVisibleDownloadSource);

    public string SelectedDownloadSourcesText
    {
        get
        {
            AjState? state = _state;
            AjDownload? download = SelectedDownload;
            if (state is null || download is null)
                return "keine Datei ausgewählt";

            int visible = state.Users.Count(user =>
                user.DownloadId == download.Id && IsVisibleDownloadSource(user));
            int active = state.Users.Count(user =>
                user.DownloadId == download.Id
                && IsVisibleDownloadSource(user)
                && IsActiveConnectedSource(user));
            return $"{visible:N0} sichtbar · {active:N0} aktiv";
        }
    }

    public IEnumerable<AjUpload> Uploads => _state is null ? Array.Empty<AjUpload>() : _state.Uploads;
    public IEnumerable<AjUpload> ActiveUploads => _state is null ? Array.Empty<AjUpload>() : _state.Uploads.Where(static upload => upload.IsActiveTransfer);
    public IEnumerable<AjUpload> InactiveUploads => _state is null ? Array.Empty<AjUpload>() : _state.Uploads.Where(static upload => !upload.IsActiveTransfer);
    public IEnumerable<AjServer> Servers => _state is null ? Array.Empty<AjServer>() : _state.Servers;
    public IEnumerable<AjSearch> Searches => _state is null ? Array.Empty<AjSearch>() : _state.Searches;
    public IEnumerable<AjSearchEntry> SelectedSearchEntries => SelectedSearch is null
        ? Array.Empty<AjSearchEntry>()
        : SelectedSearch.Entries
            .OrderByDescending(entry => entry.FilenameUsers)
            .ThenBy(entry => entry.Filename, NaturalStringComparer.Instance);
    public IEnumerable<AjShareFile> Shares => _state is null ? Array.Empty<AjShareFile>() : _state.Shares;
    public IEnumerable<AjShareFile> VisibleShares => _visibleSharesOverride ?? Shares;
    public IReadOnlyList<AjShareDirectory> ConfiguredShareDirectories
        => _state is null ? Array.Empty<AjShareDirectory>() : _state.Settings.SharedDirectories.ToList();
    public string ShareCountText => _state is null ? "0 Dateien" : $"{_state.Shares.Count:N0} Dateien";
    public string ShareSizeText => _state is null
        ? DisplayFormatHelper.Bytes(0)
        : DisplayFormatHelper.Bytes(_state.Shares.Sum(share => Math.Max(0L, share.Size)));

    public string CoreNick => string.IsNullOrWhiteSpace(_state?.Settings.Nick) ? "-" : _state.Settings.Nick;
    public string CoreNickValue => _state?.Settings.Nick?.Trim() ?? string.Empty;
    public string CoreIncomingDirectory => string.IsNullOrWhiteSpace(_state?.Settings.IncomingDirectory) ? "-" : _state.Settings.IncomingDirectory;
    public string CoreTemporaryDirectory => string.IsNullOrWhiteSpace(_state?.Settings.TemporaryDirectory) ? "-" : _state.Settings.TemporaryDirectory;
    public string CorePortText => _state is null || _state.Settings.Port <= 0 ? "-" : _state.Settings.Port.ToString();
    public int CorePortValue => (_state?.Settings.Port ?? 0) > 0 ? _state!.Settings.Port : 8000;
    public string CoreXmlPortText => _state is null || _state.Settings.XmlPort <= 0 ? "-" : _state.Settings.XmlPort.ToString();
    public int CoreXmlPortValue
    {
        get
        {
            int configured = _state?.Settings.XmlPort ?? 0;
            if (configured > 0)
                return configured;

            return Uri.TryCreate(EndpointText, UriKind.Absolute, out Uri? endpoint)
                && endpoint.Port is >= 1 and <= 65535
                ? endpoint.Port
                : 9851;
        }
    }
    public int CoreMaxConnections => Math.Max(0, _state?.Settings.MaxConnections ?? 0);
    public int CoreMaxSourcesPerFile => Math.Max(0, _state?.Settings.MaxSourcesPerFile ?? 0);
    public int CoreMaxNewConnectionsPerTurn => (_state?.Settings.MaxNewConnectionsPerTurn ?? 0) > 0
        ? _state!.Settings.MaxNewConnectionsPerTurn
        : 50;
    public long CoreMaxDownloadKb => Math.Max(0L, (_state?.Settings.MaxDownload ?? 0L) / 1024L);
    public long CoreMaxUploadKb => (_state?.Settings.MaxUpload ?? 0L) > 0
        ? _state!.Settings.MaxUpload / 1024L
        : 5000L;
    public int CoreSpeedPerSlot
    {
        get
        {
            (int minimum, int maximum) = CalculateLegacySpeedPerSlotRange(CoreMaxUploadKb);
            int configured = _state?.Settings.SpeedPerSlot ?? 0;
            int requested = configured > 0 ? configured : 165;
            return Math.Max(minimum, Math.Min(maximum, requested));
        }
    }
    public bool CoreAutoConnect => _state?.Settings.AutoConnect ?? false;
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

    public string BuildSelectedDownloadAjfspLinkWithSource()
    {
        AjDownload? download = SelectedDownload;
        if (download is null || string.IsNullOrWhiteSpace(download.Hash) || download.Size <= 0)
            return string.Empty;

        AjState? state = _state;
        string sourceText = state?.Settings.Nick?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceText) && state is not null)
        {
            sourceText = state.Users
                .Where(user => user.DownloadId == download.Id)
                .OrderByDescending(IsActiveConnectedSource)
                .ThenBy(user => user.QueueSortKey)
                .Select(user => user.Nickname?.Trim() ?? string.Empty)
                .FirstOrDefault(nickname => !string.IsNullOrWhiteSpace(nickname)) ?? string.Empty;
        }

        return AjfspLinkBuilder.BuildFileLink(download.DisplayFilename, download.Hash, download.Size, sourceText);
    }

    public string BuildShareAjfspLink(AjShareFile? share, bool includeOwnSource)
    {
        if (share is null
            || string.IsNullOrWhiteSpace(share.DisplayFilename)
            || string.IsNullOrWhiteSpace(share.Checksum)
            || share.Size <= 0)
        {
            return string.Empty;
        }

        string sourceText = includeOwnSource
            ? _state?.Settings.Nick?.Trim() ?? string.Empty
            : string.Empty;
        return string.IsNullOrWhiteSpace(sourceText)
            ? AjfspLinkBuilder.BuildFileLink(share.DisplayFilename, share.Checksum, share.Size)
            : AjfspLinkBuilder.BuildFileLink(share.DisplayFilename, share.Checksum, share.Size, sourceText);
    }

    public async Task<AjDirectoryListResult> LoadCoreDirectoryAsync(string? directory)
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        if (!IsConnected || client is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");

        string xml = await client.GetDirectoryXmlAsync(directory).ConfigureAwait(true);
        return AjXmlParser.ParseDirectoryList(xml);
    }

    public async Task<string> ApplyCoreIncomingDirectoryAsync(string incomingDirectory)
    {
        ThrowIfDisposed();

        string requested = (incomingDirectory ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(requested) || requested.Any(char.IsControl))
            throw new ArgumentException("Core-Incoming muss ein nichtleerer, strukturell gültiger Core-Pfad sein.", nameof(incomingDirectory));

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { IncomingDirectory = requested });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreIncomingDirectory));

            string effective = state.Settings.IncomingDirectory?.Trim() ?? string.Empty;
            if (!string.Equals(effective, requested, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Core meldet nach der Übertragung IncomingDirectory='{effective}' statt '{requested}'.");

            StatusText = $"Core-Incoming vom Core bestätigt: {effective}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Core-Incoming konnte nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string> ApplyCoreTemporaryDirectoryAsync(string temporaryDirectory)
    {
        ThrowIfDisposed();

        string requested = (temporaryDirectory ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(requested) || requested.Any(char.IsControl))
            throw new ArgumentException("Core-Temp muss ein nichtleerer, strukturell gültiger Core-Pfad sein.", nameof(temporaryDirectory));

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");
        if (state.Downloads.Count > 0)
            throw new InvalidOperationException("Core-Temp darf nur geändert werden, wenn die Downloadliste komplett leer ist.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { TemporaryDirectory = requested });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreTemporaryDirectory));

            string effective = state.Settings.TemporaryDirectory?.Trim() ?? string.Empty;
            if (!string.Equals(effective, requested, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Core meldet nach der Übertragung TemporaryDirectory='{effective}' statt '{requested}'.");

            StatusText = $"Core-Temp vom Core bestätigt: {effective}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Core-Temp konnte nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> ApplyMaxConnectionsAsync(int maxConnections)
    {
        ThrowIfDisposed();
        if (maxConnections < 0)
            throw new ArgumentOutOfRangeException(nameof(maxConnections), "Maximale Verbindungen dürfen nicht negativ sein.");

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        StatusText = $"Übertrage maximale Verbindungen ({maxConnections:N0}) an den Core ...";

        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { MaxConnections = maxConnections });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);

            OnPropertyChanged(nameof(CoreNick));
            OnPropertyChanged(nameof(CoreIncomingDirectory));
            OnPropertyChanged(nameof(CoreTemporaryDirectory));
            OnPropertyChanged(nameof(CorePortText));
            OnPropertyChanged(nameof(CoreXmlPortText));
            OnPropertyChanged(nameof(CoreMaxConnections));
            OnPropertyChanged(nameof(ConfiguredShareDirectories));

            int effective = Math.Max(0, state.Settings.MaxConnections);
            if (effective != maxConnections)
            {
                StatusText = $"Core meldet nach der Übertragung {effective:N0} statt {maxConnections:N0} maximale Verbindungen.";
                throw new InvalidOperationException(StatusText);
            }

            StatusText = $"Maximale Verbindungen vom Core bestätigt: {effective:N0}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Maximale Verbindungen konnten nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> ApplyMaxSourcesPerFileAsync(int maxSourcesPerFile)
    {
        ThrowIfDisposed();
        if (maxSourcesPerFile < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSourcesPerFile), "Maximale Quellen pro Datei dürfen nicht negativ sein.");

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        StatusText = $"Übertrage maximale Quellen pro Datei ({maxSourcesPerFile:N0}) an den Core ...";

        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { MaxSourcesPerFile = maxSourcesPerFile });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);

            OnPropertyChanged(nameof(CoreNick));
            OnPropertyChanged(nameof(CoreIncomingDirectory));
            OnPropertyChanged(nameof(CoreTemporaryDirectory));
            OnPropertyChanged(nameof(CorePortText));
            OnPropertyChanged(nameof(CoreXmlPortText));
            OnPropertyChanged(nameof(CoreMaxConnections));
            OnPropertyChanged(nameof(CoreMaxSourcesPerFile));
            OnPropertyChanged(nameof(ConfiguredShareDirectories));

            int effective = Math.Max(0, state.Settings.MaxSourcesPerFile);
            if (effective != maxSourcesPerFile)
            {
                StatusText = $"Core meldet nach der Übertragung {effective:N0} statt {maxSourcesPerFile:N0} maximale Quellen pro Datei.";
                throw new InvalidOperationException(StatusText);
            }

            StatusText = $"Maximale Quellen pro Datei vom Core bestätigt: {effective:N0}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Maximale Quellen pro Datei konnten nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }


    public async Task<int> ApplyMaxNewConnectionsPerTurnAsync(int maxNewConnectionsPerTurn)
    {
        ThrowIfDisposed();
        if (maxNewConnectionsPerTurn is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(maxNewConnectionsPerTurn), "Maximale neue Verbindungen pro 10 Sekunden müssen zwischen 1 und 200 liegen.");

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { MaxNewConnectionsPerTurn = maxNewConnectionsPerTurn });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreMaxNewConnectionsPerTurn));

            int effective = state.Settings.MaxNewConnectionsPerTurn;
            if (effective != maxNewConnectionsPerTurn)
                throw new InvalidOperationException($"Core meldet nach der Übertragung {effective:N0} statt {maxNewConnectionsPerTurn:N0} maximale neue Verbindungen pro 10 Sekunden.");

            StatusText = $"Maximale neue Verbindungen pro 10 Sekunden vom Core bestätigt: {effective:N0}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Maximale neue Verbindungen pro 10 Sekunden konnten nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }


    public async Task<bool> ApplyAutoConnectAsync(bool autoConnect)
    {
        ThrowIfDisposed();

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { AutoConnect = autoConnect });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreAutoConnect));

            bool effective = state.Settings.AutoConnect;
            if (effective != autoConnect)
                throw new InvalidOperationException($"Core meldet nach der Übertragung AutoConnect={effective} statt AutoConnect={autoConnect}.");

            StatusText = $"Automatisch verbinden vom Core bestätigt: {(effective ? "ein" : "aus")}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Automatisch verbinden konnte nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }


    public async Task<string> ApplyCoreNicknameAsync(string nickname)
    {
        ThrowIfDisposed();

        string requested = (nickname ?? string.Empty).Trim();
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { Nick = requested });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreNick));
            OnPropertyChanged(nameof(CoreNickValue));

            string effective = state.Settings.Nick?.Trim() ?? string.Empty;
            if (!string.Equals(effective, requested, StringComparison.Ordinal))
                throw new InvalidOperationException($"Core meldet nach der Übertragung den Benutzernamen '{effective}' statt '{requested}'.");

            StatusText = string.IsNullOrEmpty(effective)
                ? "Leerer Benutzername vom Core bestätigt."
                : $"Benutzername vom Core bestätigt: {effective}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Benutzername konnte nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }


    public async Task<int> ApplyCorePortAsync(int corePort)
    {
        ThrowIfDisposed();
        if (corePort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(corePort), "Core-Port muss zwischen 1 und 65535 liegen.");

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { Port = corePort });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CorePortText));
            OnPropertyChanged(nameof(CorePortValue));

            int effective = state.Settings.Port;
            if (effective != corePort)
                throw new InvalidOperationException($"Core meldet nach der Übertragung Port={effective} statt Port={corePort}.");

            StatusText = $"Core-Port vom Core bestätigt: {effective}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "Core-Port konnte nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }



    public async Task<int> ApplyCoreXmlPortAsync(int xmlPort)
    {
        ThrowIfDisposed();
        if (xmlPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(xmlPort), "XML-Port muss zwischen 1 und 65535 liegen.");

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { XmlPort = xmlPort });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreXmlPortText));
            OnPropertyChanged(nameof(CoreXmlPortValue));

            int effective = state.Settings.XmlPort;
            if (effective != xmlPort)
                throw new InvalidOperationException($"Core meldet nach der Übertragung XMLPort={effective} statt XMLPort={xmlPort}.");

            StatusText = $"XML-Port vom Core bestätigt: {effective}.";
            return effective;
        }
        catch (Exception ex)
        {
            StatusText = "XML-Port konnte nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<long> ApplyMaxDownloadAsync(long maxDownloadKb)
    {
        ThrowIfDisposed();
        if (maxDownloadKb is < 0 or > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(maxDownloadKb), "Max. Downloadgeschwindigkeit muss zwischen 0 und 100000000 kb/s liegen.");

        long coreValue = checked(maxDownloadKb * 1024L);
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides { MaxDownload = coreValue });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreMaxDownloadKb));

            long effectiveCoreValue = Math.Max(0L, state.Settings.MaxDownload);
            if (effectiveCoreValue != coreValue)
                throw new InvalidOperationException($"Core meldet nach der Übertragung MaxDownload={effectiveCoreValue} statt MaxDownload={coreValue}.");

            long effectiveKb = effectiveCoreValue / 1024L;
            StatusText = $"Max. Download vom Core bestätigt: {effectiveKb} kb/s.";
            return effectiveKb;
        }
        catch (Exception ex)
        {
            StatusText = "Max. Download konnte nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<(long MaxUploadKb, int SpeedPerSlot)> ApplyUploadLimitsAsync(
        long maxUploadKb,
        int requestedSpeedPerSlot)
    {
        ThrowIfDisposed();
        if (maxUploadKb is < 0 or > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(maxUploadKb), "Max. Uploadgeschwindigkeit muss zwischen 0 und 100000000 kb/s liegen.");

        (int minimum, int maximum) = CalculateLegacySpeedPerSlotRange(maxUploadKb);
        int speedPerSlot = Math.Max(minimum, Math.Min(maximum, requestedSpeedPerSlot));
        long coreValue = checked(maxUploadKb * 1024L);

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(
                state.Settings,
                new AjSettingsOverrides
                {
                    MaxUpload = coreValue,
                    SpeedPerSlot = speedPerSlot
                });
            await client.SetSettingsAsync(parameters).ConfigureAwait(true);

            string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
            state.Settings = AjXmlParser.ParseSettings(settingsXml);
            OnPropertyChanged(nameof(CoreMaxUploadKb));
            OnPropertyChanged(nameof(CoreSpeedPerSlot));

            long effectiveCoreValue = Math.Max(0L, state.Settings.MaxUpload);
            if (effectiveCoreValue != coreValue)
                throw new InvalidOperationException($"Core meldet nach der Übertragung MaxUpload={effectiveCoreValue} statt MaxUpload={coreValue}.");

            int effectiveSpeedPerSlot = state.Settings.SpeedPerSlot;
            if (effectiveSpeedPerSlot != speedPerSlot)
                throw new InvalidOperationException($"Core meldet nach der Übertragung SpeedPerSlot={effectiveSpeedPerSlot} statt SpeedPerSlot={speedPerSlot}.");

            long displayMaxUploadKb = effectiveCoreValue > 0
                ? effectiveCoreValue / 1024L
                : 5000L;
            StatusText = effectiveCoreValue > 0
                ? $"Upload-Limits vom Core bestätigt: Max. Upload {displayMaxUploadKb} kb/s, {effectiveSpeedPerSlot} kb/s pro Slot."
                : $"Upload-Limits vom Core bestätigt: MaxUpload=0, Anzeige-Fallback 5000 kb/s, {effectiveSpeedPerSlot} kb/s pro Slot.";
            return (displayMaxUploadKb, effectiveSpeedPerSlot);
        }
        catch (Exception ex)
        {
            StatusText = "Upload-Limits konnten nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static (int Minimum, int Maximum) CalculateLegacySpeedPerSlotRange(long maxUploadKb)
    {
        if (maxUploadKb <= 0)
            return (1, 500);

        int minimum = Math.Max(1, (int)Math.Pow(maxUploadKb, 0.2));
        int maximum = Math.Max(minimum, (int)Math.Pow(maxUploadKb, 0.6));
        return (minimum, maximum);
    }

    public async Task<IReadOnlyList<AjShareDirectory>> TransferShareDirectoriesAsync(
        IReadOnlyList<AjShareDirectory> directories)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(directories);

        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || client is null || state is null)
            throw new InvalidOperationException("Keine aktive Core-Verbindung.");
        if (IsBusy)
            throw new InvalidOperationException("AJCC verarbeitet gerade eine andere Core-Aktion.");

        List<AjShareDirectory> normalizedDirectories = ShareDirectoryDraftSemantics
            .Normalize(directories)
            .Where(directory => !string.IsNullOrWhiteSpace(directory.Name))
            .Select(directory => new AjShareDirectory
            {
                Name = directory.Name,
                ShareMode = directory.ShareMode
            })
            .ToList();

        IsBusy = true;
        StatusText = $"Übertrage {normalizedDirectories.Count:N0} Share-Verzeichnis(se) an den Core ...";

        try
        {
            int previousShareCount = state.Settings.SharedDirectories.Count;
            await client.SetShareDirectoriesAsync(normalizedDirectories, previousShareCount).ConfigureAwait(true);

            IReadOnlyList<AjShareDirectory> effectiveDirectories;
            try
            {
                string settingsXml = await client.GetSettingsXmlAsync().ConfigureAwait(true);
                state.Settings = AjXmlParser.ParseSettings(settingsXml);
                effectiveDirectories = state.Settings.SharedDirectories
                    .Select(directory => new AjShareDirectory
                    {
                        Name = directory.Name,
                        ShareMode = directory.ShareMode
                    })
                    .ToList();
                StatusText = $"Share-Verzeichnisse übertragen: {effectiveDirectories.Count:N0} Eintrag/Einträge vom Core zurückgelesen.";
            }
            catch (Exception refreshException)
            {
                state.Settings.SharedDirectories.Clear();
                foreach (AjShareDirectory directory in normalizedDirectories)
                {
                    state.Settings.SharedDirectories.Add(new AjShareDirectory
                    {
                        Name = directory.Name,
                        ShareMode = directory.ShareMode
                    });
                }

                effectiveDirectories = normalizedDirectories
                    .Select(directory => new AjShareDirectory
                    {
                        Name = directory.Name,
                        ShareMode = directory.ShareMode
                    })
                    .ToList();
                StatusText = "Share-Verzeichnisse wurden übertragen, konnten danach aber nicht erneut vom Core gelesen werden: "
                    + refreshException.Message;
            }

            if (!ShareDirectoriesMatch(normalizedDirectories, effectiveDirectories))
            {
                StatusText = $"Core hat die Share-Verzeichnisliste nicht vollständig übernommen: angefordert {normalizedDirectories.Count:N0}, zurückgelesen {effectiveDirectories.Count:N0}.";
                throw new InvalidOperationException(StatusText);
            }

            OnPropertyChanged(nameof(ConfiguredShareDirectories));
            OnPropertyChanged(nameof(CoreNick));
            OnPropertyChanged(nameof(CoreIncomingDirectory));
            return effectiveDirectories;
        }
        catch (Exception ex)
        {
            StatusText = "Share-Verzeichnisse konnten nicht übertragen werden: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool ShareDirectoriesMatch(
        IEnumerable<AjShareDirectory> expected,
        IEnumerable<AjShareDirectory> actual)
    {
        static string BuildKey(AjShareDirectory directory)
        {
            string path = (directory.Name ?? string.Empty)
                .Trim()
                .Trim('"')
                .Replace('\\', '/');
            if (path.Length > 1)
                path = path.TrimEnd('/');

            string shareMode = directory.ShareMode.Equals(
                ShareDirectoryDraftSemantics.RecursiveShareMode,
                StringComparison.OrdinalIgnoreCase)
                ? ShareDirectoryDraftSemantics.RecursiveShareMode
                : ShareDirectoryDraftSemantics.SingleDirectoryShareMode;
            return path + "\u001f" + shareMode;
        }

        string[] expectedKeys = ShareDirectoryDraftSemantics
            .Normalize(expected)
            .Where(directory => !string.IsNullOrWhiteSpace(directory.Name))
            .Select(BuildKey)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] actualKeys = ShareDirectoryDraftSemantics
            .Normalize(actual)
            .Where(directory => !string.IsNullOrWhiteSpace(directory.Name))
            .Select(BuildKey)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return expectedKeys.SequenceEqual(actualKeys, StringComparer.OrdinalIgnoreCase);
    }

    public void SetVisibleSharesOverride(IReadOnlyList<AjShareFile>? shares)
    {
        _visibleSharesOverride = shares;
        OnPropertyChanged(nameof(VisibleShares));
    }

    public async Task ReloadSharesAsync()
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || IsBusy || client is null || state is null)
            return;

        IsBusy = true;
        StatusText = "Lade Share-Dateiliste ...";

        try
        {
            string xml = await client.GetShareXmlAsync().ConfigureAwait(true);
            List<AjShareFile> shares = AjXmlParser.ParseShares(xml);

            state.Shares.Clear();
            foreach (AjShareFile share in shares)
                state.Shares.Add(share);

            OnPropertyChanged(nameof(Shares));
            OnPropertyChanged(nameof(ShareCountText));
            OnPropertyChanged(nameof(ShareSizeText));
            StatusText = $"Share-Dateiliste geladen: {shares.Count:N0} Dateien · {ShareSizeText}";
        }
        catch (Exception ex)
        {
            StatusText = "Share-Dateiliste konnte nicht geladen werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetSharePriorityAsync(IEnumerable<AjShareFile> shares, string priorityText)
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        List<AjShareFile> selectedShares = (shares ?? Array.Empty<AjShareFile>())
            .Where(share => share.Id > 0)
            .GroupBy(share => share.Id)
            .Select(group => group.First())
            .ToList();
        if (!IsConnected || IsBusy || client is null || _state is null || selectedShares.Count == 0)
            return;

        if (!int.TryParse((priorityText ?? string.Empty).Trim(), out int requestedPriority))
        {
            StatusText = "Share-Priorität ungültig. Erwartet: 1 bis 250.";
            return;
        }

        int priority = Math.Clamp(requestedPriority, 1, 250);
        string targetText = selectedShares.Count == 1
            ? selectedShares[0].DisplayFilename
            : $"{selectedShares.Count:N0} Share-Dateien";
        IsBusy = true;
        StatusText = $"Setze Share-Priorität {priority}: {targetText}";

        try
        {
            const int batchSize = 75;
            for (int offset = 0; offset < selectedShares.Count; offset += batchSize)
            {
                long[] ids = selectedShares
                    .Skip(offset)
                    .Take(batchSize)
                    .Select(share => share.Id)
                    .ToArray();
                await client.SetPriorityAsync(ids, priority).ConfigureAwait(true);
            }

            foreach (AjShareFile share in selectedShares)
                share.Priority = priority;

            StatusText = selectedShares.Count == 1
                ? $"Share-Priorität gesetzt: {priority} · {selectedShares[0].DisplayFilename}"
                : $"Share-Priorität gesetzt: {priority} · {selectedShares.Count:N0} Dateien";
        }
        catch (Exception ex)
        {
            StatusText = "Share-Priorität konnte nicht gesetzt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResetAllSharePrioritiesAsync()
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || IsBusy || client is null || state is null)
            return;

        List<AjShareFile> shares = state.Shares.ToList();
        if (shares.Count == 0)
            return;

        List<long> ids = shares
            .Select(share => share.Id)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return;

        IsBusy = true;
        StatusText = $"Setze alle Share-Prioritäten auf 1: {shares.Count:N0} Dateien";

        try
        {
            const int batchSize = 75;
            for (int offset = 0; offset < ids.Count; offset += batchSize)
            {
                long[] batch = ids
                    .Skip(offset)
                    .Take(batchSize)
                    .ToArray();
                await client.SetPriorityAsync(batch, 1).ConfigureAwait(true);
            }

            foreach (AjShareFile share in shares)
                share.Priority = 1;

            StatusText = $"Alle Share-Prioritäten auf 1 gesetzt: {ids.Count:N0} Dateien";
        }
        catch (Exception ex)
        {
            StatusText = "Alle Share-Prioritäten konnten nicht auf 1 gesetzt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool CanShowSelectedDownloadPartList
        => IsConnected
            && !IsBusy
            && SelectedDownload is { } download
            && !DownloadActionSemantics.IsTerminal(download);

    public async Task<(string Filename, long FileSize, IReadOnlyList<AjPart> Parts, int SourcePartListCount, int SourceCandidateCount, int SourceErrorCount)?> LoadSelectedDownloadPartListAsync()
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (download is null || client is null || state is null || !CanShowSelectedDownloadPartList)
            return null;

        IsBusy = true;
        StatusText = $"Lade aggregierte Partliste: {download.DisplayFilename}";

        try
        {
            string xml = await client.GetDownloadPartListXmlAsync(download.Id).ConfigureAwait(true);
            List<AjPart> downloadParts = AjXmlParser.ParseParts(xml)
                .Where(part => part.FromPosition >= 0)
                .OrderBy(part => part.FromPosition)
                .ToList();
            long fileSize = AjXmlParser.ParseFileSizeFromPartList(xml);
            if (fileSize <= 0)
                fileSize = download.Size;

            List<AjUserSource> sources = state.Users
                .Where(user => user.DownloadId == download.Id)
                .Where(IsPartListSourceCandidate)
                .GroupBy(user => user.Id)
                .Select(group => group.First())
                .OrderByDescending(IsActiveConnectedSource)
                .ThenBy(user => user.QueueSortKey)
                .ThenByDescending(user => user.Speed)
                .ToList();

            List<IReadOnlyList<AjPart>> sourcePartLists = new();
            int sourceErrors = 0;
            foreach (AjUserSource source in sources)
            {
                try
                {
                    string sourceXml = await client.GetUserPartListXmlAsync(source.Id).ConfigureAwait(true);
                    List<AjPart> sourceParts = AjXmlParser.ParseParts(sourceXml)
                        .Where(part => part.FromPosition >= 0)
                        .OrderBy(part => part.FromPosition)
                        .ToList();
                    if (sourceParts.Count > 0)
                        sourcePartLists.Add(sourceParts);
                }
                catch
                {
                    sourceErrors++;
                }
            }

            List<(long From, long To)> activeTransferRanges = BuildPartListActiveTransferRanges(sources, fileSize);
            List<AjPart> parts = DownloadPartListAggregator.Aggregate(
                downloadParts,
                sourcePartLists,
                fileSize,
                activeTransferRanges);

            string sourceText = sources.Count == 0
                ? "keine Quellenpartlisten"
                : $"Quellenpartlisten {sourcePartLists.Count:N0}/{sources.Count:N0}";
            if (sourceErrors > 0)
                sourceText += $", Fehler {sourceErrors:N0}";

            StatusText = $"Aggregierte Partliste geladen: {download.DisplayFilename} · {sourceText}";
            return (download.DisplayFilename, fileSize, parts, sourcePartLists.Count, sources.Count, sourceErrors);
        }
        catch (Exception ex)
        {
            StatusText = "Partliste konnte nicht geladen werden: " + ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsVisibleDownloadSource(AjUserSource user)
        => user.Status is not (3 or 4 or 8 or 16);

    private static bool IsPartListSourceCandidate(AjUserSource user)
        => user.Id > 0
            && IsVisibleDownloadSource(user)
            && user.Status != 6;

    private static List<(long From, long To)> BuildPartListActiveTransferRanges(
        IEnumerable<AjUserSource> sources,
        long fileSize)
    {
        List<(long From, long To)> ranges = new();
        if (fileSize <= 0)
            return ranges;

        const int maxVisualSegments = 300;
        long minimumVisibleRange = Math.Max(1, fileSize / maxVisualSegments);

        foreach (AjUserSource source in sources)
        {
            if (!IsActiveConnectedSource(source))
                continue;

            long from = Math.Clamp(source.DownloadFrom, 0, fileSize);
            long to = Math.Clamp(source.DownloadTo, 0, fileSize);
            if (to > from)
            {
                ranges.Add((from, to));
                continue;
            }

            long position = Math.Clamp(source.ActualDownloadPosition, 0, fileSize);
            if (position <= 0 && fileSize > 1)
                continue;

            long fallbackFrom = Math.Clamp(position - (minimumVisibleRange / 2), 0, fileSize - 1);
            long fallbackTo = Math.Clamp(fallbackFrom + minimumVisibleRange, fallbackFrom + 1, fileSize);
            ranges.Add((fallbackFrom, fallbackTo));
        }

        return ranges;
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

    public async Task ImportMoreServersAsync()
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (!IsConnected || IsBusy || client is null || state is null)
            return;

        IsBusy = true;
        StatusText = "Mehr Server: öffentliche Serverliste wird geladen ...";

        try
        {
            using HttpClient listClient = new() { Timeout = TimeSpan.FromSeconds(10) };
            string xml = await listClient.GetStringAsync(
                "http://www.applejuicenet.cc/serverlist/xmllist.php").ConfigureAwait(true);
            IReadOnlyList<string> links = AjServerListParser.ParseLinks(xml);
            if (links.Count == 0)
            {
                StatusText = "Mehr Server: öffentliche Serverliste enthält keine gültigen Serverlinks.";
                return;
            }

            AjCoreCompatibilityProfile profile = AjCoreCompatibilityProfile.FromCoreVersion(CoreVersion);
            int accepted = 0;
            int alreadyKnown = 0;
            int rejected = 0;

            foreach (string link in links)
            {
                AjProcessLinkResult result = await client
                    .ProcessLinkDetailedAsync(link, profile, string.Empty)
                    .ConfigureAwait(true);

                if (result.IsAccepted)
                    accepted++;
                else if (result.IsAlreadyDownloaded)
                    alreadyKnown++;
                else
                    rejected++;
            }

            string modifiedXml = await client.GetModifiedXmlAsync(
                timestamp: 0,
                sessionId: state.SessionId,
                filter: "server;informations").ConfigureAwait(true);
            ModifiedParseResult refresh = AjXmlParser.ParseModified(modifiedXml);
            AjStateUpdater.Apply(state, refresh);
            if (refresh.CoreTimestamp > 0)
                state.LastTimestamp = refresh.CoreTimestamp;
            RaiseStateProperties();

            StatusText = $"Mehr Server: {links.Count:N0} Link(s) verarbeitet · {accepted:N0} akzeptiert · {alreadyKnown:N0} bereits bekannt · {rejected:N0} abgewiesen.";
        }
        catch (Exception ex)
        {
            StatusText = "Mehr Server fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string> CheckCorePortReachabilityAsync()
    {
        ThrowIfDisposed();
        AjState? state = _state;

        string result;
        if (!IsConnected || state is null)
        {
            result = "Porttest: offline.";
        }
        else
        {
            string host = (state.NetworkInfo.Ip ?? string.Empty).Trim();
            int port = state.Settings.Port;

            if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            {
                result = "Porttest: nicht prüfbar — externe IP oder Core-Port sind noch nicht bekannt.";
            }
            else
            {
                bool reachable = await TcpReachabilityProbe.TestAsync(
                    host,
                    port,
                    ExternalCorePortTestTimeout).ConfigureAwait(true);

                result = reachable
                    ? "Porttest: erreichbar."
                    : "Porttest: nicht erreichbar — Portweiterleitung, Firewall oder Router prüfen.";
            }
        }

        StatusText = result;
        return result;
    }

    public async Task<string> ChangeCorePasswordAsync(string newPassword)
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        if (!IsConnected || IsBusy || client is null)
            throw new InvalidOperationException("Core ist nicht verbunden.");

        string requested = newPassword ?? string.Empty;
        IsBusy = true;
        StatusText = "Ändere Core-Passwort ...";

        try
        {
            await client.SetPasswordHashAsync(requested).ConfigureAwait(true);
            client.Password = requested;

            try
            {
                await client.GetSettingsXmlAsync().ConfigureAwait(true);
                StatusText = "Core-Passwort geändert und Verbindung verifiziert. Passwort wurde nicht gespeichert.";
            }
            catch (Exception verifyException)
            {
                StatusText = "Core-Passwort wurde übertragen, aber die Verbindung mit dem neuen Passwort konnte nicht verifiziert werden: "
                    + verifyException.Message;
            }

            return StatusText;
        }
        catch (Exception ex)
        {
            StatusText = "Core-Passwort konnte nicht geändert werden: " + ex.Message;
            throw;
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

    public async Task CleanTerminalDownloadsAsync()
    {
        ThrowIfDisposed();
        AppleJuiceCoreClient? client = _client;
        AjState? state = _state;
        if (client is null || state is null || !CanCleanDownloadList)
            return;

        SelectedDownload = null;
        IsBusy = true;
        StatusText = "Entferne fertige/abgebrochene Downloads ...";

        try
        {
            await client.CleanDownloadListAsync().ConfigureAwait(true);
            StatusText = "Downloadliste wird vollständig neu geladen ...";
            await ForceReloadDownloadsAfterCleanAsync(client, state).ConfigureAwait(true);
            StatusText = "Fertige/abgebrochene Downloads entfernt.";
        }
        catch (Exception ex)
        {
            StatusText = "Downloadliste konnte nicht bereinigt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetSelectedDownloadPowerDownloadAsync(string factorText)
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        if (download is null || client is null || !CanSetPowerDownloadSelectedDownload)
            return;

        string input = (factorText ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(
                input,
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out double factor)
            || factor <= 1.0)
        {
            StatusText = "Powerdownload-Faktor ungültig. Erwartet: 2,2 bis 50,0.";
            return;
        }

        int rawPowerDownload = AjDownload.PowerDownloadFactorToRaw(factor);
        if (rawPowerDownload <= 0)
        {
            StatusText = "Powerdownload-Faktor ungültig. Erwartet: 2,2 bis 50,0.";
            return;
        }

        double normalizedFactor = AjDownload.PowerDownloadRawToFactor(rawPowerDownload);
        IsBusy = true;
        StatusText = $"Setze Powerdownload Faktor {normalizedFactor:0.0}: {download.DisplayFilename}";

        try
        {
            await client.SetPowerDownloadAsync(download.Id, rawPowerDownload).ConfigureAwait(true);
            download.PowerDownload = rawPowerDownload;
            StatusText = $"Powerdownload gesetzt: Faktor {normalizedFactor:0.0} · {download.DisplayFilename}";
        }
        catch (Exception ex)
        {
            StatusText = "Powerdownload konnte nicht gesetzt werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ClearSelectedDownloadPowerDownloadAsync()
    {
        ThrowIfDisposed();
        AjDownload? download = SelectedDownload;
        AppleJuiceCoreClient? client = _client;
        if (download is null || client is null || !CanSetPowerDownloadSelectedDownload)
            return;

        IsBusy = true;
        StatusText = $"Lösche Powerdownload: {download.DisplayFilename}";

        try
        {
            await client.SetPowerDownloadAsync(download.Id, 0).ConfigureAwait(true);
            download.PowerDownload = 0;
            StatusText = $"Powerdownload gelöscht: {download.DisplayFilename}";
        }
        catch (Exception ex)
        {
            StatusText = "Powerdownload konnte nicht gelöscht werden: " + ex.Message;
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
                    ResolveHostIncomingRoot(state),
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

    private string ResolveHostIncomingRoot(AjState state)
    {
        string coreIncoming = (state.Settings.IncomingDirectory ?? string.Empty).Trim().Trim('"');
        if (CanUseCoreIncomingDirectoryDirectly(coreIncoming))
            return coreIncoming;

        return LocalIncomingMappingText;
    }

    private bool CanUseCoreIncomingDirectoryDirectly(string coreIncoming)
    {
        if (string.IsNullOrWhiteSpace(coreIncoming))
            return false;

        try
        {
            if (coreIncoming.StartsWith(@"\\", StringComparison.Ordinal))
                return Directory.Exists(coreIncoming);

            if (!OperatingSystem.IsWindows() || !Path.IsPathRooted(coreIncoming))
                return false;

            if (!Uri.TryCreate(EndpointText.Trim(), UriKind.Absolute, out Uri? endpoint))
                return false;

            return IsLocalCoreHost(endpoint.Host) && Directory.Exists(coreIncoming);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLocalCoreHost(string host)
    {
        string value = (host ?? string.Empty).Trim();
        if (value.Length == 0
            || string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            System.Net.IPAddress[] localAddresses = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName());

            if (System.Net.IPAddress.TryParse(value, out System.Net.IPAddress? address))
            {
                return System.Net.IPAddress.IsLoopback(address)
                    || localAddresses.Contains(address);
            }

            string machineName = Environment.MachineName;
            string dnsHostName = System.Net.Dns.GetHostName();
            if (string.Equals(value, machineName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, dnsHostName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return System.Net.Dns.GetHostAddresses(value)
                .Any(resolved => System.Net.IPAddress.IsLoopback(resolved) || localAddresses.Contains(resolved));
        }
        catch
        {
            return false;
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
        long previousMaxSearchId = _state?.Searches
            .Where(search => search.Id > 0)
            .Select(search => search.Id)
            .DefaultIfEmpty(0L)
            .Max() ?? 0L;
        ClearPendingSearchAdoption();
        IsBusy = true;
        StatusText = $"Sende Suche: {text}";

        try
        {
            await _client.SearchAsync(text).ConfigureAwait(true);
            _pendingSearchText = text;
            _pendingSearchPreviousMaxId = previousMaxSearchId;
            _pendingSearchPollCount = 0;
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

    private async Task ForceReloadDownloadsAfterCleanAsync(AppleJuiceCoreClient client, AjState state)
    {
        string xml = await client.GetModifiedXmlAsync(
            timestamp: 0,
            sessionId: null,
            filter: "ids;down;user;informations").ConfigureAwait(false);
        ModifiedParseResult result = AjXmlParser.ParseModified(xml);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_state, state) || !IsConnected)
                return;

            state.Downloads.Clear();
            state.Users.Clear();
            AjStateUpdater.Apply(state, result);
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
        ClearPendingSearchAdoption();

        if (!clearState)
            return;

        _state = null;
        _visibleSharesOverride = null;
        _serverReconnectRestriction.Clear();
        _serverReconnectRestrictionEndpoint = string.Empty;
        SelectedDownload = null;
        SelectedSearchEntry = null;
        SelectedSearch = null;
        CoreVersion = "-";
        RaiseStateProperties();
    }

    private void TryAdoptPendingSearch()
    {
        AjState? state = _state;
        if (state is null || string.IsNullOrWhiteSpace(_pendingSearchText))
            return;

        _pendingSearchPollCount++;
        bool allowExistingFallback =
            _pendingSearchPollCount >= SearchAdoptionExistingFallbackPollCount;
        AjSearch? candidate = SearchStartAdoptionSemantics.FindCandidate(
            state.Searches,
            _pendingSearchText,
            _pendingSearchPreviousMaxId,
            allowExistingFallback);

        if (candidate is not null)
        {
            SelectedSearch = candidate;
            ClearPendingSearchAdoption();
            return;
        }

        if (_pendingSearchPollCount >= SearchAdoptionMaximumPollCount)
            ClearPendingSearchAdoption();
    }

    private void ClearPendingSearchAdoption()
    {
        _pendingSearchText = string.Empty;
        _pendingSearchPreviousMaxId = 0;
        _pendingSearchPollCount = 0;
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

            TryAdoptPendingSearch();

            RefreshServerReconnectRestrictionState(DateTimeOffset.UtcNow);

            if (SelectedDownload is not null && !_state.Downloads.Any(download => download.Id == SelectedDownload.Id))
                SelectedDownload = null;

            if (SelectedSearch is null
                && string.IsNullOrWhiteSpace(_pendingSearchText)
                && _state.Searches.Count > 0)
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

    private void ExternalCorePortTestTimerOnTick(object? sender, EventArgs e)
    {
        if (_disposed || !IsConnected || _externalCorePortTestRunning)
            return;

        _ = RunAutomaticCorePortReachabilityAsync();
    }

    private void StartServerReachabilityTimer()
    {
        if (_disposed || !IsConnected)
            return;

        _serverReachabilityTimer.Start();
        _ = ProbeNextServerReachabilityAsync();
        _externalCorePortTestTimer.Start();
        _ = RunAutomaticCorePortReachabilityAsync();
    }

    private void StopServerReachabilityTimer()
    {
        _serverReachabilityTimer.Stop();
        _externalCorePortTestTimer.Stop();
        _serverReachabilityNextIndex = 0;
    }

    private async Task RunAutomaticCorePortReachabilityAsync()
    {
        if (_externalCorePortTestRunning || _disposed || !IsConnected)
            return;

        _externalCorePortTestRunning = true;
        try
        {
            await CheckCorePortReachabilityAsync().ConfigureAwait(true);
        }
        finally
        {
            _externalCorePortTestRunning = false;
        }
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
            CoreConnectionLost?.Invoke(message);
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

    private void UpdateUploadSpeedHistories()
    {
        AjState? state = _state;
        if (state is null)
        {
            _uploadSpeedHistory.Clear();
            _lastUploadSpeedSamples.Clear();
            _lastUploadSpeedSampleTimesUtc.Clear();
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        HashSet<long> currentIds = new();

        foreach (AjUpload upload in state.Uploads)
        {
            long key = upload.Id;
            if (key <= 0)
                continue;

            currentIds.Add(key);

            UploadSpeedSampleSignature signature = new(
                upload.Status,
                Math.Max(0L, upload.Speed),
                upload.UploadFrom,
                upload.UploadTo,
                upload.ActualUploadPosition);

            bool hasLastSignature = _lastUploadSpeedSamples.TryGetValue(key, out UploadSpeedSampleSignature lastSignature);
            bool hasLastTime = _lastUploadSpeedSampleTimesUtc.TryGetValue(key, out DateTime lastSampleTimeUtc);
            bool changed = !hasLastSignature || !signature.Equals(lastSignature);
            bool due = !hasLastTime || nowUtc - lastSampleTimeUtc >= UploadSpeedHistoryMinimumSampleDistance;

            if (changed || due)
            {
                Queue<long> history = GetUploadSpeedHistory(key);
                history.Enqueue(Math.Max(0L, upload.Speed));
                while (history.Count > UploadSpeedHistoryLength)
                    history.Dequeue();

                _lastUploadSpeedSamples[key] = signature;
                _lastUploadSpeedSampleTimesUtc[key] = nowUtc;
            }

            if (_uploadSpeedHistory.TryGetValue(key, out Queue<long>? existingHistory))
                upload.SpeedHistory = existingHistory.ToArray();
        }

        CleanupUploadSpeedHistories(currentIds);
    }

    private Queue<long> GetUploadSpeedHistory(long uploadId)
    {
        if (_uploadSpeedHistory.TryGetValue(uploadId, out Queue<long>? history))
            return history;

        history = new Queue<long>();
        _uploadSpeedHistory[uploadId] = history;
        return history;
    }

    private void CleanupUploadSpeedHistories(HashSet<long> currentIds)
    {
        List<long> staleIds = _uploadSpeedHistory.Keys
            .Where(id => !currentIds.Contains(id))
            .ToList();

        foreach (long id in staleIds)
        {
            _uploadSpeedHistory.Remove(id);
            _lastUploadSpeedSamples.Remove(id);
            _lastUploadSpeedSampleTimesUtc.Remove(id);
        }
    }

    private void UpdateDownloadSourceCounts()
    {
        AjState? state = _state;
        if (state is null)
            return;

        Dictionary<long, int> totalCounts = state.Users
            .Where(user => user.DownloadId > 0)
            .GroupBy(user => user.DownloadId)
            .ToDictionary(group => group.Key, group => group.Count());

        Dictionary<long, int> activeCounts = state.Users
            .Where(user => user.DownloadId > 0 && IsActiveConnectedSource(user))
            .GroupBy(user => user.DownloadId)
            .ToDictionary(group => group.Key, group => group.Count());

        Dictionary<long, long> speedByDownload = state.Users
            .Where(user => user.DownloadId > 0)
            .GroupBy(user => user.DownloadId)
            .ToDictionary(group => group.Key, group => group.Sum(user => Math.Max(0, user.Speed)));

        foreach (AjDownload download in state.Downloads)
        {
            totalCounts.TryGetValue(download.Id, out int sourceCount);
            activeCounts.TryGetValue(download.Id, out int activeSourceCount);
            speedByDownload.TryGetValue(download.Id, out long downloadSpeed);
            download.SourceCount = sourceCount;
            download.ActiveSourceCount = activeSourceCount;
            download.DownloadSpeed = downloadSpeed;
        }
    }

    private static bool IsActiveConnectedSource(AjUserSource user)
        => user.Status == 7 || user.Speed > 0;

    private void RaiseStateProperties()
    {
        UpdateDownloadSourceCounts();
        UpdateUploadSpeedHistories();
        UpdateServerCoreStates();
        OnPropertyChanged(nameof(Downloads));
        OnPropertyChanged(nameof(SelectedDownloadSources));
        OnPropertyChanged(nameof(SelectedDownloadSourcesText));
        OnPropertyChanged(nameof(Uploads));
        OnPropertyChanged(nameof(ActiveUploads));
        OnPropertyChanged(nameof(InactiveUploads));
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(Searches));
        OnPropertyChanged(nameof(Shares));
        OnPropertyChanged(nameof(VisibleShares));
        OnPropertyChanged(nameof(ShareCountText));
        OnPropertyChanged(nameof(ShareSizeText));
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
