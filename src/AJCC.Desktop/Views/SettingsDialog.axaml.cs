using AJCC.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace AJCC.Desktop.Views;

public sealed partial class SettingsDialog : Window
{
    private readonly ExternalVlcConfigurationStore _externalVlcConfigurationStore = new();
    private readonly LocalIncomingMappingStore _localIncomingMappingStore = new();
    private bool _loadingExternalVlcConfiguration;
    private string _mappingEndpoint = string.Empty;
    private Action<string>? _mappingChanged;
    private Func<int, Task<int>>? _applyMaxConnectionsAsync;
    private Func<int, Task<int>>? _applyMaxSourcesPerFileAsync;
    private Func<int, Task<int>>? _applyMaxNewConnectionsPerTurnAsync;
    private Func<bool, Task<bool>>? _applyAutoConnectAsync;
    private Func<string, Task<string>>? _applyCoreNicknameAsync;
    private Func<int, Task<int>>? _applyCorePortAsync;
    private Func<int, Task<int>>? _applyCoreXmlPortAsync;
    private Func<long, Task<long>>? _applyMaxDownloadAsync;
    private string _coreNickname = string.Empty;
    private int _corePort = 8000;
    private int _coreXmlPort = 9851;
    private bool _coreSettingsWriteRunning;

    public SettingsDialog()
    {
        InitializeComponent();
        LoadExternalVlcConfiguration();
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
        int maxSourcesPerFile,
        int maxNewConnectionsPerTurn,
        bool autoConnect,
        bool canWriteCoreSettings,
        Func<int, Task<int>>? applyMaxConnectionsAsync,
        Func<long, Task<long>>? applyMaxDownloadAsync,
        Func<int, Task<int>>? applyMaxSourcesPerFileAsync,
        Func<int, Task<int>>? applyMaxNewConnectionsPerTurnAsync,
        Func<bool, Task<bool>>? applyAutoConnectAsync,
        Func<string, Task<string>>? applyCoreNicknameAsync,
        Func<int, Task<int>>? applyCorePortAsync,
        Func<int, Task<int>>? applyCoreXmlPortAsync)
    {
        _coreNickname = (nick ?? string.Empty).Trim();
        TextBox? coreNickInput = this.FindControl<TextBox>("CoreNickTextBox");
        if (coreNickInput is not null)
            coreNickInput.Text = _coreNickname;
        SetCoreValue("CoreIncomingText", incomingDirectory);
        SetCoreValue("CoreTemporaryText", temporaryDirectory);
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
        _applyCoreXmlPortAsync = applyCoreXmlPortAsync;
        _applyMaxDownloadAsync = applyMaxDownloadAsync;

        TextBox? maxConnectionsInput = this.FindControl<TextBox>("CoreMaxConnectionsTextBox");
        if (maxConnectionsInput is not null)
            maxConnectionsInput.Text = Math.Max(0, maxConnections).ToString(System.Globalization.CultureInfo.InvariantCulture);

        TextBox? maxDownloadInput = this.FindControl<TextBox>("CoreMaxDownloadTextBox");
        if (maxDownloadInput is not null)
            maxDownloadInput.Text = Math.Max(0L, maxDownloadKb).ToString(System.Globalization.CultureInfo.InvariantCulture);

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

        Button? applyCorePortButton = this.FindControl<Button>("ApplyCorePortButton");
        if (applyCorePortButton is not null)
            applyCorePortButton.IsEnabled = canWriteCoreSettings && _applyCorePortAsync is not null;

        Button? applyCoreXmlPortButton = this.FindControl<Button>("ApplyCoreXmlPortButton");
        if (applyCoreXmlPortButton is not null)
            applyCoreXmlPortButton.IsEnabled = canWriteCoreSettings && _applyCoreXmlPortAsync is not null;

        Button? applyButton = this.FindControl<Button>("ApplyCoreSettingsButton");
        if (applyButton is not null)
            applyButton.IsEnabled = canWriteCoreSettings && _applyMaxConnectionsAsync is not null;

        Button? applyMaxDownloadButton = this.FindControl<Button>("ApplyMaxDownloadButton");
        if (applyMaxDownloadButton is not null)
            applyMaxDownloadButton.IsEnabled = canWriteCoreSettings && _applyMaxDownloadAsync is not null;

        Button? applyMaxSourcesButton = this.FindControl<Button>("ApplyMaxSourcesPerFileButton");
        if (applyMaxSourcesButton is not null)
            applyMaxSourcesButton.IsEnabled = canWriteCoreSettings && _applyMaxSourcesPerFileAsync is not null;

        Button? applyMaxNewConnectionsButton = this.FindControl<Button>("ApplyMaxNewConnectionsPerTurnButton");
        if (applyMaxNewConnectionsButton is not null)
            applyMaxNewConnectionsButton.IsEnabled = canWriteCoreSettings && _applyMaxNewConnectionsPerTurnAsync is not null;

        Button? applyAutoConnectButton = this.FindControl<Button>("ApplyAutoConnectButton");
        if (applyAutoConnectButton is not null)
            applyAutoConnectButton.IsEnabled = canWriteCoreSettings && _applyAutoConnectAsync is not null;
    }

    private void SetCoreValue(string controlName, string? value)
    {
        TextBlock? text = this.FindControl<TextBlock>(controlName);
        if (text is not null)
            text.Text = string.IsNullOrWhiteSpace(value) ? "-" : value;
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

    private void LoadExternalVlcConfiguration()
    {
        ExternalVlcConfiguration configuration = _externalVlcConfigurationStore.Load();

        _loadingExternalVlcConfiguration = true;
        try
        {
            TextBox? pathInput = this.FindControl<TextBox>("VlcPathTextBox");
            CheckBox? enabledInput = this.FindControl<CheckBox>("VlcEnabledCheckBox");
            if (pathInput is not null)
                pathInput.Text = configuration.ExecutablePath;
            if (enabledInput is not null)
                enabledInput.IsChecked = configuration.Enabled;
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

        ExternalVlcConfiguration configuration = new(
            enabledInput?.IsChecked == true,
            pathInput?.Text ?? string.Empty);

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
        if (enabledInput?.IsChecked != true)
            status.Text = "deaktiviert";
        else if (path.Length == 0)
            status.Text = "kein VLC-Programm ausgewählt";
        else if (!File.Exists(path))
            status.Text = "VLC-Programm nicht erreichbar";
        else
            status.Text = "bereit";
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(true);
}
