using AJCC.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace AJCC.Desktop.Views;

public sealed partial class SettingsDialog : Window
{
    private readonly ExternalVlcConfigurationStore _externalVlcConfigurationStore = new();
    private bool _loadingExternalVlcConfiguration;

    public SettingsDialog()
    {
        InitializeComponent();
        LoadExternalVlcConfiguration();
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

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
