using System.Collections.ObjectModel;
using AJCC.Core.Helpers;
using AJCC.Core.Models;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class ShareDirectoryDialog : Window
{
    private readonly Stack<string?> _history = new();
    private IReadOnlyList<AjShareDirectory> _configuredDirectories = Array.Empty<AjShareDirectory>();
    private List<AjShareDirectory> _draftDirectories = new();
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
        _configuredDirectories = CloneDirectories(sharedDirectories);
        _draftDirectories = CloneDirectories(_configuredDirectories);
        _loadDirectoryAsync = loadDirectoryAsync ?? throw new ArgumentNullException(nameof(loadDirectoryAsync));
    }

    public ObservableCollection<ShareDirectoryChoice> Directories { get; } = new();
    public IReadOnlyList<AjShareDirectory> DraftDirectories => _draftDirectories;

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
                    ? "Keine Unterverzeichnisse vorhanden. Änderungen bleiben nur im lokalen Entwurf."
                    : $"{Directories.Count:N0} Verzeichnisse geladen. Aktiv- und Entwurfsstatus stehen rechts; es erfolgt noch keine Core-Übertragung.";
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
        if (!TryGetSelectedDirectory(out ShareDirectoryChoice selected))
            return;

        await OpenDirectoryAsync(selected);
    }

    private async void DirectoryItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2
            || sender is not Control { DataContext: ShareDirectoryChoice selected })
            return;

        SelectDirectory(selected);
        e.Handled = true;
        await OpenDirectoryAsync(selected);
    }

    private void DirectoryItem_OnRightTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ShareDirectoryChoice selected })
            return;

        SelectDirectory(selected);
    }

    private async void OpenDirectoryMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedDirectory(out ShareDirectoryChoice selected))
            return;

        await OpenDirectoryAsync(selected);
    }

    private async Task OpenDirectoryAsync(ShareDirectoryChoice selected)
    {
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
        if (!TryGetSelectedDirectory(out ShareDirectoryChoice selected))
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
        if (!TryGetSelectedDirectory(out ShareDirectoryChoice selected))
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

    private bool TryGetSelectedDirectory(out ShareDirectoryChoice selected)
    {
        ListBox? list = this.FindControl<ListBox>("DirectoryList");
        if (list?.SelectedItem is ShareDirectoryChoice choice)
        {
            selected = choice;
            return true;
        }

        selected = null!;
        SetStatus("Bitte zuerst ein Verzeichnis auswählen.");
        return false;
    }

    private void SelectDirectory(ShareDirectoryChoice selected)
    {
        ListBox? list = this.FindControl<ListBox>("DirectoryList");
        if (list is not null)
            list.SelectedItem = selected;
    }

    private void RefreshVisibleShareStatuses()
    {
        for (int index = 0; index < Directories.Count; index++)
        {
            ShareDirectoryChoice choice = Directories[index];
            Directories[index] = choice with { ShareStatus = GetShareStatus(choice.FullPath) };
        }
    }

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

    public sealed record ShareDirectoryChoice(
        string Name,
        string FullPath,
        string ShareStatus,
        bool CanOpen);
}
