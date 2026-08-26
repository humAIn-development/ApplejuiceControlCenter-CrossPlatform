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
        string corePort,
        string xmlPort,
        int maxConnections,
        int maxSourcesPerFile,
        bool canWriteCoreSettings,
        Func<int, Task<int>>? applyMaxConnectionsAsync,
        Func<int, Task<int>>? applyMaxSourcesPerFileAsync)
    {
        SetCoreValue("CoreNickText", nick);
        SetCoreValue("CoreIncomingText", incomingDirectory);
        SetCoreValue("CoreTemporaryText", temporaryDirectory);
        SetCoreValue("CorePortText", corePort);
        SetCoreValue("CoreXmlPortText", xmlPort);

        _applyMaxConnectionsAsync = applyMaxConnectionsAsync;
        _applyMaxSourcesPerFileAsync = applyMaxSourcesPerFileAsync;

        TextBox? maxConnectionsInput = this.FindControl<TextBox>("CoreMaxConnectionsTextBox");
        if (maxConnectionsInput is not null)
            maxConnectionsInput.Text = Math.Max(0, maxConnections).ToString(System.Globalization.CultureInfo.InvariantCulture);

        TextBox? maxSourcesInput = this.FindControl<TextBox>("CoreMaxSourcesPerFileTextBox");
        if (maxSourcesInput is not null)
            maxSourcesInput.Text = Math.Max(0, maxSourcesPerFile).ToString(System.Globalization.CultureInfo.InvariantCulture);

        Button? applyButton = this.FindControl<Button>("ApplyCoreSettingsButton");
        if (applyButton is not null)
            applyButton.IsEnabled = canWriteCoreSettings && _applyMaxConnectionsAsync is not null;

        Button? applyMaxSourcesButton = this.FindControl<Button>("ApplyMaxSourcesPerFileButton");
        if (applyMaxSourcesButton is not null)
            applyMaxSourcesButton.IsEnabled = canWriteCoreSettings && _applyMaxSourcesPerFileAsync is not null;
    }

    private void SetCoreValue(string controlName, string? value)
    {
        TextBlock? text = this.FindControl<TextBlock>(controlName);
        if (text is not null)
            text.Text = string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

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
