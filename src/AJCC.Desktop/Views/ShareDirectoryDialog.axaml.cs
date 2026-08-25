using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AJCC.Core.Helpers;
using AJCC.Core.Models;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class ShareDirectoryDialog : Window
{
    private IReadOnlyList<AjShareDirectory> _configuredDirectories = Array.Empty<AjShareDirectory>();
    private List<AjShareDirectory> _draftDirectories = new();
    private Func<string?, Task<AjDirectoryListResult>>? _loadDirectoryAsync;
    private Func<IReadOnlyList<AjShareDirectory>, Task<IReadOnlyList<AjShareDirectory>>>? _transferDirectoriesAsync;
    private char _separator = '\\';
    private bool _initialLoadStarted;
    private bool _transferRunning;

    public ShareDirectoryDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += ShareDirectoryDialog_OnOpened;
    }

    public ShareDirectoryDialog(
        IReadOnlyList<AjShareDirectory> sharedDirectories,
        Func<string?, Task<AjDirectoryListResult>> loadDirectoryAsync,
        Func<IReadOnlyList<AjShareDirectory>, Task<IReadOnlyList<AjShareDirectory>>> transferDirectoriesAsync)
        : this()
    {
        _configuredDirectories = CloneDirectories(sharedDirectories);
        _draftDirectories = CloneDirectories(_configuredDirectories);
        _loadDirectoryAsync = loadDirectoryAsync ?? throw new ArgumentNullException(nameof(loadDirectoryAsync));
        _transferDirectoriesAsync = transferDirectoriesAsync ?? throw new ArgumentNullException(nameof(transferDirectoriesAsync));
    }

    public ObservableCollection<ShareDirectoryTreeNode> RootDirectories { get; } = new();
    public IReadOnlyList<AjShareDirectory> DraftDirectories => _draftDirectories;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private async void ShareDirectoryDialog_OnOpened(object? sender, EventArgs e)
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
            SetStatus("Lade Core-Verzeichnisbaum …");
            AjDirectoryListResult result = await _loadDirectoryAsync(null);
            UpdateSeparator(result);

            List<ShareDirectoryTreeNode> roots = CreateNodes(result, null);
            RootDirectories.Clear();
            foreach (ShareDirectoryTreeNode root in roots)
                RootDirectories.Add(root);

            TreeView? tree = this.FindControl<TreeView>("DirectoryTree");
            if (tree is not null)
                tree.SelectedItem = null;

            SetCurrentPath("Core-Dateisystem");
            SetStatus(roots.Count == 0
                ? "Keine Core-Verzeichnisse vorhanden. Änderungen bleiben nur im lokalen Entwurf."
                : $"{roots.Count:N0} Stammverzeichnisse geladen. Unterordner werden beim Aufklappen nachgeladen; es erfolgt noch keine Core-Übertragung.");
        }
        catch (Exception ex)
        {
            SetStatus("Core-Verzeichnisbaum konnte nicht geladen werden: " + ex.Message);
        }
    }

    private async Task LoadChildrenAsync(ShareDirectoryTreeNode node)
    {
        if (_loadDirectoryAsync is null || node.IsLoaded)
            return;

        try
        {
            SetStatus($"Lade {node.FullPath} …");
            AjDirectoryListResult result = await _loadDirectoryAsync(node.FullPath);
            UpdateSeparator(result);

            List<ShareDirectoryTreeNode> children = CreateNodes(result, node.FullPath);
            node.Children.Clear();
            foreach (ShareDirectoryTreeNode child in children)
                node.Children.Add(child);
            node.IsLoaded = true;

            SetStatus(children.Count == 0
                ? $"{node.FullPath}: keine Unterverzeichnisse."
                : $"{node.FullPath}: {children.Count:N0} Unterverzeichnisse geladen.");
        }
        catch (Exception ex)
        {
            SetStatus($"Core-Verzeichnis '{node.FullPath}' konnte nicht geladen werden: {ex.Message}");
        }
    }

    private List<ShareDirectoryTreeNode> CreateNodes(AjDirectoryListResult result, string? parentPath)
        => result.Directories
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                string fullPath = !string.IsNullOrWhiteSpace(entry.Path)
                    ? entry.Path.Trim()
                    : BuildCorePath(parentPath, entry.Name);
                return new ShareDirectoryTreeNode(
                    entry.Name.Trim(),
                    fullPath,
                    GetShareStatus(fullPath),
                    CanHaveChildren(entry),
                    LoadChildrenAsync);
            })
            .ToList();

    private void UpdateSeparator(AjDirectoryListResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Separator))
            return;

        char separator = result.Separator.Trim()[0];
        if (separator is '/' or '\\')
            _separator = separator;
    }

    private void DirectoryItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ShareDirectoryTreeNode selected }
            || selected.IsPlaceholder)
        {
            return;
        }

        SelectDirectory(selected);
        if (e.ClickCount != 2)
            return;

        e.Handled = true;
        if (!selected.CanOpen)
        {
            SetStatus("Dieses Element kann nicht als Verzeichnis geöffnet werden.");
            return;
        }

        selected.IsExpanded = !selected.IsExpanded;
    }

    private void DirectoryItem_OnRightTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ShareDirectoryTreeNode selected }
            || selected.IsPlaceholder)
        {
            return;
        }

        SelectDirectory(selected);
    }

    private void OpenDirectoryMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedDirectory(out ShareDirectoryTreeNode selected))
            return;

        if (!selected.CanOpen)
        {
            SetStatus("Dieses Element kann nicht als Verzeichnis geöffnet werden.");
            return;
        }

        selected.IsExpanded = true;
    }

    private async void ReloadDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
        => await LoadRootDirectoriesAsync();

    private void AddRecursiveShareButton_OnClick(object? sender, RoutedEventArgs e)
        => ApplySelectedShareDraft(ShareDirectoryDraftSemantics.RecursiveShareMode);

    private void AddSingleShareButton_OnClick(object? sender, RoutedEventArgs e)
        => ApplySelectedShareDraft(ShareDirectoryDraftSemantics.SingleDirectoryShareMode);

    private void AddRecursiveShareMenuItem_OnClick(object? sender, RoutedEventArgs e)
        => ApplySelectedShareDraft(ShareDirectoryDraftSemantics.RecursiveShareMode);

    private void AddSingleShareMenuItem_OnClick(object? sender, RoutedEventArgs e)
        => ApplySelectedShareDraft(ShareDirectoryDraftSemantics.SingleDirectoryShareMode);

    private void ApplySelectedShareDraft(string shareMode)
    {
        if (!TryGetSelectedDirectory(out ShareDirectoryTreeNode selected))
            return;

        ShareDirectoryDraftResult result = ShareDirectoryDraftSemantics.Apply(
            _draftDirectories,
            selected.FullPath,
            shareMode);

        if (result.BlockedByRecursiveAncestor)
        {
            SetStatus($"Nicht vorgemerkt: bereits durch rekursive Freigabe '{result.BlockingAncestorPath}' abgedeckt.");
            return;
        }

        _draftDirectories = CloneDirectories(result.Directories);
        RefreshVisibleShareStatuses();

        string modeText = shareMode == ShareDirectoryDraftSemantics.RecursiveShareMode
            ? "mit Unterordnern"
            : "nur dieser Ordner";
        string removedText = result.RemovedRedundantCount > 0
            ? $" {result.RemovedRedundantCount:N0} redundante untergeordnete Vormerkung(en) wurden entfernt."
            : string.Empty;
        SetStatus($"Lokal vorgemerkt: {selected.FullPath} – {modeText}.{removedText} Noch nicht an den Core übertragen.");
    }

    private void RemoveShareDraftButton_OnClick(object? sender, RoutedEventArgs e)
        => RemoveSelectedShareDraft();

    private void RemoveShareDraftMenuItem_OnClick(object? sender, RoutedEventArgs e)
        => RemoveSelectedShareDraft();

    private void RemoveSelectedShareDraft()
    {
        if (!TryGetSelectedDirectory(out ShareDirectoryTreeNode selected))
            return;

        AjShareDirectory? exact = _draftDirectories.FirstOrDefault(
            directory => PathsEqual(directory.Name, selected.FullPath));
        if (exact is null)
        {
            SetStatus("Für dieses Verzeichnis gibt es im lokalen Entwurf keine exakte Vormerkung.");
            return;
        }

        _draftDirectories.Remove(exact);
        RefreshVisibleShareStatuses();
        SetStatus($"Lokal zum Entfernen vorgemerkt: {selected.FullPath}. Noch nicht an den Core übertragen.");
    }

    private async void ApplyToCoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_transferRunning)
            return;
        if (_transferDirectoriesAsync is null)
        {
            SetStatus("Core-Übertragung ist in diesem Dialog nicht verfügbar.");
            return;
        }
        if (!HasPendingChanges())
        {
            SetStatus("Keine lokalen Änderungen zum Übertragen.");
            return;
        }

        List<AjShareDirectory> transferDirectories = CloneDirectories(
            ShareDirectoryDraftSemantics.Normalize(_draftDirectories));
        string message = transferDirectories.Count == 0
            ? "Der lokale Entwurf enthält keine Freigaben. Dadurch werden alle Share-Verzeichnisse im Core entfernt. Wirklich übertragen?"
            : $"{transferDirectories.Count:N0} Share-Verzeichnis(se) aus dem lokalen Entwurf an den Core übertragen?";

        ConfirmDialog confirm = new(
            "Share-Verzeichnisse übertragen",
            message,
            "An Core übertragen",
            "Abbrechen");
        if (!await confirm.ShowDialog<bool>(this))
            return;

        _transferRunning = true;
        SetTransferBusy(true);
        SetStatus($"Übertrage {transferDirectories.Count:N0} Share-Verzeichnis(se) an den Core …");

        try
        {
            IReadOnlyList<AjShareDirectory> effectiveDirectories =
                await _transferDirectoriesAsync(transferDirectories);
            _configuredDirectories = CloneDirectories(effectiveDirectories);
            _draftDirectories = CloneDirectories(_configuredDirectories);
            RefreshVisibleShareStatuses();
            SetStatus($"Core-Übertragung abgeschlossen: {_configuredDirectories.Count:N0} Share-Verzeichnis(se) aktiv.");
        }
        catch (Exception ex)
        {
            SetStatus("Core-Übertragung fehlgeschlagen. Der lokale Entwurf bleibt erhalten: " + ex.Message);
        }
        finally
        {
            _transferRunning = false;
            SetTransferBusy(false);
        }
    }

    private bool HasPendingChanges()
    {
        IReadOnlyList<AjShareDirectory> configured =
            ShareDirectoryDraftSemantics.Normalize(_configuredDirectories);
        IReadOnlyList<AjShareDirectory> draft =
            ShareDirectoryDraftSemantics.Normalize(_draftDirectories);

        if (configured.Count != draft.Count)
            return true;

        return draft.Any(candidate =>
            !configured.Any(existing =>
                PathsEqual(existing.Name, candidate.Name)
                && existing.ShareMode.Equals(candidate.ShareMode, StringComparison.OrdinalIgnoreCase)));
    }

    private void SetTransferBusy(bool busy)
    {
        Grid? root = this.FindControl<Grid>("DialogRoot");
        if (root is not null)
            root.IsEnabled = !busy;
    }

    private bool TryGetSelectedDirectory(out ShareDirectoryTreeNode selected)
    {
        TreeView? tree = this.FindControl<TreeView>("DirectoryTree");
        if (tree?.SelectedItem is ShareDirectoryTreeNode node && !node.IsPlaceholder)
        {
            selected = node;
            return true;
        }

        selected = null!;
        SetStatus("Bitte zuerst ein Verzeichnis auswählen.");
        return false;
    }

    private void SelectDirectory(ShareDirectoryTreeNode selected)
    {
        TreeView? tree = this.FindControl<TreeView>("DirectoryTree");
        if (tree is not null)
            tree.SelectedItem = selected;

        SetCurrentPath(FormatBreadcrumb(selected.FullPath));
    }

    private void RefreshVisibleShareStatuses()
    {
        foreach (ShareDirectoryTreeNode root in RootDirectories)
            RefreshNodeShareStatuses(root);
    }

    private void RefreshNodeShareStatuses(ShareDirectoryTreeNode node)
    {
        if (!node.IsPlaceholder)
            node.ShareStatus = GetShareStatus(node.FullPath);

        foreach (ShareDirectoryTreeNode child in node.Children)
            RefreshNodeShareStatuses(child);
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void SetStatus(string text)
    {
        TextBlock? status = this.FindControl<TextBlock>("DirectoryStatusText");
        if (status is not null)
            status.Text = text;
    }

    private void SetCurrentPath(string text)
    {
        TextBlock? currentPath = this.FindControl<TextBlock>("CurrentPathText");
        if (currentPath is not null)
            currentPath.Text = text;
    }

    private string FormatBreadcrumb(string path)
    {
        string normalized = NormalizePath(path);
        if (normalized.Length == 0)
            return "Core-Dateisystem";

        if (_separator == '/')
        {
            if (normalized == "/")
                return "Core-Dateisystem › /";

            string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return "Core-Dateisystem › / › " + string.Join(" › ", parts);
        }

        string prefix = normalized.StartsWith("\\", StringComparison.Ordinal) ? "\\" : string.Empty;
        string[] windowsParts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return "Core-Dateisystem › " + prefix + string.Join(" › ", windowsParts);
    }

    private string GetShareStatus(string path)
    {
        AjShareDirectory? configured = _configuredDirectories.FirstOrDefault(
            directory => PathsEqual(directory.Name, path));
        AjShareDirectory? draft = _draftDirectories.FirstOrDefault(
            directory => PathsEqual(directory.Name, path));

        if (draft is not null)
        {
            string draftModeText = FormatShareMode(draft.ShareMode);
            if (configured is null)
                return "Entwurf: " + draftModeText;

            if (configured.ShareMode.Equals(draft.ShareMode, StringComparison.OrdinalIgnoreCase))
                return "Aktiv: " + draftModeText;

            return "Entwurf: Modus → " + draftModeText;
        }

        if (configured is not null)
            return "Entwurf: entfernen";

        if (ShareDirectoryDraftSemantics.TryGetRecursiveAncestor(
                _draftDirectories,
                path,
                out string ancestorPath))
        {
            return "Abgedeckt durch " + ancestorPath;
        }

        return string.Empty;
    }

    private static string FormatShareMode(string shareMode)
        => shareMode.Equals(ShareDirectoryDraftSemantics.RecursiveShareMode, StringComparison.OrdinalIgnoreCase)
            ? "inkl. Unterordner"
            : "nur dieser Ordner";

    private static List<AjShareDirectory> CloneDirectories(IEnumerable<AjShareDirectory>? directories)
        => directories?
            .Where(directory => directory is not null)
            .Select(directory => new AjShareDirectory
            {
                Name = directory.Name ?? string.Empty,
                ShareMode = directory.ShareMode ?? string.Empty
            })
            .ToList()
            ?? new List<AjShareDirectory>();

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

    public sealed class ShareDirectoryTreeNode : INotifyPropertyChanged
    {
        private readonly Func<ShareDirectoryTreeNode, Task>? _loadChildrenAsync;
        private bool _isExpanded;
        private string _shareStatus;

        public ShareDirectoryTreeNode(
            string name,
            string fullPath,
            string shareStatus,
            bool canOpen,
            Func<ShareDirectoryTreeNode, Task>? loadChildrenAsync,
            bool isPlaceholder = false)
        {
            Name = name;
            FullPath = fullPath;
            _shareStatus = shareStatus;
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
        public bool IsLoaded { get; set; }
        public bool IsLoading { get; private set; }
        public ObservableCollection<ShareDirectoryTreeNode> Children { get; } = new();

        public string ShareStatus
        {
            get => _shareStatus;
            set
            {
                if (_shareStatus == value)
                    return;

                _shareStatus = value;
                OnPropertyChanged();
            }
        }

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

        private static ShareDirectoryTreeNode CreatePlaceholder()
            => new("Lade …", string.Empty, string.Empty, false, null, true);

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
