using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using AJCC.Core.Helpers;
using AJCC.Core.Links;
using AJCC.Core.Models;
using AJCC.Core.Protocol;
using AJCC.Core.Services;
using AJCC.Desktop.Services;
using AJCC.Desktop.ViewModels;

namespace AJCC.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private readonly ExternalVlcConfigurationStore _externalVlcConfigurationStore = new();
    private readonly LocalIncomingMappingStore _localIncomingMappingStore = new();
    private readonly UiPreferencesStore _uiPreferencesStore = new();
    private readonly CoreProfileStore _coreProfileStore = new();
    private readonly ObservableCollection<CoreProfileEntry> _coreProfiles = new();
    private readonly Dictionary<string, string> _coreProfileSessionPasswords = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _coreProfileReachabilityTimer = new();
    private static readonly TimeSpan CoreProfileReachabilityTimeout = TimeSpan.FromMilliseconds(2500);
    private const int ActiveCoreReachabilityFailureThreshold = 2;
    private string _defaultCoreProfileId = string.Empty;
    private bool _loadingCoreProfiles;
    private bool _coreProfileReachabilityRunning;
    private bool _coreProfileFailoverInProgress;
    private bool _suppressCoreProfileSwitchConfirmation;
    private bool _autoLoadShareFilesAtStartup;
    private bool _startupShareLoadEnabledForThisProcess;
    private bool _automaticStartupShareLoadHandledThisProcess;
    private int _activeCoreReachabilityFailureCount;
    private AjServer? _selectedServerForContext;
    private AjUserSource? _selectedDownloadSourceForContext;
    private AjShareFile? _selectedShareForContext;
    private int _embeddedPartListRequestVersion;
    private long _embeddedPartListDownloadId;
    private long _embeddedPartListSourceId;
    private bool _suppressDownloadSourceSelectionChanged;
    private string _appliedShareFilter = string.Empty;
    private int _shareFilterRequestVersion;
    private const double ShareDragThreshold = 6.0;
    private AjShareFile? _shareDragCandidate;
    private double _shareDragStartX;
    private double _shareDragStartY;
    private bool _shareDragInProgress;
    private PointerPressedEventArgs? _shareDragTriggerEvent;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        UiPreferences uiPreferences = _uiPreferencesStore.Load();
        _suppressCoreProfileSwitchConfirmation = uiPreferences.SuppressCoreProfileSwitchConfirmation;
        _autoLoadShareFilesAtStartup = uiPreferences.AutoLoadShareFilesAtStartup;
        _startupShareLoadEnabledForThisProcess = _autoLoadShareFilesAtStartup;
        _viewModel.CoreConnectionLost += ViewModel_OnCoreConnectionLost;
        LoadCoreProfiles();
        _coreProfileReachabilityTimer.Interval = TimeSpan.FromSeconds(10);
        _coreProfileReachabilityTimer.Tick += CoreProfileReachabilityTimer_OnTick;
        _coreProfileReachabilityTimer.Start();
        ConfigureLocalIncomingMappingControls();
        AddHandler(
            InputElement.PointerPressedEvent,
            MainWindow_OnPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerMovedEvent,
            MainWindow_OnPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerReleasedEvent,
            MainWindow_OnPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.ContextRequestedEvent,
            MainWindow_OnContextRequested,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Opened += async (_, _) => await RefreshCoreProfileReachabilityAsync();
        Closed += (_, _) =>
        {
            _coreProfileReachabilityTimer.Stop();
            _viewModel.CoreConnectionLost -= ViewModel_OnCoreConnectionLost;
        };
        Closed += MainWindow_OnClosed;
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void LoadCoreProfiles()
    {
        ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
        if (comboBox is null)
            return;

        _loadingCoreProfiles = true;
        try
        {
            CoreProfileStoreSnapshot snapshot = _coreProfileStore.Load();
            if (snapshot.Profiles.Count == 0)
            {
                string endpoint = CoreProfileStore.NormalizeEndpoint(_viewModel.EndpointText);
                CoreProfileEntry standard = new()
                {
                    Name = "Standard-Core",
                    Endpoint = endpoint
                };
                snapshot.Profiles.Add(standard);
                snapshot.DefaultProfileId = standard.Id;
                _coreProfileStore.TrySave(snapshot.Profiles, snapshot.DefaultProfileId, out _);
            }

            _coreProfiles.Clear();
            foreach (CoreProfileEntry profile in snapshot.Profiles)
                _coreProfiles.Add(profile);

            _defaultCoreProfileId = snapshot.DefaultProfileId;
            comboBox.ItemsSource = _coreProfiles;

            CoreProfileEntry? selected = _coreProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.Id, _defaultCoreProfileId, StringComparison.OrdinalIgnoreCase))
                ?? _coreProfiles.FirstOrDefault(profile =>
                    string.Equals(
                        profile.Endpoint,
                        CoreProfileStore.TryNormalizeEndpoint(_viewModel.EndpointText),
                        StringComparison.OrdinalIgnoreCase))
                ?? _coreProfiles.FirstOrDefault();

            if (selected is not null)
            {
                _defaultCoreProfileId = selected.Id;
                comboBox.SelectedItem = selected;
                _viewModel.EndpointText = selected.Endpoint;
            }
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage("Core-Profile konnten nicht geladen werden: " + ex.Message);
        }
        finally
        {
            _loadingCoreProfiles = false;
        }
    }

    private void CoreProfileComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingCoreProfiles)
            return;

        if (sender is not ComboBox comboBox || comboBox.SelectedItem is not CoreProfileEntry profile)
            return;

        if (!profile.IsSelectable)
        {
            CoreProfileEntry? previousSelectable = e.RemovedItems
                .OfType<CoreProfileEntry>()
                .FirstOrDefault(item => item.IsSelectable);

            CoreProfileEntry? activeSelectable = _viewModel.IsConnected
                ? FindCoreProfileByEndpoint(_viewModel.EndpointText)
                : null;
            if (activeSelectable?.IsSelectable != true)
                activeSelectable = null;

            CoreProfileEntry? fallback = previousSelectable
                ?? activeSelectable
                ?? _coreProfiles.FirstOrDefault(item => item.IsSelectable);

            _loadingCoreProfiles = true;
            try
            {
                comboBox.SelectedItem = fallback;
            }
            finally
            {
                _loadingCoreProfiles = false;
            }

            _viewModel.SetStatusMessage(
                $"Core-Profil {profile.Name} ist aktuell nicht erreichbar und kann nicht ausgewählt werden.");
            return;
        }

        if (_viewModel.IsConnected)
        {
            _viewModel.SetStatusMessage(
                $"Core-Zielprofil ausgewählt: {profile.Name} · Wechsel über 'Wechseln'. Aktive Verbindung bleibt bis dahin unverändert.");
            return;
        }

        if (!_viewModel.CanEditConnectionSettings)
            return;

        _viewModel.EndpointText = profile.Endpoint;

        _viewModel.SetStatusMessage(
            $"Core-Profil ausgewählt: {profile.Name} · Standard bleibt unverändert · Passwort wird höchstens für diese Sitzung gehalten.");
    }

    private async void CoreProfileComboBox_OnDropDownOpened(object? sender, EventArgs e)
        => await RefreshCoreProfileReachabilityAsync();

    private async void CoreProfileReachabilityTimer_OnTick(object? sender, EventArgs e)
        => await RefreshCoreProfileReachabilityAsync();

    private async Task RefreshCoreProfileReachabilityAsync()
    {
        if (_coreProfileReachabilityRunning
            || _coreProfileFailoverInProgress
            || _viewModel.IsBusy
            || _coreProfiles.Count == 0)
        {
            return;
        }

        CoreProfileEntry? failedActiveProfile = null;
        string failureMessage = string.Empty;

        _coreProfileReachabilityRunning = true;
        try
        {
            CoreProfileEntry? activeProfile = _viewModel.IsConnected
                ? FindCoreProfileByEndpoint(_viewModel.EndpointText)
                : null;

            foreach (CoreProfileEntry profile in _coreProfiles.ToList())
            {
                if (!_coreProfiles.Contains(profile))
                    continue;

                profile.SetReachabilityStatus(CoreProfileReachabilityStatus.Checking);

                if (activeProfile is not null
                    && string.Equals(profile.Id, activeProfile.Id, StringComparison.OrdinalIgnoreCase))
                {
                    bool activeReachable = await TestCoreProfileTcpEndpointAsync(profile);
                    if (activeReachable)
                    {
                        _activeCoreReachabilityFailureCount = 0;
                        profile.SetReachabilityStatus(CoreProfileReachabilityStatus.Reachable);
                        continue;
                    }

                    _activeCoreReachabilityFailureCount++;
                    profile.SetReachabilityStatus(CoreProfileReachabilityStatus.Unreachable);

                    if (_activeCoreReachabilityFailureCount < ActiveCoreReachabilityFailureThreshold)
                        continue;

                    bool verificationSucceeded = await TestCoreProfileTcpEndpointAsync(profile);
                    if (verificationSucceeded)
                    {
                        _activeCoreReachabilityFailureCount = 0;
                        profile.SetReachabilityStatus(CoreProfileReachabilityStatus.Reachable);
                        continue;
                    }

                    failureMessage =
                        $"Der XML-Port des aktiven Core ist in {_activeCoreReachabilityFailureCount:N0} aufeinanderfolgenden Profilprüfungen nicht erreichbar.";
                    failedActiveProfile = profile;
                    break;
                }

                bool profileReachable = await TestCoreProfileTcpEndpointAsync(profile);
                if (_coreProfiles.Contains(profile))
                {
                    profile.SetReachabilityStatus(
                        profileReachable
                            ? CoreProfileReachabilityStatus.Reachable
                            : CoreProfileReachabilityStatus.Unreachable);
                }
            }
        }
        finally
        {
            _coreProfileReachabilityRunning = false;
        }

        if (failedActiveProfile is not null && !_coreProfileFailoverInProgress)
            await TryAutomaticCoreProfileFailoverAsync(failedActiveProfile, failureMessage);
    }

    private static async Task<bool> TestCoreProfileTcpEndpointAsync(CoreProfileEntry profile)
    {
        try
        {
            CoreEndpoint endpoint = CoreEndpoint.Parse(profile.Endpoint);
            return await TcpReachabilityProbe.TestAsync(
                endpoint.Host,
                endpoint.BaseUri.Port,
                CoreProfileReachabilityTimeout);
        }
        catch
        {
            return false;
        }
    }

    private CoreProfileEntry? FindCoreProfileByEndpoint(string? endpointText)
    {
        string normalized = CoreProfileStore.TryNormalizeEndpoint(endpointText);
        return _coreProfiles.FirstOrDefault(profile =>
            string.Equals(
                CoreProfileStore.TryNormalizeEndpoint(profile.Endpoint),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    private List<CoreProfileEntry> BuildCoreFailoverCandidates(CoreProfileEntry failedProfile)
    {
        List<CoreProfileEntry> profiles = _coreProfiles.ToList();
        List<CoreProfileEntry> result = new();
        HashSet<string> added = new(StringComparer.OrdinalIgnoreCase);

        CoreProfileEntry? defaultProfile = profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _defaultCoreProfileId, StringComparison.OrdinalIgnoreCase));
        if (defaultProfile is not null
            && !string.Equals(defaultProfile.Id, failedProfile.Id, StringComparison.OrdinalIgnoreCase))
        {
            result.Add(defaultProfile);
            added.Add(defaultProfile.Id);
        }

        int failedIndex = profiles.FindIndex(profile =>
            string.Equals(profile.Id, failedProfile.Id, StringComparison.OrdinalIgnoreCase));
        if (failedIndex < 0)
            failedIndex = -1;

        for (int offset = 1; offset <= profiles.Count; offset++)
        {
            int index = (failedIndex + offset + profiles.Count) % profiles.Count;
            CoreProfileEntry candidate = profiles[index];
            if (string.Equals(candidate.Id, failedProfile.Id, StringComparison.OrdinalIgnoreCase)
                || !added.Add(candidate.Id))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private void SelectCoreProfileWithoutHandling(
        ComboBox comboBox,
        CoreProfileEntry profile)
    {
        _loadingCoreProfiles = true;
        try
        {
            comboBox.SelectedItem = profile;
        }
        finally
        {
            _loadingCoreProfiles = false;
        }
    }

    private async void ViewModel_OnCoreConnectionLost(string message)
    {
        if (_coreProfileFailoverInProgress)
            return;

        CoreProfileEntry? failedProfile = FindCoreProfileByEndpoint(_viewModel.EndpointText);
        if (failedProfile is null)
            return;

        failedProfile.SetReachabilityStatus(CoreProfileReachabilityStatus.Unreachable);
        await TryAutomaticCoreProfileFailoverAsync(failedProfile, message);
    }

    private async Task TryAutomaticCoreProfileFailoverAsync(
        CoreProfileEntry failedProfile,
        string reason)
    {
        if (_coreProfileFailoverInProgress)
            return;

        _coreProfileFailoverInProgress = true;
        _coreProfileReachabilityTimer.Stop();
        _activeCoreReachabilityFailureCount = 0;

        try
        {
            failedProfile.SetReachabilityStatus(CoreProfileReachabilityStatus.Unreachable);
            _viewModel.SetStatusMessage(
                $"Aktiver Core ausgefallen: {failedProfile.Name}. Suche erreichbares Ersatzprofil ...");

            await _viewModel.DisconnectAsync();

            foreach (CoreProfileEntry candidate in BuildCoreFailoverCandidates(failedProfile))
            {
                if (!_coreProfileSessionPasswords.TryGetValue(candidate.Id, out string? password))
                    continue;

                if (!await TestCoreProfileTcpEndpointAsync(candidate))
                {
                    candidate.SetReachabilityStatus(CoreProfileReachabilityStatus.Unreachable);
                    continue;
                }

                candidate.SetReachabilityStatus(CoreProfileReachabilityStatus.Reachable);

                CoreEndpoint endpoint;
                try
                {
                    endpoint = CoreEndpoint.Parse(candidate.Endpoint);
                }
                catch
                {
                    candidate.SetReachabilityStatus(CoreProfileReachabilityStatus.Unreachable);
                    continue;
                }

                _viewModel.EndpointText = endpoint.BaseUri.ToString();
                await _viewModel.ToggleConnectionAsync(password);
                if (!_viewModel.IsConnected)
                    continue;

                ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
                if (comboBox is not null)
                    SelectCoreProfileWithoutHandling(comboBox, candidate);

                _viewModel.SetStatusMessage(
                    $"Automatischer Core-Failover abgeschlossen: {candidate.Name} ist jetzt aktiv.");
                return;
            }

            if (_coreProfileSessionPasswords.TryGetValue(failedProfile.Id, out string? failedPassword)
                && await TestCoreProfileTcpEndpointAsync(failedProfile))
            {
                CoreEndpoint failedEndpoint = CoreEndpoint.Parse(failedProfile.Endpoint);
                _viewModel.EndpointText = failedEndpoint.BaseUri.ToString();
                await _viewModel.ToggleConnectionAsync(failedPassword);
                if (_viewModel.IsConnected)
                {
                    failedProfile.SetReachabilityStatus(CoreProfileReachabilityStatus.Reachable);
                    ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
                    if (comboBox is not null)
                        SelectCoreProfileWithoutHandling(comboBox, failedProfile);

                    _viewModel.SetStatusMessage(
                        $"Der bisherige Core {failedProfile.Name} ist wieder erreichbar und wurde erneut verbunden.");
                    return;
                }
            }

            string detail = string.IsNullOrWhiteSpace(reason)
                ? "Kein erreichbares Core-Profil mit bekanntem Sitzungspasswort verfügbar."
                : reason + " Kein erreichbares Core-Profil mit bekanntem Sitzungspasswort verfügbar.";
            _viewModel.SetStatusMessage(
                $"Automatischer Core-Failover nicht möglich. AJCC-X bleibt offline. {detail}");
        }
        finally
        {
            _coreProfileFailoverInProgress = false;
            _coreProfileReachabilityTimer.Start();
        }
    }

    private void SetDefaultCoreProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEditConnectionSettings)
            return;

        ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
        if (comboBox?.SelectedItem is not CoreProfileEntry profile)
        {
            _viewModel.SetStatusMessage("Kein Core-Profil als Standard ausgewählt.");
            return;
        }

        string previousDefaultProfileId = _defaultCoreProfileId;
        _defaultCoreProfileId = profile.Id;
        if (!_coreProfileStore.TrySave(_coreProfiles, _defaultCoreProfileId, out string errorMessage))
        {
            _defaultCoreProfileId = previousDefaultProfileId;
            _viewModel.SetStatusMessage("Standard-Core-Profil konnte nicht gespeichert werden: " + errorMessage);
            return;
        }

        _viewModel.SetStatusMessage($"Standard-Core-Profil gesetzt: {profile.Name}.");
    }

    private async void SaveCoreProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEditConnectionSettings)
            return;

        string endpoint;
        CoreEndpoint parsedEndpoint;
        try
        {
            endpoint = CoreProfileStore.NormalizeEndpoint(_viewModel.EndpointText);
            parsedEndpoint = CoreEndpoint.Parse(endpoint);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage("Core-Profil kann nicht gespeichert werden: " + ex.Message);
            return;
        }

        CoreProfileEntry? existing = _coreProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
        string initialName = existing?.Name ?? parsedEndpoint.Host;

        CoreProfileSaveDialog dialog = new(initialName);
        if (!await dialog.ShowDialog<bool>(this))
            return;

        string name = dialog.ProfileName.Trim();
        if (existing is null)
        {
            existing = new CoreProfileEntry
            {
                Name = name,
                Endpoint = endpoint
            };
            _coreProfiles.Add(existing);
        }
        else
        {
            existing.Name = name;
            existing.Endpoint = endpoint;
        }

        if (!_coreProfileStore.TrySave(_coreProfiles, _defaultCoreProfileId, out string errorMessage))
        {
            LoadCoreProfiles();
            _viewModel.SetStatusMessage("Core-Profil konnte nicht gespeichert werden: " + errorMessage);
            return;
        }

        LoadCoreProfiles();
        _viewModel.SetStatusMessage(
            $"Core-Profil gespeichert: {name} · Passwort wird nicht gespeichert.");
    }

    private async void EditCoreProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEditConnectionSettings)
            return;

        ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
        if (comboBox?.SelectedItem is not CoreProfileEntry profile)
        {
            _viewModel.SetStatusMessage("Kein Core-Profil zum Bearbeiten ausgewählt.");
            return;
        }

        CoreProfileEditDialog dialog = new(profile.Name, profile.Endpoint);
        if (!await dialog.ShowDialog<bool>(this))
            return;

        string name = dialog.ProfileName.Trim();
        string endpoint = dialog.Endpoint;
        bool duplicateEndpoint = _coreProfiles.Any(other =>
            !ReferenceEquals(other, profile)
            && string.Equals(other.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
        if (duplicateEndpoint)
        {
            _viewModel.SetStatusMessage("Für diesen Core-Endpunkt existiert bereits ein anderes Profil.");
            return;
        }

        string previousName = profile.Name;
        string previousEndpoint = profile.Endpoint;
        profile.Name = name;
        profile.Endpoint = endpoint;

        if (!_coreProfileStore.TrySave(_coreProfiles, _defaultCoreProfileId, out string errorMessage))
        {
            profile.Name = previousName;
            profile.Endpoint = previousEndpoint;
            LoadCoreProfiles();
            _viewModel.SetStatusMessage("Core-Profil konnte nicht geändert werden: " + errorMessage);
            return;
        }

        LoadCoreProfiles();

        TextBox? passwordInput = this.FindControl<TextBox>("PasswordInput");
        if (passwordInput is not null)
            passwordInput.Text = string.Empty;

        _viewModel.SetStatusMessage(
            $"Core-Profil geändert: {name} · Passwort bleibt Laufzeiteingabe.");
    }

    private void DeleteCoreProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanEditConnectionSettings)
            return;

        ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
        if (comboBox?.SelectedItem is not CoreProfileEntry profile)
        {
            _viewModel.SetStatusMessage("Kein Core-Profil zum Löschen ausgewählt.");
            return;
        }

        if (_coreProfiles.Count <= 1)
        {
            _viewModel.SetStatusMessage("Mindestens ein Core-Profil muss erhalten bleiben.");
            return;
        }

        string deletedName = profile.Name;
        _coreProfileSessionPasswords.Remove(profile.Id);
        bool deletedWasDefault = string.Equals(
            profile.Id,
            _defaultCoreProfileId,
            StringComparison.OrdinalIgnoreCase);
        int removedIndex = Math.Max(0, comboBox.SelectedIndex);
        bool saveSucceeded;
        string saveError;

        _loadingCoreProfiles = true;
        try
        {
            _coreProfiles.Remove(profile);

            if (deletedWasDefault)
                _defaultCoreProfileId = _coreProfiles[0].Id;

            int fallbackIndex = Math.Min(removedIndex, _coreProfiles.Count - 1);
            CoreProfileEntry fallback = _coreProfiles[fallbackIndex];
            comboBox.SelectedItem = fallback;
            _viewModel.EndpointText = fallback.Endpoint;

            TextBox? passwordInput = this.FindControl<TextBox>("PasswordInput");
            if (passwordInput is not null)
                passwordInput.Text = string.Empty;

            saveSucceeded = _coreProfileStore.TrySave(
                _coreProfiles,
                _defaultCoreProfileId,
                out saveError);
        }
        finally
        {
            _loadingCoreProfiles = false;
        }

        if (!saveSucceeded)
        {
            LoadCoreProfiles();
            _viewModel.SetStatusMessage(
                $"Core-Profil konnte nicht gelöscht werden: {saveError}");
            return;
        }

        _viewModel.SetStatusMessage(
            $"Core-Profil gelöscht: {deletedName}.");
    }


    private async void ManageCoreProfilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreProfileFailoverInProgress || !_viewModel.CanToggleConnection)
            return;

        ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
        string selectedProfileId = (comboBox?.SelectedItem as CoreProfileEntry)?.Id ?? string.Empty;
        string activeEndpoint = _viewModel.IsConnected
            ? CoreProfileStore.TryNormalizeEndpoint(_viewModel.EndpointText)
            : string.Empty;
        CoreProfileEntry? activeProfile = _viewModel.IsConnected
            ? _coreProfiles.FirstOrDefault(profile =>
                string.Equals(
                    CoreProfileStore.TryNormalizeEndpoint(profile.Endpoint),
                    activeEndpoint,
                    StringComparison.OrdinalIgnoreCase))
            : null;

        Dictionary<string, string> previousEndpoints = _coreProfiles.ToDictionary(
            profile => profile.Id,
            profile => CoreProfileStore.TryNormalizeEndpoint(profile.Endpoint),
            StringComparer.OrdinalIgnoreCase);

        CoreProfileManagerDialog dialog = new(
            _coreProfiles,
            _defaultCoreProfileId,
            activeProfile?.Id ?? string.Empty,
            activeEndpoint);
        if (!await dialog.ShowDialog<bool>(this))
            return;

        IReadOnlyList<CoreProfileEntry> updatedProfiles = dialog.ResultProfiles;
        string updatedDefaultProfileId = dialog.ResultDefaultProfileId;
        if (!_coreProfileStore.TrySave(updatedProfiles, updatedDefaultProfileId, out string errorMessage))
        {
            _viewModel.SetStatusMessage("Core-Profile konnten nicht gespeichert werden: " + errorMessage);
            return;
        }

        foreach (string cachedProfileId in _coreProfileSessionPasswords.Keys.ToList())
        {
            CoreProfileEntry? updatedProfile = updatedProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, cachedProfileId, StringComparison.OrdinalIgnoreCase));
            if (updatedProfile is null
                || !previousEndpoints.TryGetValue(cachedProfileId, out string? previousEndpoint)
                || !string.Equals(
                    previousEndpoint,
                    CoreProfileStore.TryNormalizeEndpoint(updatedProfile.Endpoint),
                    StringComparison.OrdinalIgnoreCase))
            {
                _coreProfileSessionPasswords.Remove(cachedProfileId);
            }
        }

        _loadingCoreProfiles = true;
        try
        {
            _coreProfiles.Clear();
            foreach (CoreProfileEntry profile in updatedProfiles)
            {
                _coreProfiles.Add(new CoreProfileEntry
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Endpoint = profile.Endpoint
                });
            }

            _defaultCoreProfileId = updatedDefaultProfileId;
            if (comboBox is not null)
            {
                comboBox.ItemsSource = _coreProfiles;
                CoreProfileEntry? selected = _coreProfiles.FirstOrDefault(profile =>
                        string.Equals(profile.Id, selectedProfileId, StringComparison.OrdinalIgnoreCase))
                    ?? _coreProfiles.FirstOrDefault(profile =>
                        string.Equals(profile.Id, activeProfile?.Id, StringComparison.OrdinalIgnoreCase))
                    ?? _coreProfiles.FirstOrDefault(profile =>
                        string.Equals(profile.Id, _defaultCoreProfileId, StringComparison.OrdinalIgnoreCase))
                    ?? _coreProfiles.FirstOrDefault();
                comboBox.SelectedItem = selected;

                if (!_viewModel.IsConnected && selected is not null)
                    _viewModel.EndpointText = selected.Endpoint;
            }
        }
        finally
        {
            _loadingCoreProfiles = false;
        }

        if (_viewModel.IsConnected)
            _viewModel.EndpointText = activeEndpoint;

        await RefreshCoreProfileReachabilityAsync();

        CoreProfileEntry? changedActiveProfile = _coreProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, activeProfile?.Id, StringComparison.OrdinalIgnoreCase));
        bool activeEndpointChanged = _viewModel.IsConnected
            && changedActiveProfile is not null
            && !string.Equals(
                CoreProfileStore.TryNormalizeEndpoint(changedActiveProfile.Endpoint),
                activeEndpoint,
                StringComparison.OrdinalIgnoreCase);

        _viewModel.SetStatusMessage(
            activeEndpointChanged
                ? "Core-Profile gespeichert. Das aktuell verbundene Profil wurde geändert; die laufende Verbindung bleibt bis 'Wechseln' unverändert."
                : $"Core-Profile gespeichert: {_coreProfiles.Count} · Standard: {_coreProfiles.FirstOrDefault(profile => string.Equals(profile.Id, _defaultCoreProfileId, StringComparison.OrdinalIgnoreCase))?.Name ?? "unbekannt"}.");
    }

    private async void SwitchCoreProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreProfileFailoverInProgress
            || !_viewModel.IsConnected
            || !_viewModel.CanToggleConnection)
        {
            return;
        }

        ComboBox? comboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
        if (comboBox?.SelectedItem is not CoreProfileEntry profile)
        {
            _viewModel.SetStatusMessage("Kein Core-Zielprofil ausgewählt.");
            return;
        }

        string activeEndpoint = CoreProfileStore.TryNormalizeEndpoint(_viewModel.EndpointText);
        string targetEndpoint = CoreProfileStore.TryNormalizeEndpoint(profile.Endpoint);
        if (string.Equals(activeEndpoint, targetEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.SetStatusMessage($"Core-Profil ist bereits aktiv: {profile.Name}.");
            return;
        }

        string password;
        if (_coreProfileSessionPasswords.TryGetValue(profile.Id, out string? cachedPassword))
        {
            password = cachedPassword;
        }
        else
        {
            CoreProfilePasswordDialog passwordDialog = new(profile.Name, profile.Endpoint);
            if (!await passwordDialog.ShowDialog<bool>(this))
                return;

            password = passwordDialog.Password;
        }

        CoreEndpoint parsedTarget;
        try
        {
            parsedTarget = CoreEndpoint.Parse(profile.Endpoint);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage("Core-Zielprofil ist ungültig: " + ex.Message);
            return;
        }

        _viewModel.SetStatusMessage($"Prüfe Ziel-Core vor dem Wechsel: {profile.Name} ...");

        ConnectionTestResult connection;
        try
        {
            using HttpClient testHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
            AppleJuiceCoreClient testClient = new(parsedTarget, password, testHttpClient);
            connection = await testClient.TestConnectionAsync();
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage("Core-Zielprofil konnte nicht geprüft werden: " + ex.Message);
            return;
        }

        if (!connection.Success)
        {
            _viewModel.SetStatusMessage(
                $"Core-Zielprofil nicht erreichbar oder Anmeldung abgewiesen: {connection.Message}");
            return;
        }

        if (!_suppressCoreProfileSwitchConfirmation)
        {
            ConfirmDialog confirm = new(
                "Core-Profil wechseln",
                $"Aktiven Core wechseln?\n\nVon: {activeEndpoint}\nZu: {profile.Name} · {parsedTarget.BaseUri}\n\nDie sichtbaren Core-Daten werden vollständig geleert und vom Ziel-Core neu geladen.",
                "Wechseln",
                "Abbrechen",
                showSuppressFutureConfirmationOption: true);
            if (!await confirm.ShowDialog<bool>(this))
                return;

            if (confirm.SuppressFutureConfirmationRequested)
            {
                if (_uiPreferencesStore.TrySave(
                        new UiPreferences(true, _autoLoadShareFilesAtStartup),
                        out string errorMessage))
                {
                    _suppressCoreProfileSwitchConfirmation = true;
                }
                else
                {
                    _viewModel.SetStatusMessage(
                        "Core-Wechsel-Rückfrage konnte nicht gespeichert werden: " + errorMessage);
                }
            }
        }

        await _viewModel.ToggleConnectionAsync(string.Empty);
        if (_viewModel.IsConnected)
        {
            _viewModel.SetStatusMessage("Core-Profilwechsel abgebrochen: aktive Verbindung konnte nicht getrennt werden.");
            return;
        }

        _viewModel.EndpointText = parsedTarget.BaseUri.ToString();
        await _viewModel.ToggleConnectionAsync(password);

        TextBox? passwordInput = this.FindControl<TextBox>("PasswordInput");
        if (_viewModel.IsConnected)
        {
            _coreProfileSessionPasswords[profile.Id] = password;
            if (passwordInput is not null)
                passwordInput.Text = string.Empty;

            _viewModel.SetStatusMessage($"Core-Profil gewechselt: {profile.Name} · vollständiger State wurde neu geladen.");
        }
        else if (passwordInput is not null)
        {
            passwordInput.Text = password;
        }
    }

    private async void ConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreProfileFailoverInProgress)
            return;

        ComboBox? profileComboBox = this.FindControl<ComboBox>("CoreProfileComboBox");
        CoreProfileEntry? selectedProfile = profileComboBox?.SelectedItem as CoreProfileEntry;

        if (_viewModel.IsConnected)
        {
            await _viewModel.ToggleConnectionAsync(string.Empty);
            if (!_viewModel.IsConnected && selectedProfile is not null)
                _viewModel.EndpointText = selectedProfile.Endpoint;
            return;
        }

        if (selectedProfile is null)
        {
            _viewModel.SetStatusMessage("Kein Core-Profil ausgewählt.");
            return;
        }

        _viewModel.EndpointText = selectedProfile.Endpoint;

        string password;
        if (_coreProfileSessionPasswords.TryGetValue(selectedProfile.Id, out string? cachedPassword))
        {
            password = cachedPassword;
        }
        else
        {
            CoreProfilePasswordDialog passwordDialog = new(selectedProfile.Name, selectedProfile.Endpoint);
            if (!await passwordDialog.ShowDialog<bool>(this))
                return;

            password = passwordDialog.Password;
        }

        await _viewModel.ToggleConnectionAsync(password);
        if (_viewModel.IsConnected)
        {
            _coreProfileSessionPasswords[selectedProfile.Id] = password;
            await RunOptionalStartupShareLoadAsync();
        }
    }

    private async Task RunOptionalStartupShareLoadAsync()
    {
        if (_automaticStartupShareLoadHandledThisProcess || !_viewModel.IsConnected)
            return;

        _automaticStartupShareLoadHandledThisProcess = true;

        if (_uiPreferencesStore.HasStartupShareLoadMarker())
        {
            _uiPreferencesStore.ClearStartupShareLoadMarker();
            if (_startupShareLoadEnabledForThisProcess)
            {
                _startupShareLoadEnabledForThisProcess = false;
                _autoLoadShareFilesAtStartup = false;

                string message =
                    "Der automatische Share-Startload wurde deaktiviert, weil der letzte Start dabei nicht sauber abgeschlossen wurde. "
                    + "AJCC-X lädt die Share-Dateiliste in diesem Programmstart nicht automatisch.";
                if (!_uiPreferencesStore.TrySave(
                        new UiPreferences(_suppressCoreProfileSwitchConfirmation, false),
                        out string errorMessage))
                {
                    message += " Die deaktivierte Einstellung konnte nicht gespeichert werden: " + errorMessage;
                }

                _viewModel.SetStatusMessage(message);
                ConfirmDialog warning = new(
                    "Share-Startload deaktiviert",
                    message,
                    "OK",
                    "Schließen");
                await warning.ShowDialog<bool>(this);
            }
        }

        if (!_startupShareLoadEnabledForThisProcess)
            return;

        _uiPreferencesStore.MarkStartupShareLoadInProgress();
        try
        {
            await _viewModel.ReloadSharesAsync();
        }
        finally
        {
            _uiPreferencesStore.ClearStartupShareLoadMarker();
        }
    }

    private async void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsDialog dialog = new();
        dialog.ConfigureLocalIncomingMapping(
            _viewModel.EndpointText,
            _viewModel.LocalIncomingMappingText,
            mapping => _viewModel.LocalIncomingMappingText = mapping);
        dialog.ConfigureUiPreferences(
            _suppressCoreProfileSwitchConfirmation,
            _autoLoadShareFilesAtStartup,
            suppress =>
            {
                if (!_uiPreferencesStore.TrySave(
                        new UiPreferences(suppress, _autoLoadShareFilesAtStartup),
                        out string errorMessage))
                {
                    _viewModel.SetStatusMessage(
                        "Core-Wechsel-Rückfrage konnte nicht gespeichert werden: " + errorMessage);
                    return false;
                }

                _suppressCoreProfileSwitchConfirmation = suppress;
                _viewModel.SetStatusMessage(
                    suppress
                        ? "Manuelle Core-Profilwechsel erfolgen künftig ohne Rückfrage."
                        : "Rückfrage für manuelle Core-Profilwechsel ist wieder aktiv.");
                return true;
            },
            autoLoadShareFilesAtStartup =>
            {
                if (!_uiPreferencesStore.TrySave(
                        new UiPreferences(
                            _suppressCoreProfileSwitchConfirmation,
                            autoLoadShareFilesAtStartup),
                        out string errorMessage))
                {
                    _viewModel.SetStatusMessage(
                        "Share-Startload-Option konnte nicht gespeichert werden: " + errorMessage);
                    return false;
                }

                _autoLoadShareFilesAtStartup = autoLoadShareFilesAtStartup;
                if (!autoLoadShareFilesAtStartup)
                    _startupShareLoadEnabledForThisProcess = false;
                _viewModel.SetStatusMessage(
                    autoLoadShareFilesAtStartup
                        ? "Share-Dateiliste wird beim nächsten Programmstart nach der ersten Core-Verbindung automatisch geladen."
                        : "Share-Dateiliste wird beim Programmstart nicht automatisch geladen.");
                return true;
            });
        dialog.ConfigureCoreSettings(
            _viewModel.CoreNickValue,
            _viewModel.CoreIncomingDirectory,
            _viewModel.CoreTemporaryDirectory,
            _viewModel.CorePortValue,
            _viewModel.CoreXmlPortValue,
            _viewModel.CoreMaxConnections,
            _viewModel.CoreMaxDownloadKb,
            _viewModel.CoreMaxUploadKb,
            _viewModel.CoreSpeedPerSlot,
            _viewModel.CoreMaxSourcesPerFile,
            _viewModel.CoreMaxNewConnectionsPerTurn,
            _viewModel.CoreAutoConnect,
            _viewModel.IsConnected && !_viewModel.IsBusy,
            _viewModel.ApplyMaxConnectionsAsync,
            _viewModel.ApplyMaxDownloadAsync,
            _viewModel.ApplyUploadLimitsAsync,
            _viewModel.ApplyMaxSourcesPerFileAsync,
            _viewModel.ApplyMaxNewConnectionsPerTurnAsync,
            _viewModel.ApplyAutoConnectAsync,
            _viewModel.ApplyCoreNicknameAsync,
            _viewModel.ApplyCorePortAsync,
            _viewModel.ApplyCoreXmlPortAsync,
            _viewModel.LoadCoreDirectoryAsync,
            _viewModel.ApplyCoreIncomingDirectoryAsync,
            () => _viewModel.Downloads.Any(),
            _viewModel.ApplyCoreTemporaryDirectoryAsync,
            _viewModel.CheckCorePortReachabilityAsync,
            _viewModel.ChangeCorePasswordAsync);
        await dialog.ShowDialog<bool>(this);
    }

    private void ConfigureLocalIncomingMappingControls()
    {
        TextBox? mappingInput = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(input =>
                (input.PlaceholderText ?? string.Empty).StartsWith(
                    "Optional: lokales/gemountetes Abbild",
                    StringComparison.Ordinal));
        if (mappingInput is not null)
        {
            mappingInput.ClearValue(InputElement.IsEnabledProperty);
            mappingInput.IsEnabled = true;
            mappingInput.IsReadOnly = true;
        }

        Button? mappingButton = this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "Auswählen…", StringComparison.Ordinal));
        if (mappingButton is not null)
        {
            mappingButton.ClearValue(InputElement.IsEnabledProperty);
            mappingButton.IsEnabled = true;
        }
    }

    private async void BrowseLocalIncomingMappingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
            return;

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Lokales Incoming-Mapping auswählen",
                AllowMultiple = false
            });

        if (folders.Count == 0)
            return;

        Uri path = folders[0].Path;
        if (!path.IsFile)
            return;

        _viewModel.LocalIncomingMappingText = path.LocalPath;
        _localIncomingMappingStore.TrySave(_viewModel.EndpointText, path.LocalPath, out _);
    }

    private void ShareContextOpenWithVlc_OnClick(object? sender, RoutedEventArgs e)
    {
        List<AjShareFile> shares = GetSelectedShareFilesForContext();
        if (shares.Count != 1)
        {
            SetExternalVlcRuntimeStatus("genau eine Share-Datei markieren");
            return;
        }

        ExternalVlcConfiguration configuration = _externalVlcConfigurationStore.Load();
        if (!configuration.Enabled
            || string.IsNullOrWhiteSpace(configuration.ExecutablePath)
            || !File.Exists(configuration.ExecutablePath))
        {
            SetExternalVlcRuntimeStatus("VLC ist deaktiviert oder nicht erreichbar");
            return;
        }

        AjShareFile share = shares[0];
        if (!ShareMediaPathSemantics.IsPlausibleMediaFileName(share.Filename))
        {
            SetExternalVlcRuntimeStatus("kein unterstütztes Audio-/Videoformat");
            return;
        }

        if (!TryResolveShareMediaFile(share, out string localFile))
        {
            SetExternalVlcRuntimeStatus("Datei über Incoming-Mapping nicht erreichbar");
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = configuration.ExecutablePath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(localFile);
            Process.Start(startInfo);
            SetExternalVlcRuntimeStatus("VLC gestartet");
        }
        catch (Exception ex)
        {
            SetExternalVlcRuntimeStatus("VLC-Start fehlgeschlagen: " + ex.Message);
        }
    }

    private bool TryResolveShareMediaFile(AjShareFile share, out string localFile)
    {
        localFile = string.Empty;
        if (!ShareMediaPathSemantics.TryGetRelativePathBelowIncoming(
                _viewModel.CoreIncomingDirectory,
                share.Filename,
                out string relativePath))
        {
            return false;
        }

        string coreIncoming = _viewModel.CoreIncomingDirectory.Trim().Trim('"');
        string mapping = _viewModel.LocalIncomingMappingText.Trim().Trim('"');
        string root = Directory.Exists(coreIncoming)
            ? coreIncoming
            : Directory.Exists(mapping)
                ? mapping
                : string.Empty;
        if (root.Length == 0)
            return false;

        try
        {
            string fullRoot = Path.GetFullPath(root);
            string candidate = fullRoot;
            foreach (string part in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
                candidate = Path.Combine(candidate, part);

            candidate = Path.GetFullPath(candidate);
            string relativeCheck = Path.GetRelativePath(fullRoot, candidate);
            if (relativeCheck == ".."
                || relativeCheck.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relativeCheck)
                || !File.Exists(candidate))
            {
                return false;
            }

            localFile = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SetExternalVlcRuntimeStatus(string text)
        => _viewModel.SetStatusMessage(text);

    private async void PauseDownloadButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.PauseSelectedDownloadAsync();

    private async void ResumeDownloadButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ResumeSelectedDownloadAsync();

    private async void CancelDownloadButton_OnClick(object? sender, RoutedEventArgs e)
        => await ConfirmAndCancelSelectedDownloadAsync();

    private async void SearchButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.StartSearchAsync();

    private async void RemoveSearchButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.RemoveSelectedSearchAsync();

    private async void ShareSnapshotDiffButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsConnected || _viewModel.IsBusy)
            return;

        List<AjShareFile> shares = _viewModel.Shares.ToList();
        if (shares.Count == 0)
        {
            _viewModel.SetStatusMessage(
                "Share-Vergleich: Share-Dateiliste ist leer oder noch nicht manuell geladen. Bitte zuerst 'Neu laden' verwenden.");
            return;
        }

        CoreEndpoint endpoint;
        try
        {
            endpoint = CoreEndpoint.Parse(_viewModel.EndpointText);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage(
                "Share-Vergleich: Core-Endpunkt konnte nicht bestimmt werden: " + ex.Message);
            return;
        }

        List<ShareSnapshotSourceFile> fileSource = shares
            .Select(share => new ShareSnapshotSourceFile(share.Filename, share.Size))
            .ToList();
        List<ShareSnapshotSourceRoot> rootSource = _viewModel.ConfiguredShareDirectories
            .Select(directory => new ShareSnapshotSourceRoot(directory.Name, directory.ShareMode))
            .ToList();

        _viewModel.SetStatusMessage(
            $"Share-Vergleich wird lokal erstellt: {fileSource.Count:N0} Dateien ...");

        try
        {
            ShareSnapshotDocument currentSnapshot = await Task.Run(() =>
                ShareSnapshotService.CreateSnapshot(
                    endpoint.Host,
                    endpoint.BaseUri.Port,
                    fileSource,
                    rootSource));

            ShareSnapshotLoadResult loadResult = await ShareSnapshotService.LoadAsync(
                endpoint.Host,
                endpoint.BaseUri.Port);

            ShareSnapshotComparisonReport report = await Task.Run(() =>
                ShareSnapshotService.Compare(
                    currentSnapshot,
                    loadResult.Snapshot,
                    loadResult.StoragePath));

            ShareSnapshotDiffDialog dialog = new(
                report,
                currentSnapshot,
                loadResult.ErrorMessage);

            bool baselineSaved = await dialog.ShowDialog<bool>(this);
            _viewModel.SetStatusMessage(
                baselineSaved
                    ? "Share-Vergleich: aktueller Stand wurde als lokale Vergleichsbasis gespeichert."
                    : loadResult.HasError
                        ? "Share-Vergleich geschlossen. Die vorhandene Vergleichsbasis konnte nicht gelesen werden: "
                          + loadResult.ErrorMessage
                        : $"Share-Vergleich geschlossen: {report.TotalChangeCount:N0} Änderung(en) zur lokalen Vergleichsbasis.");
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage(
                "Share-Vergleich konnte nicht erstellt werden: " + ex.Message);
        }
    }

    private async void ReloadSharesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ReloadSharesAsync();
        await ApplyShareFilterAsync(_appliedShareFilter);
    }

    private async void ShareFilterInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await ApplyShareFilterFromInputAsync();
    }

    private async void ShareFilterApplyButton_OnClick(object? sender, RoutedEventArgs e)
        => await ApplyShareFilterFromInputAsync();

    private async void ShareFilterClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? input = this.FindControl<TextBox>("ShareFilterInput");
        if (input is not null)
            input.Text = string.Empty;

        await ApplyShareFilterAsync(string.Empty);
    }

    private async Task ApplyShareFilterFromInputAsync()
    {
        TextBox? input = this.FindControl<TextBox>("ShareFilterInput");
        await ApplyShareFilterAsync(input?.Text);
    }

    private async Task ApplyShareFilterAsync(string? filterText)
    {
        string filter = (filterText ?? string.Empty).Trim();
        _appliedShareFilter = filter;
        int requestVersion = ++_shareFilterRequestVersion;

        List<AjShareFile> allShares = _viewModel.Shares.ToList();
        if (filter.Length == 0)
        {
            _viewModel.SetVisibleSharesOverride(null);
            UpdateShareFilterSummary(allShares.Count, allShares.Count);
            return;
        }

        List<AjShareFile> visibleShares = allShares.Count >= 5000
            ? await Task.Run(() => FilterShareFiles(allShares, filter))
            : FilterShareFiles(allShares, filter);

        if (requestVersion != _shareFilterRequestVersion)
            return;

        _viewModel.SetVisibleSharesOverride(visibleShares);
        UpdateShareFilterSummary(visibleShares.Count, allShares.Count);
    }

    private static List<AjShareFile> FilterShareFiles(
        IReadOnlyList<AjShareFile> shares,
        string filter)
        => shares
            .Where(share =>
                ContainsShareFilterText(share.DisplayFilename, filter)
                || ContainsShareFilterText(share.DirectoryPath, filter)
                || ContainsShareFilter(share.FileType, filter)
                || ContainsShareFilter(share.Checksum, filter))
            .ToList();

    private static bool ContainsShareFilter(string? value, string filter)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsShareFilterText(string? value, string filter)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(filter))
            return false;

        if (value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!filter.Any(IsShareFilterSeparator))
            return false;

        for (int start = 0; start < value.Length; start++)
        {
            if (ShareFilterMatchesAt(value, filter, start))
                return true;
        }

        return false;
    }

    private static bool ShareFilterMatchesAt(string value, string filter, int valueStart)
    {
        int valueIndex = valueStart;
        int filterIndex = 0;

        while (filterIndex < filter.Length && valueIndex < value.Length)
        {
            bool filterSeparator = IsShareFilterSeparator(filter[filterIndex]);
            if (filterSeparator)
            {
                if (!IsShareFilterSeparator(value[valueIndex]))
                    return false;

                while (filterIndex < filter.Length && IsShareFilterSeparator(filter[filterIndex]))
                    filterIndex++;
                while (valueIndex < value.Length && IsShareFilterSeparator(value[valueIndex]))
                    valueIndex++;
                continue;
            }

            if (char.ToUpperInvariant(value[valueIndex]) != char.ToUpperInvariant(filter[filterIndex]))
                return false;

            valueIndex++;
            filterIndex++;
        }

        return filterIndex == filter.Length;
    }

    private static bool IsShareFilterSeparator(char value)
        => value == '.' || value == '_' || char.IsWhiteSpace(value);

    private void UpdateShareFilterSummary(int visibleCount, int totalCount)
    {
        TextBlock? summary = this.FindControl<TextBlock>("ShareFilterSummaryText");
        if (summary is not null)
            summary.Text = $"{visibleCount:N0} / {totalCount:N0} sichtbar";
    }

    private async void ShareDirectoriesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsConnected || _viewModel.IsBusy)
            return;

        ShareDirectoryDialog dialog = new(
            _viewModel.ConfiguredShareDirectories,
            _viewModel.Shares.ToList(),
            _viewModel.LoadCoreDirectoryAsync,
            _viewModel.TransferShareDirectoriesAsync);
        await dialog.ShowDialog<bool>(this);
    }

    private async void ShareContextSetPriority_OnClick(object? sender, RoutedEventArgs e)
    {
        List<AjShareFile> shares = GetSelectedShareFilesForContext();
        if (shares.Count == 0 || !_viewModel.IsConnected || _viewModel.IsBusy)
            return;

        TextPromptDialog dialog = new(
            "Share-Priorität setzen",
            "Priorität 1 bis 250:",
            shares[0].Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
            shares.Count == 1
                ? "Werte außerhalb des Bereichs werden wie im produktiven AJCC auf 1 bis 250 begrenzt."
                : $"{shares.Count:N0} markierte Share-Dateien erhalten dieselbe Priorität. Werte werden auf 1 bis 250 begrenzt.",
            "Setzen");

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.SetSharePriorityAsync(shares, dialog.TextValue);
    }

    private async void ShareContextResetAllPriorities_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsConnected || _viewModel.IsBusy)
            return;

        List<AjShareFile> shares = _viewModel.Shares.ToList();
        if (shares.Count == 0)
            return;

        ConfirmDialog dialog = new(
            "Alle Share-Prioritäten zurücksetzen",
            $"Priorität von {shares.Count:N0} Share-Dateien auf 1 setzen?",
            "Auf 1 setzen",
            "Abbrechen");

        bool confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed)
            await _viewModel.ResetAllSharePrioritiesAsync();
    }

    private async void ShareContextCopyAjfsp_OnClick(object? sender, RoutedEventArgs e)
        => await CopySelectedShareValuesAsync(share => _viewModel.BuildShareAjfspLink(share, includeOwnSource: false));

    private async void ShareContextCopyAjfspWithSource_OnClick(object? sender, RoutedEventArgs e)
        => await CopySelectedShareValuesAsync(share => _viewModel.BuildShareAjfspLink(share, includeOwnSource: true));


    private async void ShareContextExportAjl_OnClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<AjShareFile> shares =
            AjLegacyLinkListBuilder.PrepareShareExport(GetSelectedShareFilesForContext());
        if (shares.Count == 0)
            return;

        FilePickerSaveOptions options = new()
        {
            Title = "AppleJuice-Linkliste (.ajl) exportieren",
            SuggestedFileName = BuildDefaultAjlExportFileName(shares),
            DefaultExtension = "ajl",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("AppleJuice-Linkliste")
                {
                    Patterns = new[] { "*.ajl" }
                }
            }
        };

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(options);
        if (file is null)
            return;

        await using Stream stream = await file.OpenWriteAsync();
        stream.SetLength(0);

        await using StreamWriter writer = new(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(AjLegacyLinkListBuilder.BuildLegacyContent(shares));
    }

    private static string BuildDefaultAjlExportFileName(IReadOnlyList<AjShareFile> selectedShares)
    {
        if (selectedShares.Count == 1)
        {
            string baseName = Path.GetFileNameWithoutExtension(selectedShares[0].DisplayFilename.Trim());
            baseName = SanitizeFileNameForExport(baseName);
            if (!string.IsNullOrWhiteSpace(baseName))
                return baseName + ".ajl";
        }

        return $"ApplejuiceControlCenter_ShareExport_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.ajl";
    }

    private static string SanitizeFileNameForExport(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);
        foreach (char c in value.Trim())
            builder.Append(invalidChars.Contains(c) ? '_' : c);

        string result = builder.ToString().Trim(' ', '.');
        return result.Length > 80 ? result[..80] : result;
    }

    private async void ShareContextCopyFilename_OnClick(object? sender, RoutedEventArgs e)
        => await CopySelectedShareValuesAsync(share => share.DisplayFilename);

    private async void ShareContextCopyPath_OnClick(object? sender, RoutedEventArgs e)
        => await CopySelectedShareValuesAsync(share => share.DirectoryPath);

    private async void ShareContextCopyChecksum_OnClick(object? sender, RoutedEventArgs e)
        => await CopySelectedShareValuesAsync(share => share.Checksum);

    private async void ShareContextCopyId_OnClick(object? sender, RoutedEventArgs e)
        => await CopySelectedShareValuesAsync(share => share.Id.ToString());

    private async Task CopySelectedShareValuesAsync(Func<AjShareFile, string?> selector)
    {
        List<string> values = GetSelectedShareFilesForContext()
            .Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();

        if (values.Count == 0)
            return;

        await CopyTextAsync(string.Join(Environment.NewLine, values));
    }

    private List<AjShareFile> GetSelectedShareFilesForContext()
    {
        ListBox? sharesList = this.FindControl<ListBox>("SharesList");
        List<AjShareFile> selectedShares = sharesList?.SelectedItems?
            .OfType<AjShareFile>()
            .Where(share => share.Id > 0)
            .GroupBy(share => share.Id)
            .Select(group => group.First())
            .ToList()
            ?? new List<AjShareFile>();

        if (selectedShares.Count == 0 && _selectedShareForContext is { Id: > 0 } share)
            selectedShares.Add(share);

        return selectedShares;
    }

    private void SelectShareForContext(AjShareFile share)
    {
        _selectedShareForContext = share;

        ListBox? sharesList = this.FindControl<ListBox>("SharesList");
        if (sharesList?.SelectedItems is not { } selectedItems)
            return;

        if (selectedItems.OfType<AjShareFile>().Any(selected => selected.Id == share.Id))
            return;

        selectedItems.Clear();
        selectedItems.Add(share);
    }

    private async void DownloadSearchResultButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.DownloadSelectedSearchEntryAsync();

    private void MainWindow_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control source)
            return;

        var currentPoint = e.GetCurrentPoint(this);
        if (currentPoint.Properties.IsLeftButtonPressed
            && source.DataContext is AjShareFile dragShare)
        {
            var position = e.GetPosition(this);
            _shareDragCandidate = dragShare;
            _shareDragTriggerEvent = e;
            _shareDragStartX = position.X;
            _shareDragStartY = position.Y;
        }
        else if (!currentPoint.Properties.IsRightButtonPressed)
        {
            ResetShareDragState();
        }

        if (!currentPoint.Properties.IsRightButtonPressed)
            return;

        switch (source.DataContext)
        {
            case AjDownload download:
                _viewModel.SelectedDownload = download;
                break;
            case AjUserSource userSource:
                _selectedDownloadSourceForContext = userSource;
                break;
            case AjShareFile share:
                SelectShareForContext(share);
                break;
            case AjSearchEntry entry:
                _viewModel.SelectedSearchEntry = entry;
                break;
            case AjServer server:
                _selectedServerForContext = server;
                break;
        }
    }

    private async void MainWindow_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_shareDragInProgress
            || _shareDragCandidate is not { Id: > 0 } candidate
            || _shareDragTriggerEvent is not { } triggerEvent)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetShareDragState();
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _shareDragStartX) < ShareDragThreshold
            && Math.Abs(position.Y - _shareDragStartY) < ShareDragThreshold)
        {
            return;
        }

        ListBox? sharesList = this.FindControl<ListBox>("SharesList");
        List<AjShareFile> selectedShares = sharesList?.SelectedItems?
            .OfType<AjShareFile>()
            .Where(share => share.Id > 0)
            .GroupBy(share => share.Id)
            .Select(group => group.First())
            .ToList()
            ?? new List<AjShareFile>();

        if (!selectedShares.Any(share => share.Id == candidate.Id))
        {
            selectedShares.Clear();
            selectedShares.Add(candidate);
        }

        string text = ShareAjfspDragExportSemantics.BuildPlainTextLinkList(selectedShares);
        ResetShareDragState();
        if (string.IsNullOrWhiteSpace(text))
            return;

        _shareDragInProgress = true;
        try
        {
            DataTransfer data = new();
            data.Add(DataTransferItem.CreateText(text));
#pragma warning disable CS0618
            await DragDrop.DoDragDropAsync(triggerEvent, data, DragDropEffects.Copy);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage("AJFSP-Drag konnte nicht gestartet werden: " + ex.Message);
        }
        finally
        {
            _shareDragInProgress = false;
        }
    }

    private void MainWindow_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        => ResetShareDragState();

    private void ResetShareDragState()
    {
        _shareDragCandidate = null;
        _shareDragTriggerEvent = null;
    }

    private void MainWindow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control source)
            return;

        ListBoxItem? item = source.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item is null)
            return;

        bool isDownloadRow;
        switch (item.DataContext)
        {
            case AjDownload download:
                _viewModel.SelectedDownload = download;
                isDownloadRow = true;
                break;
            case AjUserSource userSource:
                _selectedDownloadSourceForContext = userSource;
                isDownloadRow = false;
                break;
            case AjShareFile share:
                SelectShareForContext(share);
                isDownloadRow = false;
                break;
            case AjSearchEntry entry:
                _viewModel.SelectedSearchEntry = entry;
                isDownloadRow = false;
                break;
            case AjServer server:
                _selectedServerForContext = server;
                isDownloadRow = false;
                break;
            default:
                return;
        }

        foreach (var descendant in item.GetVisualDescendants())
        {
            if (descendant is not Control { ContextMenu: { } menu } contextHost)
                continue;

            if (isDownloadRow)
                ConfigureDownloadContextMenu(menu);

            menu.Open(contextHost);
            e.Handled = true;
            return;
        }
    }

    private void ConfigureDownloadContextMenu(ContextMenu menu)
    {
        bool canPause = _viewModel.CanPauseSelectedDownload;
        bool canResume = _viewModel.CanResumeSelectedDownload;
        bool canCancel = _viewModel.CanCancelSelectedDownload;
        bool canClean = _viewModel.CanCleanDownloadList;
        bool canRename = _viewModel.CanRenameSelectedDownload;
        bool canSetTargetDirectory = _viewModel.CanSetTargetDirectorySelectedDownload;
        bool canSetPowerDownload = _viewModel.CanSetPowerDownloadSelectedDownload;
        bool canShowPartList = _viewModel.CanShowSelectedDownloadPartList;
        bool hasControlActions = canPause || canResume || canCancel || canClean;
        bool hasMetadataActions = canRename || canSetTargetDirectory || canSetPowerDownload || canShowPartList;
        int separatorIndex = 0;

        foreach (object? rawItem in menu.Items)
        {
            if (rawItem is MenuItem item)
            {
                string header = item.Header?.ToString() ?? string.Empty;
                item.IsVisible = header switch
                {
                    "Pausieren" => canPause,
                    "Fortsetzen" => canResume,
                    "Download abbrechen…" => canCancel,
                    "Fertige/abgebrochene Downloads entfernen" => canClean,
                    "Umbenennen…" => canRename,
                    "Zielverzeichnis setzen…" => canSetTargetDirectory,
                    "Powerdownload setzen…" => canSetPowerDownload,
                    "Powerdownload löschen" => canSetPowerDownload,
                    "Partliste anzeigen…" => canShowPartList,
                    _ => true
                };
                continue;
            }

            if (rawItem is Separator separator)
            {
                separator.IsVisible = separatorIndex switch
                {
                    0 => hasControlActions && hasMetadataActions,
                    1 => hasControlActions || hasMetadataActions,
                    _ => true
                };
                separatorIndex++;
            }
        }
    }

    private void DownloadRow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: AjDownload download })
            _viewModel.SelectedDownload = download;
    }

    private void SearchResultRow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: AjSearchEntry entry })
            _viewModel.SelectedSearchEntry = entry;
    }

    private void ServerRow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: AjServer server })
            _selectedServerForContext = server;
    }

    private async void MoreServersButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ImportMoreServersAsync();

    private async void ServerContextLogin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedServerForContext is not { } server || !_viewModel.IsConnected || _viewModel.IsBusy)
            return;

        bool rapidSwitchConfirmed = false;
        var evaluation = _viewModel.EvaluateServerLogin(server);
        if (evaluation.RequiresConfirmation)
        {
            int waitMinutes = Math.Max(1, (int)Math.Ceiling(evaluation.RecommendedWait.TotalMinutes));
            string connectedForText = evaluation.ConnectedFor.TotalMinutes >= 1
                ? $"{(int)evaluation.ConnectedFor.TotalMinutes} Min. {evaluation.ConnectedFor.Seconds} Sek."
                : $"{Math.Max(0, (int)evaluation.ConnectedFor.TotalSeconds)} Sek.";

            ConfirmDialog dialog = new(
                "Serverwechsel-Warnung",
                "Der Core ist noch keine 30 Minuten mit dem aktuellen Server verbunden.\n\n" +
                $"Zielserver: {server.Name} / ID {server.Id}\n" +
                $"Aktuelle Verbindungsdauer: {connectedForText}\n\n" +
                "Zu viele Serverwechsel in kurzer Zeit können eine 30-Minuten-Reconnect-Sperre auslösen.\n" +
                $"Empfehlung: noch ca. {waitMinutes} Minuten warten.\n\n" +
                "Trotzdem jetzt einen Serverlogin versuchen?",
                "Trotzdem verbinden",
                "Abbrechen");

            bool confirmed = await dialog.ShowDialog<bool>(this);
            if (!confirmed)
                return;

            rapidSwitchConfirmed = true;
        }

        await _viewModel.LoginServerAsync(server, rapidSwitchConfirmed);
    }

    private async void ServerContextRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedServerForContext is { } server)
            await _viewModel.RemoveServerAsync(server);
    }

    private async void DownloadContextPause_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.PauseSelectedDownloadAsync();

    private async void DownloadContextResume_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ResumeSelectedDownloadAsync();

    private async void DownloadContextCancel_OnClick(object? sender, RoutedEventArgs e)
        => await ConfirmAndCancelSelectedDownloadAsync();

    private async void DownloadContextClean_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.CleanTerminalDownloadsAsync();

    private async void DownloadContextRename_OnClick(object? sender, RoutedEventArgs e)
        => await RenameSelectedDownloadWithDialogAsync();

    private async void DownloadContextSetTargetDirectory_OnClick(object? sender, RoutedEventArgs e)
        => await SetSelectedDownloadTargetDirectoryWithDialogAsync();

    private async void DownloadContextSetPowerDownload_OnClick(object? sender, RoutedEventArgs e)
        => await SetSelectedDownloadPowerDownloadWithDialogAsync();

    private async void DownloadContextClearPowerDownload_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ClearSelectedDownloadPowerDownloadAsync();

    private async void DownloadContextShowPartList_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await _viewModel.LoadSelectedDownloadPartListAsync();
        if (!result.HasValue)
            return;

        var partList = result.Value;
        PartListDialog dialog = new(
            partList.Filename,
            partList.FileSize,
            partList.Parts,
            partList.SourcePartListCount,
            partList.SourceCandidateCount,
            partList.SourceErrorCount);
        await dialog.ShowDialog<bool>(this);
    }

    private async void DownloadsList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ClearDownloadSourceSelection();

        int requestVersion = ++_embeddedPartListRequestVersion;
        AjDownload? selected = (sender as ListBox)?.SelectedItem as AjDownload;
        _viewModel.SelectedDownload = selected;
        if (selected is null)
        {
            ClearEmbeddedPartList("Markiere einen Download, um die Partliste zu laden.");
            return;
        }

        if (_embeddedPartListDownloadId == selected.Id)
            return;

        _embeddedPartListDownloadId = 0;
        ClearEmbeddedPartList("Partliste wird geladen…");
        await Task.Delay(140);
        if (requestVersion != _embeddedPartListRequestVersion)
            return;

        await LoadEmbeddedPartListAsync(requestVersion);
    }

    private async void DownloadRow_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: AjDownload download }
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ListBox? sourceList = this.FindControl<ListBox>("DownloadSourcesList");
        bool sourcePartListIsActive =
            _embeddedPartListSourceId != 0 || sourceList?.SelectedItem is AjUserSource;
        if (!sourcePartListIsActive || _viewModel.SelectedDownload?.Id != download.Id)
            return;

        ClearDownloadSourceSelection();
        int requestVersion = ++_embeddedPartListRequestVersion;
        _embeddedPartListDownloadId = 0;
        ClearEmbeddedPartList("Partliste wird geladen…");
        await LoadEmbeddedPartListAsync(requestVersion);
    }

    private void ClearDownloadSourceSelection()
    {
        ListBox? sourceList = this.FindControl<ListBox>("DownloadSourcesList");
        _suppressDownloadSourceSelectionChanged = true;
        try
        {
            if (sourceList is not null)
                sourceList.SelectedItem = null;
        }
        finally
        {
            _suppressDownloadSourceSelectionChanged = false;
        }

        _selectedDownloadSourceForContext = null;
        _embeddedPartListSourceId = 0;
    }

    private async void DownloadSourcesList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressDownloadSourceSelectionChanged)
            return;

        AjUserSource? source = (sender as ListBox)?.SelectedItem as AjUserSource;
        _selectedDownloadSourceForContext = source;
        if (source is null)
        {
            if (_embeddedPartListSourceId == 0 || _viewModel.SelectedDownload is null)
                return;

            int fallbackRequestVersion = ++_embeddedPartListRequestVersion;
            _embeddedPartListSourceId = 0;
            _embeddedPartListDownloadId = 0;
            ClearEmbeddedPartList("Partliste wird geladen…");
            await LoadEmbeddedPartListAsync(fallbackRequestVersion);
            return;
        }

        if (_embeddedPartListSourceId == source.Id)
            return;

        int requestVersion = ++_embeddedPartListRequestVersion;
        _embeddedPartListDownloadId = 0;
        _embeddedPartListSourceId = 0;
        ClearEmbeddedPartList("Quellen-Partliste wird geladen…");

        await Task.Delay(80);
        if (requestVersion != _embeddedPartListRequestVersion)
            return;

        await LoadEmbeddedSourcePartListAsync(source, requestVersion);
    }

    private async void EmbeddedPartListRefresh_OnClick(object? sender, RoutedEventArgs e)
    {
        int requestVersion = ++_embeddedPartListRequestVersion;
        if (this.FindControl<ListBox>("DownloadSourcesList")?.SelectedItem is AjUserSource source)
        {
            _embeddedPartListSourceId = 0;
            await LoadEmbeddedSourcePartListAsync(source, requestVersion);
            return;
        }

        _embeddedPartListDownloadId = 0;
        _embeddedPartListSourceId = 0;
        await LoadEmbeddedPartListAsync(requestVersion);
    }

    private async Task LoadEmbeddedPartListAsync(int requestVersion)
    {
        if (!await WaitForPartListIdleAsync(requestVersion))
            return;

        if (!_viewModel.CanShowSelectedDownloadPartList)
        {
            ClearEmbeddedPartList(
                _viewModel.SelectedDownload is null
                    ? "Markiere einen Download, um die Partliste zu laden."
                    : "Für diesen Download ist keine Partliste verfügbar.");
            return;
        }

        var result = await _viewModel.LoadSelectedDownloadPartListAsync();
        if (requestVersion != _embeddedPartListRequestVersion)
            return;

        if (!result.HasValue)
        {
            ClearEmbeddedPartList("Partliste konnte nicht geladen werden.");
            return;
        }

        var partList = result.Value;
        WrapPanel? segmentsPanel = this.FindControl<WrapPanel>("EmbeddedPartListSegmentsPanel");
        TextBlock? summaryText = this.FindControl<TextBlock>("EmbeddedPartListSummaryText");
        if (segmentsPanel is null || summaryText is null)
            return;

        segmentsPanel.Children.Clear();
        List<PartListDialog.VisualSegment> segments =
            PartListDialog.BuildVisualSegments(partList.Parts, partList.FileSize);
        foreach (PartListDialog.VisualSegment segment in segments)
        {
            segmentsPanel.Children.Add(new Border
            {
                Width = 8,
                Height = 10,
                Margin = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = PartListDialog.BrushForType(segment.Type)
            });
        }

        string sourceSummary = partList.SourceCandidateCount <= 0
            ? "keine Quellenpartlisten"
            : $"Quellenpartlisten {partList.SourcePartListCount:N0}/{partList.SourceCandidateCount:N0}";
        if (partList.SourceErrorCount > 0)
            sourceSummary += $", Fehler {partList.SourceErrorCount:N0}";

        summaryText.Text = $"Aggregierte Partliste · {segments.Count:N0} Blöcke · {sourceSummary}";
        _embeddedPartListDownloadId = _viewModel.SelectedDownload?.Id ?? 0;
        _embeddedPartListSourceId = 0;
    }

    private async Task LoadEmbeddedSourcePartListAsync(AjUserSource source, int requestVersion)
    {
        if (!await WaitForPartListIdleAsync(requestVersion))
            return;

        var result = await _viewModel.LoadDownloadSourcePartListAsync(source);
        if (requestVersion != _embeddedPartListRequestVersion)
            return;

        if (!result.HasValue)
        {
            ClearEmbeddedPartList("Quellen-Partliste konnte nicht geladen werden.");
            return;
        }

        var partList = result.Value;
        WrapPanel? segmentsPanel = this.FindControl<WrapPanel>("EmbeddedPartListSegmentsPanel");
        TextBlock? summaryText = this.FindControl<TextBlock>("EmbeddedPartListSummaryText");
        if (segmentsPanel is null || summaryText is null)
            return;

        segmentsPanel.Children.Clear();
        List<PartListDialog.VisualSegment> segments =
            PartListDialog.BuildVisualSegments(partList.Parts, partList.FileSize);
        foreach (PartListDialog.VisualSegment segment in segments)
        {
            segmentsPanel.Children.Add(new Border
            {
                Width = 8,
                Height = 10,
                Margin = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = PartListDialog.BrushForType(segment.Type == 1 ? -1 : segment.Type)
            });
        }

        summaryText.Text =
            $"Partliste Quelle · {partList.SourceName} · {partList.Filename} · {segments.Count:N0} Blöcke · Grün verfügbar · Blau wird geladen · Schwarz nicht verfügbar";
        _embeddedPartListDownloadId = 0;
        _embeddedPartListSourceId = source.Id;
    }

    private async Task<bool> WaitForPartListIdleAsync(int requestVersion)
    {
        while (_viewModel.IsBusy)
        {
            await Task.Delay(50);
            if (requestVersion != _embeddedPartListRequestVersion)
                return false;
        }

        return requestVersion == _embeddedPartListRequestVersion;
    }

    private void ClearEmbeddedPartList(string message)
    {
        WrapPanel? segmentsPanel = this.FindControl<WrapPanel>("EmbeddedPartListSegmentsPanel");
        TextBlock? summaryText = this.FindControl<TextBlock>("EmbeddedPartListSummaryText");
        if (segmentsPanel is not null)
            segmentsPanel.Children.Clear();
        if (summaryText is not null)
            summaryText.Text = message;
        _embeddedPartListDownloadId = 0;
        _embeddedPartListSourceId = 0;
    }

    private async void SearchResultContextDownload_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.DownloadSelectedSearchEntryAsync();

    private async Task ConfirmAndCancelSelectedDownloadAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanCancelSelectedDownload)
            return;

        ConfirmDialog dialog = new(
            "Download abbrechen",
            $"Download abbrechen?\n\n{download.DisplayFilename}",
            "Abbrechen",
            "Zurück");

        bool confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed)
            await _viewModel.CancelSelectedDownloadAsync();
    }

    private async Task RenameSelectedDownloadWithDialogAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanRenameSelectedDownload)
            return;

        TextPromptDialog dialog = new(
            "Download umbenennen",
            "Neuer Dateiname:",
            download.DisplayFilename,
            "Der Dateiname wird an den verbundenen Core übergeben.",
            "Umbenennen");

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.RenameSelectedDownloadAsync(dialog.TextValue);
    }

    private async Task SetSelectedDownloadPowerDownloadWithDialogAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanSetPowerDownloadSelectedDownload)
            return;

        double currentFactor = download.PowerDownload <= 0
            ? 2.2
            : AjDownload.PowerDownloadRawToFactor(download.PowerDownload);
        TextPromptDialog dialog = new(
            "Powerdownload setzen",
            "Faktor 2,2 bis 50,0:",
            currentFactor.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            "Punkt oder Komma sind erlaubt. Werte außerhalb des Core-Bereichs werden begrenzt.",
            "Setzen");

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.SetSelectedDownloadPowerDownloadAsync(dialog.TextValue);
    }

    private async Task SetSelectedDownloadTargetDirectoryWithDialogAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanSetTargetDirectorySelectedDownload)
            return;

        string incomingDirectory = _viewModel.CoreIncomingDirectory == "-"
            ? string.Empty
            : _viewModel.CoreIncomingDirectory;
        TargetDirectoryDialog dialog = new(download.TargetDirectory, incomingDirectory, _viewModel.LoadCoreDirectoryAsync);

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.SetSelectedDownloadTargetDirectoryAsync(dialog.TargetDirectory, dialog.SelectedFromCoreBrowser);
    }

    private async void DownloadContextCopyAjfsp_OnClick(object? sender, RoutedEventArgs e)
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || string.IsNullOrWhiteSpace(download.Hash) || download.Size <= 0)
            return;

        string link = AjfspLinkBuilder.BuildFileLink(download.DisplayFilename, download.Hash, download.Size);
        await CopyTextAsync(link);
    }

    private async void DownloadContextCopyAjfspWithSource_OnClick(object? sender, RoutedEventArgs e)
    {
        string link = _viewModel.BuildSelectedDownloadAjfspLinkWithSource();
        if (!string.IsNullOrWhiteSpace(link))
            await CopyTextAsync(link);
    }

    private async void DownloadContextCopyFilename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDownload is { } download)
            await CopyTextAsync(download.DisplayFilename);
    }

    private async void DownloadContextCopyHash_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDownload is { } download)
            await CopyTextAsync(download.Hash);
    }

    private async void DownloadContextCopyId_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDownload is { } download)
            await CopyTextAsync(download.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async void DownloadSourceContextCopyNick_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedDownloadSourceForContext is { } source)
            await CopyTextAsync(source.NicknameText);
    }

    private async void DownloadSourceContextCopyFilename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedDownloadSourceForContext is { } source)
            await CopyTextAsync(source.Filename);
    }

    private async void DownloadSourceContextCopyDownloadId_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedDownloadSourceForContext is { } source)
            await CopyTextAsync(source.DownloadId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async void SearchResultContextCopyAjfsp_OnClick(object? sender, RoutedEventArgs e)
    {
        AjSearchEntry? entry = _viewModel.SelectedSearchEntry;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Checksum) || entry.Size <= 0)
            return;

        string link = AjfspLinkBuilder.BuildFileLink(entry.Filename, entry.Checksum, entry.Size);
        await CopyTextAsync(link);
    }

    private async void SearchResultContextCopyFilename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSearchEntry is { } entry)
            await CopyTextAsync(entry.Filename);
    }

    private async void SearchResultContextCopySize_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSearchEntry is { } entry)
            await CopyTextAsync(entry.SizeText);
    }

    private async void SearchResultContextCopyChecksum_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSearchEntry is { } entry)
            await CopyTextAsync(entry.Checksum);
    }

    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
        => _viewModel.Dispose();
}
