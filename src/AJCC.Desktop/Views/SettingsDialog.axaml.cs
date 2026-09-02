using AJCC.Core.Models;
using AJCC.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace AJCC.Desktop.Views;

public sealed partial class SettingsDialog : Window
{
    private readonly ExternalVlcConfigurationStore _externalVlcConfigurationStore = new();
    private readonly DownloadQueueConfigurationStore _downloadQueueConfigurationStore = new();
    private readonly LocalIncomingMappingStore _localIncomingMappingStore = new();
    private bool _loadingExternalVlcConfiguration;
    private bool _loadingUiPreferences;
    private Func<bool, bool>? _applySuppressCoreSwitchConfirmation;
    private Func<bool, bool>? _applyAutoLoadShareFilesAtStartup;
    private Func<bool, bool>? _applyGuiSoundsEnabled;
    private string _mappingEndpoint = string.Empty;
    private Action<string>? _mappingChanged;
    private bool _autoLoadShareFilesAtStartup;
    private bool _guiSoundsEnabled = true;
    private bool _tabSoundReady;
    private Func<int, Task<int>>? _applyMaxConnectionsAsync;
    private Func<int, Task<int>>? _applyMaxSourcesPerFileAsync;
    private Func<int, Task<int>>? _applyMaxNewConnectionsPerTurnAsync;
    private Func<bool, Task<bool>>? _applyAutoConnectAsync;
    private Func<string, Task<string>>? _applyCoreNicknameAsync;
    private Func<int, Task<int>>? _applyCorePortAsync;
    private Func<Task<string>>? _checkCorePortReachabilityAsync;
    private Func<int, Task<int>>? _applyCoreXmlPortAsync;
    private Func<string?, Task<AjDirectoryListResult>>? _loadCoreDirectoryAsync;
    private Func<string, Task<string>>? _applyCoreIncomingDirectoryAsync;
    private Func<bool>? _hasCoreDownloads;
    private Func<string, Task<string>>? _applyCoreTemporaryDirectoryAsync;
    private Func<long, Task<long>>? _applyMaxDownloadAsync;
    private Func<long, int, Task<(long MaxUploadKb, int SpeedPerSlot)>>? _applyUploadLimitsAsync;
    private Func<string, Task<string>>? _changeCorePasswordAsync;
    private string _coreNickname = string.Empty;
    private string _coreIncomingDirectory = string.Empty;
    private string _coreTemporaryDirectory = string.Empty;
    private int _corePort = 8000;
    private int _coreXmlPort = 9851;
    private bool _coreSettingsWriteRunning;
    private bool _corePortReachabilityRunning;

    public SettingsDialog()
    {
        InitializeComponent();
        LoadExternalVlcConfiguration();
        LoadDownloadQueueConfiguration();
        Opened += (_, _) => _tabSoundReady = true;
    }

    public void ConfigureUiPreferences(
        bool suppressCoreSwitchConfirmation,
        bool autoLoadShareFilesAtStartup,
        bool guiSoundsEnabled,
        Func<bool, bool>? applySuppressCoreSwitchConfirmation,
        Func<bool, bool>? applyAutoLoadShareFilesAtStartup,
        Func<bool, bool>? applyGuiSoundsEnabled)
    {
        _applySuppressCoreSwitchConfirmation = applySuppressCoreSwitchConfirmation;
        _applyAutoLoadShareFilesAtStartup = applyAutoLoadShareFilesAtStartup;
        _applyGuiSoundsEnabled = applyGuiSoundsEnabled;
        _autoLoadShareFilesAtStartup = autoLoadShareFilesAtStartup;
        _guiSoundsEnabled = guiSoundsEnabled;

        CheckBox? suppressInput = this.FindControl<CheckBox>("SuppressCoreSwitchConfirmationCheckBox");
        CheckBox? autoLoadInput = this.FindControl<CheckBox>("AutoLoadShareFilesAtStartupCheckBox");
        CheckBox? guiSoundsInput = this.FindControl<CheckBox>("GuiSoundsEnabledCheckBox");

        _loadingUiPreferences = true;
        try
        {
            if (suppressInput is not null)
                suppressInput.IsChecked = suppressCoreSwitchConfirmation;
            if (autoLoadInput is not null)
                autoLoadInput.IsChecked = autoLoadShareFilesAtStartup;
            if (guiSoundsInput is not null)
                guiSoundsInput.IsChecked = guiSoundsEnabled;
        }
        finally
        {
            _loadingUiPreferences = false;
        }
    }

    private void SettingsTabControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_tabSoundReady
            || sender is not TabControl tabControl
            || !ReferenceEquals(e.Source, tabControl))
            return;

        AudioFeedbackService.PlayButtonTick();
    }

    public void ConfigureLocalIncomingMapping(
        string endpointText,
        string currentMapping,
        Action<string>? mappingChanged)
    {
        _mappingEndpoint = endpointText ?? string.Empty;
        _mappingChanged = mappingChanged;

        TextBlock? endpoint = this.FindControl<TextBlock>("MappingEndpointText");
        if (endpoint is not null)
            endpoint.Text = _mappingEndpoint;

        TextBox? mapping = this.FindControl<TextBox>("LocalIncomingMappingTextBox");
        if (mapping is not null)
            mapping.Text = currentMapping ?? string.Empty;
    }

    public void ConfigureCoreSettings(
        string nick,
        string incomingDirectory,
        string temporaryDirectory,
        int corePort,
        int xmlPort,
        int maxConnections,
        long maxDownloadKb,
        long maxUploadKb,
        int speedPerSlot,
        int maxSourcesPerFile,
        int maxNewConnectionsPerTurn,
        bool autoConnect,
        bool canWriteCoreSettings,
        Func<int, Task<int>>? applyMaxConnectionsAsync,
        Func<long, Task<long>>? applyMaxDownloadAsync,
        Func<long, int, Task<(long MaxUploadKb, int SpeedPerSlot)>>? applyUploadLimitsAsync,
        Func<int, Task<int>>? applyMaxSourcesPerFileAsync,
        Func<int, Task<int>>? applyMaxNewConnectionsPerTurnAsync,
        Func<bool, Task<bool>>? applyAutoConnectAsync,
        Func<string, Task<string>>? applyCoreNicknameAsync,
        Func<int, Task<int>>? applyCorePortAsync,
        Func<int, Task<int>>? applyCoreXmlPortAsync,
        Func<string?, Task<AjDirectoryListResult>>? loadCoreDirectoryAsync,
        Func<string, Task<string>>? applyCoreIncomingDirectoryAsync,
        Func<bool>? hasCoreDownloads,
        Func<string, Task<string>>? applyCoreTemporaryDirectoryAsync,
        Func<Task<string>>? checkCorePortReachabilityAsync,
        Func<string, Task<string>>? changeCorePasswordAsync)
    {
        _coreNickname = (nick ?? string.Empty).Trim();
        _coreIncomingDirectory = string.IsNullOrWhiteSpace(incomingDirectory)
            ? string.Empty
            : incomingDirectory.Trim();
        _coreTemporaryDirectory = string.IsNullOrWhiteSpace(temporaryDirectory)
            ? string.Empty
            : temporaryDirectory.Trim();
        TextBox? coreNickInput = this.FindControl<TextBox>("CoreNickTextBox");
        if (coreNickInput is not null)
            coreNickInput.Text = _coreNickname;
        SetCoreValue("CoreIncomingText", _coreIncomingDirectory);
        SetCoreValue("CoreTemporaryText", _coreTemporaryDirectory);
        _corePort = corePort is >= 1 and <= 65535 ? corePort : 8000;
        TextBox? corePortInput = this.FindControl<TextBox>("CorePortTextBox");
        if (corePortInput is not null)
            corePortInput.Text = _corePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _coreXmlPort = xmlPort;
        TextBox? coreXmlPortInput = this.FindControl<TextBox>("CoreXmlPortTextBox");
        if (coreXmlPortInput is not null)
            coreXmlPortInput.Text = _coreXmlPort.ToString(System.Globalization.CultureInfo.InvariantCulture);

        _applyMaxConnectionsAsync = applyMaxConnectionsAsync;
        _applyMaxSourcesPerFileAsync = applyMaxSourcesPerFileAsync;
        _applyMaxNewConnectionsPerTurnAsync = applyMaxNewConnectionsPerTurnAsync;
        _applyAutoConnectAsync = applyAutoConnectAsync;
        _applyCoreNicknameAsync = applyCoreNicknameAsync;
        _applyCorePortAsync = applyCorePortAsync;
        _checkCorePortReachabilityAsync = checkCorePortReachabilityAsync;
        _applyCoreXmlPortAsync = applyCoreXmlPortAsync;
        _loadCoreDirectoryAsync = loadCoreDirectoryAsync;
        _applyCoreIncomingDirectoryAsync = applyCoreIncomingDirectoryAsync;
        _hasCoreDownloads = hasCoreDownloads;
        _applyCoreTemporaryDirectoryAsync = applyCoreTemporaryDirectoryAsync;
        _applyMaxDownloadAsync = applyMaxDownloadAsync;
        _applyUploadLimitsAsync = applyUploadLimitsAsync;
        _changeCorePasswordAsync = changeCorePasswordAsync;

        TextBox? maxConnectionsInput = this.FindControl<TextBox>("CoreMaxConnectionsTextBox");
        if (maxConnectionsInput is not null)
            maxConnectionsInput.Text = Math.Max(0, maxConnections).ToString(System.Globalization.CultureInfo.InvariantCulture);

        TextBox? maxDownloadInput = this.FindControl<TextBox>("CoreMaxDownloadTextBox");
        if (maxDownloadInput is not null)
            maxDownloadInput.Text = Math.Max(0L, maxDownloadKb).ToString(System.Globalization.CultureInfo.InvariantCulture);

        TextBox? maxUploadInput = this.FindControl<TextBox>("CoreMaxUploadTextBox");
        if (maxUploadInput is not null)
            maxUploadInput.Text = Math.Max(0L, maxUploadKb).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateUploadSpeedPerSlotRange(speedPerSlot);

        TextBox? maxSourcesInput = this.FindControl<TextBox>("CoreMaxSourcesPerFileTextBox");
        if (maxSourcesInput is not null)
            maxSourcesInput.Text = Math.Max(0, maxSourcesPerFile).ToString(System.Globalization.CultureInfo.InvariantCulture);

        TextBox? maxNewConnectionsInput = this.FindControl<TextBox>("CoreMaxNewConnectionsPerTurnTextBox");
        if (maxNewConnectionsInput is not null)
            maxNewConnectionsInput.Text = maxNewConnectionsPerTurn.ToString(System.Globalization.CultureInfo.InvariantCulture);

        CheckBox? autoConnectInput = this.FindControl<CheckBox>("CoreAutoConnectCheckBox");
        if (autoConnectInput is not null)
            autoConnectInput.IsChecked = autoConnect;

        Button? applyCoreNickButton = this.FindControl<Button>("ApplyCoreNickButton");
        if (applyCoreNickButton is not null)
            applyCoreNickButton.IsEnabled = canWriteCoreSettings && _applyCoreNicknameAsync is not null;

        Button? changeCoreIncomingButton = this.FindControl<Button>("ChangeCoreIncomingButton");
        if (changeCoreIncomingButton is not null)
        {
            changeCoreIncomingButton.IsEnabled = canWriteCoreSettings
                && _loadCoreDirectoryAsync is not null
                && _applyCoreIncomingDirectoryAsync is not null;
        }

        Button? changeCoreTemporaryButton = this.FindControl<Button>("ChangeCoreTemporaryButton");
        if (changeCoreTemporaryButton is not null)
        {
            changeCoreTemporaryButton.IsEnabled = canWriteCoreSettings
                && _loadCoreDirectoryAsync is not null
                && _hasCoreDownloads is not null
                && _applyCoreTemporaryDirectoryAsync is not null;
        }

        Button? applyCorePortButton = this.FindControl<Button>("ApplyCorePortButton");
        if (applyCorePortButton is not null)
            applyCorePortButton.IsEnabled = canWriteCoreSettings && _applyCorePortAsync is not null;

        Button? checkCorePortButton = this.FindControl<Button>("CheckCorePortReachabilityButton");
        if (checkCorePortButton is not null)
            checkCorePortButton.IsEnabled = canWriteCoreSettings && _checkCorePortReachabilityAsync is not null;

        Button? applyCoreXmlPortButton = this.FindControl<Button>("ApplyCoreXmlPortButton");
        if (applyCoreXmlPortButton is not null)
            applyCoreXmlPortButton.IsEnabled = canWriteCoreSettings && _applyCoreXmlPortAsync is not null;

        Button? applyButton = this.FindControl<Button>("ApplyCoreSettingsButton");
        if (applyButton is not null)
            applyButton.IsEnabled = canWriteCoreSettings && _applyMaxConnectionsAsync is not null;

        Button? applyMaxDownloadButton = this.FindControl<Button>("ApplyMaxDownloadButton");
        if (applyMaxDownloadButton is not null)
            applyMaxDownloadButton.IsEnabled = canWriteCoreSettings && _applyMaxDownloadAsync is not null;

        Button? applyUploadLimitsButton = this.FindControl<Button>("ApplyUploadLimitsButton");
        if (applyUploadLimitsButton is not null)
            applyUploadLimitsButton.IsEnabled = canWriteCoreSettings && _applyUploadLimitsAsync is not null;

        Button? applyMaxSourcesButton = this.FindControl<Button>("ApplyMaxSourcesPerFileButton");
        if (applyMaxSourcesButton is not null)
            applyMaxSourcesButton.IsEnabled = canWriteCoreSettings && _applyMaxSourcesPerFileAsync is not null;

        Button? applyMaxNewConnectionsButton = this.FindControl<Button>("ApplyMaxNewConnectionsPerTurnButton");
        if (applyMaxNewConnectionsButton is not null)
            applyMaxNewConnectionsButton.IsEnabled = canWriteCoreSettings && _applyMaxNewConnectionsPerTurnAsync is not null;

        Button? applyAutoConnectButton = this.FindControl<Button>("ApplyAutoConnectButton");
        if (applyAutoConnectButton is not null)
            applyAutoConnectButton.IsEnabled = canWriteCoreSettings && _applyAutoConnectAsync is not null;

        Button? changeCorePasswordButton = this.FindControl<Button>("ChangeCorePasswordButton");
        if (changeCorePasswordButton is not null)
            changeCorePasswordButton.IsEnabled = canWriteCoreSettings && _changeCorePasswordAsync is not null;
    }

    private static (int Minimum, int Maximum) CalculateLegacySpeedPerSlotRange(long maxUploadKb)
    {
        if (maxUploadKb <= 0)
            return (1, 500);

        int minimum = Math.Max(1, (int)Math.Pow(maxUploadKb, 0.2));
        int maximum = Math.Max(minimum, (int)Math.Pow(maxUploadKb, 0.6));
        return (minimum, maximum);
    }

    private void UpdateUploadSpeedPerSlotRange(int? preferredValue = null)
    {
        TextBox? maxUploadInput = this.FindControl<TextBox>("CoreMaxUploadTextBox");
        Slider? slider = this.FindControl<Slider>("CoreSpeedPerSlotSlider");
        if (slider is null)
            return;

        string raw = (maxUploadInput?.Text ?? string.Empty).Trim();
        long maxUploadKb = long.TryParse(
            raw,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out long parsed)
            ? parsed
            : 0L;
        (int minimum, int maximum) = CalculateLegacySpeedPerSlotRange(maxUploadKb);

        slider.Minimum = minimum;
        slider.Maximum = maximum;
        int current = preferredValue ?? (int)Math.Round(slider.Value);
        slider.Value = Math.Max(minimum, Math.Min(maximum, current));
    }

    private void CoreMaxUploadTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            UpdateUploadSpeedPerSlotRange();
        }
        catch
        {
            // Range feedback must never break text editing.
        }
    }

    private void SetCoreValue(string controlName, string? value)
    {
        TextBlock? text = this.FindControl<TextBlock>(controlName);
        if (text is not null)
            text.Text = string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private async void ChangeCorePasswordButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _changeCorePasswordAsync is null)
            return;

        TextBox? newPasswordInput = this.FindControl<TextBox>("NewCorePasswordTextBox");
        TextBox? confirmPasswordInput = this.FindControl<TextBox>("ConfirmCorePasswordTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? changeButton = this.FindControl<Button>("ChangeCorePasswordButton");

        string requested = newPasswordInput?.Text ?? string.Empty;
        string confirmation = confirmPasswordInput?.Text ?? string.Empty;
        if (!string.Equals(requested, confirmation, StringComparison.Ordinal))
        {
            if (status is not null)
                status.Text = "Die beiden neuen Passwörter stimmen nicht überein.";
            return;
        }

        if (requested.Any(char.IsControl))
        {
            if (status is not null)
                status.Text = "Das neue Core-Passwort enthält unzulässige Steuerzeichen.";
            return;
        }

        string firstMessage = requested.Length == 0
            ? "Core-Passwortschutz wirklich entfernen?\n\nDas neue Passwort ist leer. Der Core wird danach ohne Passwort geschützt sein.\n\nFortfahren?"
            : "Core-Passwort wirklich ändern?\n\nDas neue Passwort wird nicht gespeichert und muss nach einem Neustart erneut eingegeben werden.\n\nFortfahren?";

        ConfirmDialog firstConfirm = new(
            "Core-Passwort ändern",
            firstMessage,
            "Fortfahren",
            "Abbrechen");
        if (!await firstConfirm.ShowDialog<bool>(this))
            return;

        ConfirmDialog secondConfirm = new(
            "Core-Passwort wirklich übernehmen?",
            "Letzte Bestätigung: neues Core-Passwort jetzt an den verbundenen Core übertragen?",
            "Jetzt ändern",
            "Zurück");
        if (!await secondConfirm.ShowDialog<bool>(this))
            return;

        _coreSettingsWriteRunning = true;
        if (changeButton is not null)
            changeButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage neues Core-Passwort an den Core …";

        try
        {
            string result = await _changeCorePasswordAsync(requested);
            if (newPasswordInput is not null)
                newPasswordInput.Text = string.Empty;
            if (confirmPasswordInput is not null)
                confirmPasswordInput.Text = string.Empty;
            if (status is not null)
                status.Text = result;
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Passwortänderung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (changeButton is not null)
                changeButton.IsEnabled = _changeCorePasswordAsync is not null;
        }
    }

    private async void ChangeCoreIncomingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning
            || _loadCoreDirectoryAsync is null
            || _applyCoreIncomingDirectoryAsync is null)
        {
            return;
        }

        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? changeButton = this.FindControl<Button>("ChangeCoreIncomingButton");

        CoreDirectoryPickerDialog picker = new(_loadCoreDirectoryAsync);
        bool selected = await picker.ShowDialog<bool>(this);
        if (!selected)
            return;

        string requested = picker.SelectedDirectory.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(requested) || requested.Any(char.IsControl))
        {
            if (status is not null)
                status.Text = "Der ausgewählte Core-Incoming-Pfad ist ungültig.";
            return;
        }

        if (string.Equals(requested, _coreIncomingDirectory, StringComparison.Ordinal))
        {
            if (status is not null)
                status.Text = "Core-Incoming entspricht bereits dem vom Core gemeldeten Wert.";
            return;
        }

        string currentDisplay = string.IsNullOrWhiteSpace(_coreIncomingDirectory)
            ? "(leer)"
            : _coreIncomingDirectory;

        ConfirmDialog firstConfirm = new(
            "Kritische Core-Werte übernehmen",
            $"Core-Incoming wirklich ändern?\n\nAktuell: {currentDisplay}\nNeu: {requested}\n\nFortfahren?",
            "Fortfahren",
            "Abbrechen");
        if (!await firstConfirm.ShowDialog<bool>(this))
            return;

        ConfirmDialog secondConfirm = new(
            "Core-Wert wirklich übernehmen?",
            "Letzte Bestätigung: Core-Incoming jetzt wirklich an den Core schreiben?\n\nNur der ausgewählte Core-Pfad wird als Override gesetzt; alle anderen Legacy-Settings bleiben erhalten.",
            "Jetzt schreiben",
            "Zurück");
        if (!await secondConfirm.ShowDialog<bool>(this))
            return;

        _coreSettingsWriteRunning = true;
        if (changeButton is not null)
            changeButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Uebertrage Core-Incoming an den Core ...";

        try
        {
            string effective = await _applyCoreIncomingDirectoryAsync(requested);
            _coreIncomingDirectory = effective;
            SetCoreValue("CoreIncomingText", effective);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: Core-Incoming {effective}.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (changeButton is not null)
            {
                changeButton.IsEnabled = _loadCoreDirectoryAsync is not null
                    && _applyCoreIncomingDirectoryAsync is not null;
            }
        }
    }

    private async void ChangeCoreTemporaryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning
            || _loadCoreDirectoryAsync is null
            || _hasCoreDownloads is null
            || _applyCoreTemporaryDirectoryAsync is null)
        {
            return;
        }

        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? changeButton = this.FindControl<Button>("ChangeCoreTemporaryButton");

        if (_hasCoreDownloads())
        {
            if (status is not null)
                status.Text = "Core-Temp kann nur geändert werden, wenn die Downloadliste komplett leer ist.";
            return;
        }

        CoreDirectoryPickerDialog picker = new(_loadCoreDirectoryAsync);
        bool selected = await picker.ShowDialog<bool>(this);
        if (!selected)
            return;

        string requested = picker.SelectedDirectory.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(requested) || requested.Any(char.IsControl))
        {
            if (status is not null)
                status.Text = "Der ausgewählte Core-Temp-Pfad ist ungültig.";
            return;
        }

        if (string.Equals(requested, _coreTemporaryDirectory, StringComparison.Ordinal))
        {
            if (status is not null)
                status.Text = "Core-Temp entspricht bereits dem vom Core gemeldeten Wert.";
            return;
        }

        string currentDisplay = string.IsNullOrWhiteSpace(_coreTemporaryDirectory)
            ? "(leer)"
            : _coreTemporaryDirectory;

        ConfirmDialog firstConfirm = new(
            "Kritische Core-Werte übernehmen",
            $"Core-Temp wirklich ändern?\n\nAktuell: {currentDisplay}\nNeu: {requested}\n\nFortfahren?",
            "Fortfahren",
            "Abbrechen");
        if (!await firstConfirm.ShowDialog<bool>(this))
            return;

        if (_hasCoreDownloads())
        {
            if (status is not null)
                status.Text = "Core-Temp wurde nicht geändert: Die Downloadliste ist nicht mehr leer.";
            return;
        }

        ConfirmDialog secondConfirm = new(
            "Core-Wert wirklich übernehmen?",
            "Letzte Bestätigung: Core-Temp jetzt wirklich an den Core schreiben?\n\nDie Änderung ist nur bei vollständig leerer Downloadliste zulässig; alle anderen Legacy-Settings bleiben erhalten.",
            "Jetzt schreiben",
            "Zurück");
        if (!await secondConfirm.ShowDialog<bool>(this))
            return;

        _coreSettingsWriteRunning = true;
        if (changeButton is not null)
            changeButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Uebertrage Core-Temp an den Core ...";

        try
        {
            string effective = await _applyCoreTemporaryDirectoryAsync(requested);
            _coreTemporaryDirectory = effective;
            SetCoreValue("CoreTemporaryText", effective);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: Core-Temp {effective}.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (changeButton is not null)
            {
                changeButton.IsEnabled = _loadCoreDirectoryAsync is not null
                    && _hasCoreDownloads is not null
                    && _applyCoreTemporaryDirectoryAsync is not null;
            }
        }
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);


    private async void ApplyCoreNickButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyCoreNicknameAsync is null)
            return;

        TextBox? input = this.FindControl<TextBox>("CoreNickTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyCoreNickButton");
        string requested = (input?.Text ?? string.Empty).Trim();

        if (string.Equals(requested, _coreNickname, StringComparison.Ordinal))
        {
            if (status is not null)
                status.Text = "Der Benutzername entspricht bereits dem vom Core gemeldeten Wert.";
            return;
        }

        string currentDisplay = string.IsNullOrEmpty(_coreNickname) ? "(leer)" : _coreNickname;
        string requestedDisplay = string.IsNullOrEmpty(requested) ? "(leer)" : requested;

        ConfirmDialog firstConfirm = new(
            "Kritische Core-Werte übernehmen",
            $"Benutzername wirklich ändern?\n\nAktuell: {currentDisplay}\nNeu: {requestedDisplay}\n\nFortfahren?",
            "Fortfahren",
            "Abbrechen");
        if (!await firstConfirm.ShowDialog<bool>(this))
            return;

        ConfirmDialog secondConfirm = new(
            "Core-Wert wirklich übernehmen?",
            "Letzte Bestätigung: Benutzername jetzt wirklich an den Core schreiben?\n\nDiese Aktion sollte nicht beiläufig getestet werden.",
            "Jetzt schreiben",
            "Zurück");
        if (!await secondConfirm.ShowDialog<bool>(this))
            return;

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage Benutzername an den Core …";

        try
        {
            string effective = await _applyCoreNicknameAsync(requested);
            _coreNickname = effective;
            if (input is not null)
                input.Text = effective;
            if (status is not null)
                status.Text = string.IsNullOrEmpty(effective)
                    ? "Vom Core bestätigt: Benutzername ist leer."
                    : $"Vom Core bestätigt: Benutzername {effective}.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyCoreNicknameAsync is not null;
        }
    }


    private async void ApplyCorePortButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyCorePortAsync is null)
            return;

        TextBox? input = this.FindControl<TextBox>("CorePortTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyCorePortButton");
        string raw = (input?.Text ?? string.Empty).Trim();

        if (!int.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int requested)
            || requested is < 1 or > 65535)
        {
            if (status is not null)
                status.Text = "Bitte einen Core-Port zwischen 1 und 65535 eingeben.";
            return;
        }

        if (requested == _corePort)
        {
            if (status is not null)
                status.Text = "Der Core-Port entspricht bereits dem vom Core gemeldeten Wert.";
            return;
        }

        ConfirmDialog firstConfirm = new(
            "Kritische Core-Werte übernehmen",
            $"Core-Port wirklich ändern?\n\nAktuell: {_corePort}\nNeu: {requested}\n\nFortfahren?",
            "Fortfahren",
            "Abbrechen");
        if (!await firstConfirm.ShowDialog<bool>(this))
            return;

        ConfirmDialog secondConfirm = new(
            "Core-Wert wirklich übernehmen?",
            "Letzte Bestätigung: Core-Port jetzt wirklich an den Core schreiben?\n\nDiese Aktion sollte nicht beiläufig getestet werden.",
            "Jetzt schreiben",
            "Zurück");
        if (!await secondConfirm.ShowDialog<bool>(this))
            return;

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage Core-Port an den Core …";

        try
        {
            int effective = await _applyCorePortAsync(requested);
            _corePort = effective;
            if (input is not null)
                input.Text = effective.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: Core-Port {effective}.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyCorePortAsync is not null;
        }
    }



    private async void CheckCorePortReachabilityButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_corePortReachabilityRunning || _checkCorePortReachabilityAsync is null)
            return;

        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? checkButton = this.FindControl<Button>("CheckCorePortReachabilityButton");

        _corePortReachabilityRunning = true;
        if (checkButton is not null)
            checkButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Porttest: läuft …";

        try
        {
            string result = await _checkCorePortReachabilityAsync();
            if (status is not null)
                status.Text = result;
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Porttest fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _corePortReachabilityRunning = false;
            if (checkButton is not null)
                checkButton.IsEnabled = _checkCorePortReachabilityAsync is not null;
        }
    }


    private async void ApplyCoreXmlPortButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyCoreXmlPortAsync is null)
            return;

        TextBox? input = this.FindControl<TextBox>("CoreXmlPortTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyCoreXmlPortButton");
        string raw = (input?.Text ?? string.Empty).Trim();

        if (!int.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int requested)
            || requested is < 1 or > 65535)
        {
            if (status is not null)
                status.Text = "Bitte einen XML-Port zwischen 1 und 65535 eingeben.";
            return;
        }

        if (requested == _coreXmlPort)
        {
            if (status is not null)
                status.Text = "Der XML-Port entspricht bereits dem vom Core gemeldeten bzw. aktuell verbundenen Wert.";
            return;
        }

        ConfirmDialog firstConfirm = new(
            "Kritische Core-Werte übernehmen",
            $"XML-Port wirklich ändern?\n\nAktuell: {_coreXmlPort}\nNeu: {requested}\n\nFortfahren?",
            "Fortfahren",
            "Abbrechen");
        if (!await firstConfirm.ShowDialog<bool>(this))
            return;

        ConfirmDialog secondConfirm = new(
            "Core-Wert wirklich übernehmen?",
            "Letzte Bestätigung: XML-Port jetzt wirklich an den Core schreiben?\n\nDer neue XML-Port gilt für die AJCC-Core-Verbindung.",
            "Jetzt schreiben",
            "Zurück");
        if (!await secondConfirm.ShowDialog<bool>(this))
            return;

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage XML-Port an den Core …";

        try
        {
            int effective = await _applyCoreXmlPortAsync(requested);
            _coreXmlPort = effective;
            if (input is not null)
                input.Text = effective.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: XML-Port {effective}.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyCoreXmlPortAsync is not null;
        }
    }

    private async void ApplyMaxDownloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyMaxDownloadAsync is null)
            return;

        TextBox? input = this.FindControl<TextBox>("CoreMaxDownloadTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyMaxDownloadButton");
        string raw = (input?.Text ?? string.Empty).Trim();

        if (!long.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long requested)
            || requested is < 0 or > 100_000_000)
        {
            if (status is not null)
                status.Text = "Bitte eine ganze Zahl zwischen 0 und 100000000 kb/s eingeben.";
            return;
        }

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage Max. Download an den Core …";

        try
        {
            long effective = await _applyMaxDownloadAsync(requested);
            if (input is not null)
                input.Text = effective.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: Max. Download {effective} kb/s.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyMaxDownloadAsync is not null;
        }
    }

    private async void ApplyUploadLimitsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyUploadLimitsAsync is null)
            return;

        TextBox? maxUploadInput = this.FindControl<TextBox>("CoreMaxUploadTextBox");
        Slider? speedSlider = this.FindControl<Slider>("CoreSpeedPerSlotSlider");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyUploadLimitsButton");
        string raw = (maxUploadInput?.Text ?? string.Empty).Trim();

        if (!long.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long maxUploadKb)
            || maxUploadKb is < 0 or > 100_000_000)
        {
            if (status is not null)
                status.Text = "Bitte eine ganze Zahl zwischen 0 und 100000000 kb/s für Max. Upload eingeben.";
            return;
        }

        if (speedSlider is null)
            return;

        (int minimum, int maximum) = CalculateLegacySpeedPerSlotRange(maxUploadKb);
        int requestedSpeedPerSlot = Math.Max(
            minimum,
            Math.Min(maximum, (int)Math.Round(speedSlider.Value)));
        speedSlider.Value = requestedSpeedPerSlot;

        ConfirmDialog confirm = new(
            "Upload-Limits an Core übertragen",
            $"Max. Upload: {maxUploadKb} kb/s\nkb/s pro Uploadslot: {requestedSpeedPerSlot} (zulässiger Bereich {minimum}-{maximum})\n\nFortfahren?",
            "Übertragen",
            "Abbrechen");
        if (!await confirm.ShowDialog<bool>(this))
            return;

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage Upload-Limits an den Core …";

        try
        {
            (long MaxUploadKb, int SpeedPerSlot) effective =
                await _applyUploadLimitsAsync(maxUploadKb, requestedSpeedPerSlot);
            if (maxUploadInput is not null)
                maxUploadInput.Text = effective.MaxUploadKb.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateUploadSpeedPerSlotRange(effective.SpeedPerSlot);
            if (status is not null)
                status.Text = $"Vom Core zurückgelesen: Max. Upload {effective.MaxUploadKb} kb/s, {effective.SpeedPerSlot} kb/s pro Slot.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyUploadLimitsAsync is not null;
        }
    }

    private async void ApplyCoreSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyMaxConnectionsAsync is null)
            return;

        TextBox? input = this.FindControl<TextBox>("CoreMaxConnectionsTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyCoreSettingsButton");
        string raw = (input?.Text ?? string.Empty).Trim();

        if (!int.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int maxConnections)
            || maxConnections < 0)
        {
            if (status is not null)
                status.Text = "Bitte eine ganze Zahl ab 0 eingeben.";
            return;
        }

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage an den Core …";

        try
        {
            int effective = await _applyMaxConnectionsAsync(maxConnections);
            if (input is not null)
                input.Text = effective.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: {effective:N0} maximale Verbindungen.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyMaxConnectionsAsync is not null;
        }
    }

    private async void ApplyMaxSourcesPerFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyMaxSourcesPerFileAsync is null)
            return;

        TextBox? input = this.FindControl<TextBox>("CoreMaxSourcesPerFileTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyMaxSourcesPerFileButton");
        string raw = (input?.Text ?? string.Empty).Trim();

        if (!int.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int maxSourcesPerFile)
            || maxSourcesPerFile < 0)
        {
            if (status is not null)
                status.Text = "Bitte eine ganze Zahl ab 0 eingeben.";
            return;
        }

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage an den Core …";

        try
        {
            int effective = await _applyMaxSourcesPerFileAsync(maxSourcesPerFile);
            if (input is not null)
                input.Text = effective.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: {effective:N0} maximale Quellen pro Datei.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyMaxSourcesPerFileAsync is not null;
        }
    }


    private async void ApplyMaxNewConnectionsPerTurnButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyMaxNewConnectionsPerTurnAsync is null)
            return;

        TextBox? input = this.FindControl<TextBox>("CoreMaxNewConnectionsPerTurnTextBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyMaxNewConnectionsPerTurnButton");
        string raw = (input?.Text ?? string.Empty).Trim();

        if (!int.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int maxNewConnectionsPerTurn)
            || maxNewConnectionsPerTurn is < 1 or > 200)
        {
            if (status is not null)
                status.Text = "Bitte eine ganze Zahl zwischen 1 und 200 eingeben.";
            return;
        }

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage an den Core …";

        try
        {
            int effective = await _applyMaxNewConnectionsPerTurnAsync(maxNewConnectionsPerTurn);
            if (input is not null)
                input.Text = effective.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (status is not null)
                status.Text = $"Vom Core bestätigt: {effective:N0} maximale neue Verbindungen pro 10 Sekunden.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyMaxNewConnectionsPerTurnAsync is not null;
        }
    }


    private async void ApplyAutoConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_coreSettingsWriteRunning || _applyAutoConnectAsync is null)
            return;

        CheckBox? input = this.FindControl<CheckBox>("CoreAutoConnectCheckBox");
        TextBlock? status = this.FindControl<TextBlock>("CoreSettingsStatusText");
        Button? applyButton = this.FindControl<Button>("ApplyAutoConnectButton");
        bool autoConnect = input?.IsChecked == true;

        _coreSettingsWriteRunning = true;
        if (applyButton is not null)
            applyButton.IsEnabled = false;
        if (status is not null)
            status.Text = "Übertrage an den Core …";

        try
        {
            bool effective = await _applyAutoConnectAsync(autoConnect);
            if (input is not null)
                input.IsChecked = effective;
            if (status is not null)
                status.Text = $"Vom Core bestätigt: Automatisch verbinden {(effective ? "ein" : "aus")}.";
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Übertragung fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _coreSettingsWriteRunning = false;
            if (applyButton is not null)
                applyButton.IsEnabled = _applyAutoConnectAsync is not null;
        }
    }

    private async void BrowseLocalIncomingMappingButton_OnClick(object? sender, RoutedEventArgs e)
    {
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

        SaveLocalIncomingMapping(path.LocalPath);
    }

    private void RemoveLocalIncomingMappingButton_OnClick(object? sender, RoutedEventArgs e)
        => SaveLocalIncomingMapping(string.Empty);

    private void SaveLocalIncomingMapping(string mapping)
    {
        TextBox? input = this.FindControl<TextBox>("LocalIncomingMappingTextBox");

        if (!_localIncomingMappingStore.TrySave(_mappingEndpoint, mapping, out string errorMessage))
        {
            if (input is not null)
                input.Text = "Speichern fehlgeschlagen: " + errorMessage;
            return;
        }

        if (input is not null)
            input.Text = mapping;

        _mappingChanged?.Invoke(mapping);
    }

    private void SuppressCoreSwitchConfirmationCheckBox_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_loadingUiPreferences
            || _applySuppressCoreSwitchConfirmation is null
            || sender is not CheckBox input)
        {
            return;
        }

        bool requested = input.IsChecked == true;
        if (_applySuppressCoreSwitchConfirmation(requested))
            return;

        _loadingUiPreferences = true;
        try
        {
            input.IsChecked = !requested;
        }
        finally
        {
            _loadingUiPreferences = false;
        }
    }

    private void GuiSoundsEnabledCheckBox_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_loadingUiPreferences
            || _applyGuiSoundsEnabled is null
            || sender is not CheckBox input)
        {
            return;
        }

        bool requested = input.IsChecked == true;
        if (_applyGuiSoundsEnabled(requested))
        {
            _guiSoundsEnabled = requested;
            return;
        }

        _loadingUiPreferences = true;
        try
        {
            input.IsChecked = _guiSoundsEnabled;
        }
        finally
        {
            _loadingUiPreferences = false;
        }
    }

    private async void AutoLoadShareFilesAtStartupCheckBox_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_loadingUiPreferences
            || _applyAutoLoadShareFilesAtStartup is null
            || sender is not CheckBox input)
        {
            return;
        }

        bool requested = input.IsChecked == true;
        if (requested && !_autoLoadShareFilesAtStartup)
        {
            ConfirmDialog confirm = new(
                "Share-Dateiliste beim Start laden",
                "Diese Option lädt die vollständige Share-Dateiliste beim Programmstart nach der ersten Core-Verbindung.\n\n"
                + "Bei sehr großen Shares kann das den Start deutlich verzögern oder den Core belasten. "
                + "Wenn ein Auto-Load nicht sauber abgeschlossen wurde, deaktiviert AJCC-X die Option beim nächsten Start automatisch.\n\n"
                + "Option aktivieren?",
                "Aktivieren",
                "Abbrechen");
            if (!await confirm.ShowDialog<bool>(this))
            {
                _loadingUiPreferences = true;
                try
                {
                    input.IsChecked = false;
                }
                finally
                {
                    _loadingUiPreferences = false;
                }
                return;
            }
        }

        if (_applyAutoLoadShareFilesAtStartup(requested))
        {
            _autoLoadShareFilesAtStartup = requested;
            return;
        }

        _loadingUiPreferences = true;
        try
        {
            input.IsChecked = _autoLoadShareFilesAtStartup;
        }
        finally
        {
            _loadingUiPreferences = false;
        }
    }

    private void LoadDownloadQueueConfiguration()
    {
        DownloadQueueConfiguration configuration = _downloadQueueConfigurationStore.Load();
        bool enabled = configuration.Limit > 0;
        int preparedLimit = enabled ? configuration.Limit : configuration.PreparedLimit;

        CheckBox? enabledInput = this.FindControl<CheckBox>("DownloadQueueEnabledCheckBox");
        NumericUpDown? limitInput = this.FindControl<NumericUpDown>("DownloadQueueLimitInput");
        if (enabledInput is not null)
            enabledInput.IsChecked = enabled;
        if (limitInput is not null)
            limitInput.Value = preparedLimit;

        UpdateDownloadQueueStatus(configuration);
    }

    private void SaveDownloadQueueConfigurationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CheckBox? enabledInput = this.FindControl<CheckBox>("DownloadQueueEnabledCheckBox");
        NumericUpDown? limitInput = this.FindControl<NumericUpDown>("DownloadQueueLimitInput");
        decimal rawLimit = limitInput?.Value ?? DownloadQueueConfiguration.Default.PreparedLimit;
        int preparedLimit = Math.Clamp((int)Math.Round(rawLimit), 1, 100);
        bool enabled = enabledInput?.IsChecked == true;

        DownloadQueueConfiguration configuration = new(
            enabled ? preparedLimit : 0,
            preparedLimit);
        TextBlock? status = this.FindControl<TextBlock>("DownloadQueueStatusText");

        if (!_downloadQueueConfigurationStore.TrySave(configuration, out string errorMessage))
        {
            if (status is not null)
                status.Text = "Download-Queue konnte nicht gespeichert werden: " + errorMessage;
            return;
        }

        if (limitInput is not null)
            limitInput.Value = preparedLimit;
        UpdateDownloadQueueStatus(configuration);
    }

    private void UpdateDownloadQueueStatus(DownloadQueueConfiguration configuration)
    {
        TextBlock? status = this.FindControl<TextBlock>("DownloadQueueStatusText");
        if (status is null)
            return;

        status.Text = configuration.Limit > 0
            ? $"aktiv · maximal {configuration.Limit:N0} Downloads gleichzeitig"
            : $"deaktiviert · vorbereitetes Limit {configuration.PreparedLimit:N0}";
    }

    private async void ConfigureDownloadStatusColorsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DownloadStatusColorConfigurationStore store = new();
        DownloadStatusColorDialog dialog = new(store.Load());
        if (!await dialog.ShowDialog<bool>(this))
            return;

        SaveDownloadStatusColors(
            store,
            dialog.Configuration,
            "Download-Statusfarben gespeichert. Sie werden nach dem Schließen der Einstellungen übernommen.");
    }

    private void ResetDownloadStatusColorsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DownloadStatusColorConfigurationStore store = new();
        SaveDownloadStatusColors(
            store,
            new DownloadStatusColorConfiguration(),
            "Standardfarben wiederhergestellt. Sie werden nach dem Schließen der Einstellungen übernommen.");
    }

    private void SaveDownloadStatusColors(
        DownloadStatusColorConfigurationStore store,
        DownloadStatusColorConfiguration configuration,
        string successMessage)
    {
        TextBlock? status = this.FindControl<TextBlock>("DownloadStatusColorStatusText");
        if (!store.TrySave(configuration, out string errorMessage))
        {
            if (status is not null)
                status.Text = "Speichern fehlgeschlagen: " + errorMessage;
            return;
        }

        if (status is not null)
            status.Text = successMessage;
    }

    private async void BrowseVlcButton_OnClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "VLC-Programm auswählen",
                AllowMultiple = false
            });

        if (files.Count == 0)
            return;

        Uri path = files[0].Path;
        if (!path.IsFile)
            return;

        TextBox? pathInput = this.FindControl<TextBox>("VlcPathTextBox");
        if (pathInput is not null)
            pathInput.Text = path.LocalPath;

        SaveExternalVlcConfiguration();
    }

    private void VlcEnabledCheckBox_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_loadingExternalVlcConfiguration)
            SaveExternalVlcConfiguration();
    }

    private static bool IsValidVlcExecutablePath(string path)
    {
        string candidate = (path ?? string.Empty).Trim();
        if (candidate.Length == 0 || !File.Exists(candidate))
            return false;

        string fileName = Path.GetFileName(candidate);
        string expectedFileName = OperatingSystem.IsWindows() ? "vlc.exe" : "vlc";
        return string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadExternalVlcConfiguration()
    {
        ExternalVlcConfiguration configuration = _externalVlcConfigurationStore.Load();
        string path = (configuration.ExecutablePath ?? string.Empty).Trim();
        bool enabled = configuration.Enabled && IsValidVlcExecutablePath(path);

        if (configuration.Enabled && !enabled)
            _externalVlcConfigurationStore.TrySave(new ExternalVlcConfiguration(false, path), out _);

        _loadingExternalVlcConfiguration = true;
        try
        {
            TextBox? pathInput = this.FindControl<TextBox>("VlcPathTextBox");
            CheckBox? enabledInput = this.FindControl<CheckBox>("VlcEnabledCheckBox");
            if (pathInput is not null)
                pathInput.Text = path;
            if (enabledInput is not null)
                enabledInput.IsChecked = enabled;
        }
        finally
        {
            _loadingExternalVlcConfiguration = false;
        }

        UpdateExternalVlcStatus();
    }

    private void SaveExternalVlcConfiguration()
    {
        TextBox? pathInput = this.FindControl<TextBox>("VlcPathTextBox");
        CheckBox? enabledInput = this.FindControl<CheckBox>("VlcEnabledCheckBox");
        TextBlock? status = this.FindControl<TextBlock>("VlcStatusText");

        string path = (pathInput?.Text ?? string.Empty).Trim();
        bool requestedEnabled = enabledInput?.IsChecked == true;
        bool effectiveEnabled = requestedEnabled && IsValidVlcExecutablePath(path);

        if (requestedEnabled && !effectiveEnabled && enabledInput is not null)
        {
            _loadingExternalVlcConfiguration = true;
            try
            {
                enabledInput.IsChecked = false;
            }
            finally
            {
                _loadingExternalVlcConfiguration = false;
            }
        }

        ExternalVlcConfiguration configuration = new(effectiveEnabled, path);

        if (!_externalVlcConfigurationStore.TrySave(configuration, out string errorMessage))
        {
            if (status is not null)
                status.Text = "Speichern fehlgeschlagen: " + errorMessage;
            return;
        }

        UpdateExternalVlcStatus();
    }

    private void UpdateExternalVlcStatus()
    {
        TextBox? pathInput = this.FindControl<TextBox>("VlcPathTextBox");
        CheckBox? enabledInput = this.FindControl<CheckBox>("VlcEnabledCheckBox");
        TextBlock? status = this.FindControl<TextBlock>("VlcStatusText");
        if (status is null)
            return;

        string path = (pathInput?.Text ?? string.Empty).Trim();
        if (path.Length > 0 && !File.Exists(path))
            status.Text = "VLC-Programm nicht erreichbar";
        else if (path.Length > 0 && !IsValidVlcExecutablePath(path))
            status.Text = "kein gültiges VLC-Programm ausgewählt";
        else if (enabledInput?.IsChecked == true)
            status.Text = "bereit";
        else
            status.Text = "deaktiviert";
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(true);
}
