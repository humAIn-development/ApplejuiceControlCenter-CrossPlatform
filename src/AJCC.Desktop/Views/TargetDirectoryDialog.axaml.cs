using AJCC.Core.Helpers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class TargetDirectoryDialog : Window
{
    private char _separator = '\\';

    public TargetDirectoryDialog()
    {
        InitializeComponent();
    }

    public TargetDirectoryDialog(string currentTargetDirectory, string incomingDirectory)
        : this()
    {
        _separator = CoreTargetDirectory.DetermineSeparator(incomingDirectory, currentTargetDirectory);

        TextBlock? incomingText = this.FindControl<TextBlock>("IncomingPathText");
        TextBlock? currentText = this.FindControl<TextBlock>("CurrentTargetText");
        TextBox? input = this.FindControl<TextBox>("TargetPathInput");

        if (incomingText is not null)
            incomingText.Text = string.IsNullOrWhiteSpace(incomingDirectory) ? "unbekannt" : incomingDirectory.Trim();
        if (currentText is not null)
            currentText.Text = string.IsNullOrWhiteSpace(currentTargetDirectory) ? "Incoming" : currentTargetDirectory.Trim();
        if (input is not null)
        {
            input.Text = currentTargetDirectory ?? string.Empty;
            input.SelectionStart = 0;
            input.SelectionEnd = input.Text?.Length ?? 0;
        }
    }

    public string TargetDirectory { get; private set; } = string.Empty;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void IncomingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? input = this.FindControl<TextBox>("TargetPathInput");
        TextBlock? validation = this.FindControl<TextBlock>("ValidationText");
        if (input is not null)
            input.Text = string.Empty;
        if (validation is not null)
            validation.Text = "Leer bedeutet: Download direkt in das Incoming-Verzeichnis des verbundenen Cores legen.";
    }

    private void AcceptButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? input = this.FindControl<TextBox>("TargetPathInput");
        TextBlock? validation = this.FindControl<TextBlock>("ValidationText");
        string raw = input?.Text ?? string.Empty;
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeRelative(raw, _separator);

        if (!result.Success)
        {
            if (validation is not null)
                validation.Text = result.ErrorMessage;
            input?.Focus();
            return;
        }

        if (result.Changed && !string.Equals(raw.Trim().Trim('"'), result.Value, StringComparison.Ordinal))
        {
            if (input is not null)
                input.Text = result.Value;
            if (validation is not null)
                validation.Text = "Der Pfad wurde für Core-Kompatibilität bereinigt. Bitte prüfen und erneut auf Übernehmen klicken.";
            input?.Focus();
            return;
        }

        TargetDirectory = result.Value;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
