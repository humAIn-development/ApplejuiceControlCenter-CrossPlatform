using System.Collections.ObjectModel;
using AJCC.Core.Helpers;
using AJCC.Core.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class TargetDirectoryDialog : Window
{
    private char _separator = '\\';
    private string _incomingDirectory = string.Empty;
    private string _currentCorePath = string.Empty;
    private string _browserSelectedRelative = string.Empty;
    private Func<string?, Task<AjDirectoryListResult>>? _loadDirectoryAsync;
    private bool _initialLoadStarted;

    public TargetDirectoryDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += TargetDirectoryDialog_OnOpened;
    }

    public TargetDirectoryDialog(
        string currentTargetDirectory,
        string incomingDirectory,
        Func<string?, Task<AjDirectoryListResult>> loadDirectoryAsync)
        : this()
    {
        _incomingDirectory = (incomingDirectory ?? string.Empty).Trim().Trim('"');
        _loadDirectoryAsync = loadDirectoryAsync ?? throw new ArgumentNullException(nameof(loadDirectoryAsync));
        _separator = CoreTargetDirectory.DetermineSeparator(_incomingDirectory, currentTargetDirectory);

        TextBlock? incomingText = this.FindControl<TextBlock>("IncomingPathText");
        TextBlock? currentText = this.FindControl<TextBlock>("CurrentTargetText");
        TextBox? input = this.FindControl<TextBox>("TargetPathInput");

        if (incomingText is not null)
            incomingText.Text = string.IsNullOrWhiteSpace(_incomingDirectory) ? "unbekannt" : _incomingDirectory;
        if (currentText is not null)
            currentText.Text = string.IsNullOrWhiteSpace(currentTargetDirectory) ? "Incoming" : currentTargetDirectory.Trim();

        if (input is not null)
        {
            input.Text = currentTargetDirectory ?? string.Empty;
            input.SelectionStart = 0;
            input.SelectionEnd = input.Text?.Length ?? 0;
        }
    }

    public ObservableCollection<CoreDirectoryChoice> Directories { get; } = new();
    public string TargetDirectory { get; private set; } = string.Empty;
    public bool SelectedFromCoreBrowser { get; private set; }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private async void TargetDirectoryDialog_OnOpened(object? sender, EventArgs e)
    {
        if (_initialLoadStarted)
            return;

        _initialLoadStarted = true;
        await LoadDirectoryAsync(_incomingDirectory, showError: true);
    }

    private async Task<bool> LoadDirectoryAsync(string path, bool showError)
    {
        TextBlock? status = this.FindControl<TextBlock>("DirectoryStatusText");
        TextBlock? currentPathText = this.FindControl<TextBlock>("CurrentCorePathText");

        if (_loadDirectoryAsync is null)
        {
            if (status is not null)
                status.Text = "Core-Verzeichnisbrowser ist nicht verfÃ¼gbar.";
            return false;
        }

        string normalizedPath = NormalizeAbsoluteCorePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            if (status is not null)
                status.Text = "Das Core-Incoming-Verzeichnis ist noch nicht bekannt.";
            return false;
        }

        if (!IsAtOrBelowIncoming(normalizedPath))
        {
            if (status is not null)
                status.Text = "Navigation auÃŸerhalb des Core-Incoming-Verzeichnisses wurde blockiert.";
            return false;
        }

        try
        {
            if (status is not null)
                status.Text = "Lade Core-Verzeichnis â€¦";

            AjDirectoryListResult result = await _loadDirectoryAsync(normalizedPath);
            if (!string.IsNullOrWhiteSpace(result.Separator))
            {
                char separator = result.Separator.Trim()[0];
                if (separator is '/' or '\\')
                    _separator = separator;
            }

            _currentCorePath = normalizedPath;
            Directories.Clear();

            foreach (AjDirectoryEntry entry in result.Directories
                         .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                         .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                string fullPath = !string.IsNullOrWhiteSpace(entry.Path)
                    ? NormalizeAbsoluteCorePath(entry.Path)
                    : CombineCorePath(_currentCorePath, entry.Name.Trim());
                if (IsAtOrBelowIncoming(fullPath))
                    Directories.Add(new CoreDirectoryChoice(entry.Name.Trim(), fullPath));
            }

            if (currentPathText is not null)
                currentPathText.Text = _currentCorePath;

            if (status is not null)
            {
                status.Text = Directories.Count == 0
                    ? "Keine Unterordner vorhanden. Der aktuelle Ordner kann trotzdem als Ziel verwendet werden."
                    : $"{Directories.Count:N0} Unterordner geladen.";
            }

            return true;
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Core-Verzeichnis konnte nicht geladen werden: " + ex.Message;

            if (showError)
            {
                TextBlock? validation = this.FindControl<TextBlock>("ValidationText");
                if (validation is not null)
                    validation.Text = "Core-Verzeichnisbrowser konnte nicht geladen werden. Die manuelle relative Eingabe bleibt verfÃ¼gbar.";
            }

            return false;
        }
    }

    private async void OpenDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ListBox? list = this.FindControl<ListBox>("CoreDirectoryList");
        if (list?.SelectedItem is not CoreDirectoryChoice selected)
        {
            SetDirectoryStatus("Bitte zuerst einen Unterordner auswÃ¤hlen.");
            return;
        }

        await LoadDirectoryAsync(selected.FullPath, showError: true);
    }

    private async void UpDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentCorePath) || PathsEqual(_currentCorePath, _incomingDirectory))
        {
            SetDirectoryStatus("Core-Incoming ist bereits die oberste erlaubte Ebene.");
            return;
        }

        await LoadDirectoryAsync(GetParentUnderIncoming(_currentCorePath), showError: true);
    }

    private async void ReloadDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        string path = string.IsNullOrWhiteSpace(_currentCorePath) ? _incomingDirectory : _currentCorePath;
        await LoadDirectoryAsync(path, showError: true);
    }

    private void UseCurrentDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentCorePath))
        {
            SetDirectoryStatus("Noch kein Core-Verzeichnis geladen.");
            return;
        }

        string relative = GetRelativeUnderIncoming(_currentCorePath);
        TextBox? input = this.FindControl<TextBox>("TargetPathInput");
        TextBlock? validation = this.FindControl<TextBlock>("ValidationText");

        _browserSelectedRelative = relative;
        if (input is not null)
            input.Text = relative;

        if (validation is not null)
        {
            validation.Text = relative.Length == 0
                ? "Vorhandenes Core-Incoming gewÃ¤hlt. Mit Ãœbernehmen bestÃ¤tigen."
                : $"Vorhandener Core-Ordner gewÃ¤hlt: {relative}. Mit Ãœbernehmen bestÃ¤tigen.";
        }
    }

    private void IncomingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? input = this.FindControl<TextBox>("TargetPathInput");
        TextBlock? validation = this.FindControl<TextBlock>("ValidationText");

        _browserSelectedRelative = string.Empty;
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
        string trimmed = raw.Trim().Trim('"');

        bool selectedFromBrowser = string.Equals(
            trimmed,
            _browserSelectedRelative,
            StringComparison.Ordinal);

        CoreTargetDirectoryNormalizationResult result = selectedFromBrowser
            ? CoreTargetDirectory.NormalizeExistingRelative(raw, _separator)
            : CoreTargetDirectory.NormalizeRelative(raw, _separator);

        if (!result.Success)
        {
            if (validation is not null)
                validation.Text = result.ErrorMessage;
            input?.Focus();
            return;
        }

        if (!selectedFromBrowser
            && result.Changed
            && !string.Equals(trimmed, result.Value, StringComparison.Ordinal))
        {
            if (input is not null)
                input.Text = result.Value;
            if (validation is not null)
                validation.Text = "Der manuelle Pfad wurde fÃ¼r Core-KompatibilitÃ¤t bereinigt. Bitte prÃ¼fen und erneut auf Ãœbernehmen klicken.";
            input?.Focus();
            return;
        }

        TargetDirectory = result.Value;
        SelectedFromCoreBrowser = selectedFromBrowser;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void SetDirectoryStatus(string text)
    {
        TextBlock? status = this.FindControl<TextBlock>("DirectoryStatusText");
        if (status is not null)
            status.Text = text;
    }

    private string NormalizeAbsoluteCorePath(string path)
    {
        string value = (path ?? string.Empty).Trim().Trim('"');
        if (value.Length == 0)
            return string.Empty;

        char other = _separator == '/' ? '\\' : '/';
        value = value.Replace(other, _separator);

        string root = _separator == '/' && value.StartsWith("/", StringComparison.Ordinal)
            ? "/"
            : string.Empty;

        string[] parts = value.Split(_separator, StringSplitOptions.RemoveEmptyEntries);
        string normalized = string.Join(_separator, parts);

        if (root.Length > 0)
            normalized = root + normalized;

        if (_separator == '\\' && value.StartsWith("\\\\", StringComparison.Ordinal))
            normalized = "\\\\" + normalized.TrimStart('\\');

        return normalized.Length == 0 ? root : normalized;
    }

    private string CombineCorePath(string parent, string child)
    {
        string left = NormalizeAbsoluteCorePath(parent).TrimEnd(_separator);
        string right = (child ?? string.Empty)
            .Trim()
            .Trim('"')
            .Replace(_separator == '/' ? '\\' : '/', _separator)
            .Trim(_separator);

        if (left.Length == 0)
            return right;
        if (left == "/" && _separator == '/')
            return "/" + right;

        return left + _separator + right;
    }

    private bool IsAtOrBelowIncoming(string path)
    {
        string root = NormalizeAbsoluteCorePath(_incomingDirectory).TrimEnd(_separator);
        string candidate = NormalizeAbsoluteCorePath(path).TrimEnd(_separator);

        if (root.Length == 0 || candidate.Length == 0)
            return false;

        StringComparison comparison = _separator == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(candidate, root, comparison)
            || candidate.StartsWith(root + _separator, comparison);
    }

    private bool PathsEqual(string left, string right)
    {
        StringComparison comparison = _separator == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            NormalizeAbsoluteCorePath(left).TrimEnd(_separator),
            NormalizeAbsoluteCorePath(right).TrimEnd(_separator),
            comparison);
    }

    private string GetParentUnderIncoming(string path)
    {
        string root = NormalizeAbsoluteCorePath(_incomingDirectory).TrimEnd(_separator);
        string current = NormalizeAbsoluteCorePath(path).TrimEnd(_separator);

        if (PathsEqual(current, root))
            return root;

        int index = current.LastIndexOf(_separator);
        if (index < 0)
            return root;

        string parent = current[..index];
        return IsAtOrBelowIncoming(parent) ? parent : root;
    }

    private string GetRelativeUnderIncoming(string path)
    {
        string root = NormalizeAbsoluteCorePath(_incomingDirectory).TrimEnd(_separator);
        string current = NormalizeAbsoluteCorePath(path).TrimEnd(_separator);

        if (PathsEqual(current, root))
            return string.Empty;

        if (!IsAtOrBelowIncoming(current))
            throw new InvalidOperationException("Core-Pfad liegt auÃŸerhalb von Incoming.");

        return current[(root.Length + 1)..];
    }

    public sealed record CoreDirectoryChoice(string Name, string FullPath);
}