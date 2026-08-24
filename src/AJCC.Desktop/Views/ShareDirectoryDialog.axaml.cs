using System.Collections.ObjectModel;
using AJCC.Core.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class ShareDirectoryDialog : Window
{
    private readonly Stack<string?> _history = new();
    private IReadOnlyList<AjShareDirectory> _sharedDirectories = Array.Empty<AjShareDirectory>();
    private Func<string?, Task<AjDirectoryListResult>>? _loadDirectoryAsync;
    private string? _currentDirectoryParameter;
    private char _separator = '\\';
    private bool _initialLoadStarted;

    public ShareDirectoryDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += ShareDirectoryDialog_OnOpened;
    }

    public ShareDirectoryDialog(
        IReadOnlyList<AjShareDirectory> sharedDirectories,
        Func<string?, Task<AjDirectoryListResult>> loadDirectoryAsync)
        : this()
    {
        _sharedDirectories = sharedDirectories ?? Array.Empty<AjShareDirectory>();
        _loadDirectoryAsync = loadDirectoryAsync ?? throw new ArgumentNullException(nameof(loadDirectoryAsync));
    }

    public ObservableCollection<ShareDirectoryChoice> Directories { get; } = new();

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private async void ShareDirectoryDialog_OnOpened(object? sender, EventArgs e)
    {
        if (_initialLoadStarted)
            return;

        _initialLoadStarted = true;
        await LoadDirectoryAsync(null);
    }

    private async Task<bool> LoadDirectoryAsync(string? directoryParameter)
    {
        TextBlock? status = this.FindControl<TextBlock>("DirectoryStatusText");
        TextBlock? currentPath = this.FindControl<TextBlock>("CurrentPathText");

        if (_loadDirectoryAsync is null)
        {
            if (status is not null)
                status.Text = "Core-Verzeichnisbrowser ist nicht verfügbar.";
            return false;
        }

        try
        {
            if (status is not null)
                status.Text = "Lade Core-Verzeichnis …";

            AjDirectoryListResult result = await _loadDirectoryAsync(directoryParameter);
            if (!string.IsNullOrWhiteSpace(result.Separator))
            {
                char separator = result.Separator.Trim()[0];
                if (separator is '/' or '\\')
                    _separator = separator;
            }

            _currentDirectoryParameter = string.IsNullOrWhiteSpace(directoryParameter)
                ? null
                : directoryParameter.Trim();

            Directories.Clear();
            foreach (AjDirectoryEntry entry in result.Directories
                         .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                         .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                string fullPath = !string.IsNullOrWhiteSpace(entry.Path)
                    ? entry.Path.Trim()
                    : BuildCorePath(_currentDirectoryParameter, entry.Name);

                Directories.Add(new ShareDirectoryChoice(
                    entry.Name.Trim(),
                    fullPath,
                    GetShareStatus(fullPath),
                    CanHaveChildren(entry)));
            }

            if (currentPath is not null)
                currentPath.Text = _currentDirectoryParameter ?? "Core-Dateisystem";

            if (status is not null)
            {
                status.Text = Directories.Count == 0
                    ? "Keine Unterverzeichnisse vorhanden."
                    : $"{Directories.Count:N0} Verzeichnisse geladen. Konfigurierte Freigaben sind rechts markiert.";
            }

            return true;
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Core-Verzeichnis konnte nicht geladen werden: " + ex.Message;
            return false;
        }
    }

    private async void OpenDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ListBox? list = this.FindControl<ListBox>("DirectoryList");
        if (list?.SelectedItem is not ShareDirectoryChoice selected)
        {
            SetStatus("Bitte zuerst ein Verzeichnis auswählen.");
            return;
        }

        if (!selected.CanOpen)
        {
            SetStatus("Dieses Element kann nicht als Verzeichnis geöffnet werden.");
            return;
        }

        string? previous = _currentDirectoryParameter;
        if (await LoadDirectoryAsync(selected.FullPath))
            _history.Push(previous);
    }

    private async void UpDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_history.Count == 0)
        {
            SetStatus("Die oberste Core-Ebene ist bereits erreicht.");
            return;
        }

        string? previous = _history.Peek();
        if (await LoadDirectoryAsync(previous))
            _history.Pop();
    }

    private async void ReloadDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
        => await LoadDirectoryAsync(_currentDirectoryParameter);

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void SetStatus(string text)
    {
        TextBlock? status = this.FindControl<TextBlock>("DirectoryStatusText");
        if (status is not null)
            status.Text = text;
    }

    private string GetShareStatus(string path)
    {
        AjShareDirectory? configured = _sharedDirectories.FirstOrDefault(
            directory => PathsEqual(directory.Name, path));
        if (configured is null)
            return string.Empty;

        return configured.ShareMode.Equals("subdirectory", StringComparison.OrdinalIgnoreCase)
            ? "Freigegeben inkl. Unterordner"
            : "Freigegeben – nur dieser Ordner";
    }

    private bool PathsEqual(string left, string right)
    {
        StringComparison comparison = _separator == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(NormalizePath(left), NormalizePath(right), comparison);
    }

    private string NormalizePath(string value)
    {
        string path = (value ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0)
            return string.Empty;

        char other = _separator == '/' ? '\\' : '/';
        path = path.Replace(other, _separator);

        if (_separator == '/' && path == "/")
            return path;
        if (_separator == '\\'
            && path.Length == 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && path[2] == '\\')
        {
            return path;
        }

        return path.TrimEnd(_separator);
    }

    private string BuildCorePath(string? parentPath, string name)
    {
        string parent = (parentPath ?? string.Empty).Trim().Trim('"');
        string child = (name ?? string.Empty).Trim().Trim('"');
        if (parent.Length == 0)
            return child;
        if (child.Length == 0)
            return parent;

        return parent[^1] == _separator
            ? parent + child
            : parent + _separator + child;
    }

    private static bool CanHaveChildren(AjDirectoryEntry entry)
        => entry.IsFileSystem || entry.Type is 1 or 2 or 4 or 5;

    public sealed record ShareDirectoryChoice(
        string Name,
        string FullPath,
        string ShareStatus,
        bool CanOpen);
}
