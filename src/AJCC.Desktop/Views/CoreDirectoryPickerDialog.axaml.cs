using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AJCC.Core.Helpers;
using AJCC.Core.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class CoreDirectoryPickerDialog : Window
{
    private Func<string?, Task<AjDirectoryListResult>>? _loadDirectoryAsync;
    private bool _initialLoadStarted;
    private char _separator = '\\';

    public CoreDirectoryPickerDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += CoreDirectoryPickerDialog_OnOpened;
    }

    public CoreDirectoryPickerDialog(
        Func<string?, Task<AjDirectoryListResult>> loadDirectoryAsync)
        : this()
    {
        _loadDirectoryAsync = loadDirectoryAsync
            ?? throw new ArgumentNullException(nameof(loadDirectoryAsync));
    }

    public ObservableCollection<CoreDirectoryNode> RootDirectories { get; } = new();
    public string SelectedDirectory { get; private set; } = string.Empty;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private async void CoreDirectoryPickerDialog_OnOpened(object? sender, EventArgs e)
    {
        if (_initialLoadStarted)
            return;

        _initialLoadStarted = true;
        await LoadRootDirectoriesAsync();
    }

    private async Task LoadRootDirectoriesAsync()
    {
        if (_loadDirectoryAsync is null)
        {
            SetStatus("Core-Verzeichnisbrowser ist nicht verfügbar.");
            return;
        }

        try
        {
            SetStatus("Lade Core-Verzeichnisbaum ...");
            AjDirectoryListResult result = await _loadDirectoryAsync(null);
            UpdateSeparator(result);

            RootDirectories.Clear();
            foreach (CoreDirectoryNode node in CreateNodes(result, null))
                RootDirectories.Add(node);

            SetStatus(RootDirectories.Count == 0
                ? "Der Core hat keine Verzeichnisse gemeldet."
                : $"{RootDirectories.Count:N0} Stammverzeichnisse geladen. Unterordner werden beim Aufklappen nachgeladen.");
        }
        catch (Exception ex)
        {
            SetStatus("Core-Verzeichnisbaum konnte nicht geladen werden: " + ex.Message);
        }
    }

    private async Task LoadChildrenAsync(CoreDirectoryNode node)
    {
        if (_loadDirectoryAsync is null)
            return;

        try
        {
            SetStatus($"Lade {node.FullPath} ...");
            AjDirectoryListResult result = await _loadDirectoryAsync(node.FullPath);
            UpdateSeparator(result);

            node.Children.Clear();
            foreach (CoreDirectoryNode child in CreateNodes(result, node.FullPath))
                node.Children.Add(child);
            node.IsLoaded = true;

            SetStatus(node.Children.Count == 0
                ? $"{node.FullPath}: keine Unterverzeichnisse."
                : $"{node.FullPath}: {node.Children.Count:N0} Unterverzeichnisse geladen.");
        }
        catch (Exception ex)
        {
            SetStatus($"Core-Verzeichnis '{node.FullPath}' konnte nicht geladen werden: {ex.Message}");
        }
    }

    private List<CoreDirectoryNode> CreateNodes(
        AjDirectoryListResult result,
        string? parentPath)
    {
        return result.Directories
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .OrderBy(entry => entry.Name, NaturalStringComparer.Instance)
            .ThenBy(entry => entry.Path ?? string.Empty, NaturalStringComparer.Instance)
            .Select(entry =>
            {
                string fullPath = !string.IsNullOrWhiteSpace(entry.Path)
                    ? entry.Path.Trim()
                    : BuildCorePath(parentPath, entry.Name.Trim());
                bool canOpen = entry.IsFileSystem || entry.Type is 1 or 2 or 4 or 5;
                return new CoreDirectoryNode(
                    entry.Name.Trim(),
                    fullPath,
                    canOpen,
                    LoadChildrenAsync);
            })
            .Where(node => !string.IsNullOrWhiteSpace(node.FullPath))
            .ToList();
    }

    private void UpdateSeparator(AjDirectoryListResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Separator))
            return;

        char separator = result.Separator.Trim()[0];
        if (separator is '/' or '\\')
            _separator = separator;
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

    private async void ReloadButton_OnClick(object? sender, RoutedEventArgs e)
        => await LoadRootDirectoriesAsync();

    private void AcceptButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TreeView? tree = this.FindControl<TreeView>("DirectoryTree");
        if (tree?.SelectedItem is not CoreDirectoryNode selected
            || selected.IsPlaceholder)
        {
            SetStatus("Bitte zuerst ein Core-Verzeichnis auswählen.");
            return;
        }

        string path = selected.FullPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl))
        {
            SetStatus("Der ausgewählte Core-Pfad ist strukturell ungültig.");
            return;
        }

        SelectedDirectory = path;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void SetStatus(string text)
    {
        TextBlock? status = this.FindControl<TextBlock>("DirectoryStatusText");
        if (status is not null)
            status.Text = text;
    }

    public sealed class CoreDirectoryNode : INotifyPropertyChanged
    {
        private readonly Func<CoreDirectoryNode, Task>? _loadChildrenAsync;
        private bool _isExpanded;

        public CoreDirectoryNode(
            string name,
            string fullPath,
            bool canOpen,
            Func<CoreDirectoryNode, Task>? loadChildrenAsync,
            bool isPlaceholder = false)
        {
            Name = name;
            FullPath = fullPath;
            CanOpen = canOpen;
            _loadChildrenAsync = loadChildrenAsync;
            IsPlaceholder = isPlaceholder;

            if (CanOpen && !IsPlaceholder)
                Children.Add(CreatePlaceholder());
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; }
        public string FullPath { get; }
        public bool CanOpen { get; }
        public bool IsPlaceholder { get; }
        public bool IsVisibleInTree => !IsPlaceholder;
        public bool IsLoaded { get; set; }
        public bool IsLoading { get; private set; }
        public ObservableCollection<CoreDirectoryNode> Children { get; } = new();

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                OnPropertyChanged();

                if (value && CanOpen && !IsLoaded && !IsLoading && _loadChildrenAsync is not null)
                    _ = EnsureChildrenLoadedAsync();
            }
        }

        private async Task EnsureChildrenLoadedAsync()
        {
            IsLoading = true;
            try
            {
                if (_loadChildrenAsync is not null)
                    await _loadChildrenAsync(this);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static CoreDirectoryNode CreatePlaceholder()
            => new("Lade ...", string.Empty, false, null, true);

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
