using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using AJCC.Core.Links;
using AJCC.Core.Models;
using AJCC.Desktop.ViewModels;

namespace AJCC.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private AjServer? _selectedServerForContext;
    private AjUserSource? _selectedDownloadSourceForContext;
    private int _embeddedPartListRequestVersion;
    private long _embeddedPartListDownloadId;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        AddHandler(
            InputElement.PointerPressedEvent,
            MainWindow_OnPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.ContextRequestedEvent,
            MainWindow_OnContextRequested,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Closed += MainWindow_OnClosed;
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private async void ConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? passwordInput = this.FindControl<TextBox>("PasswordInput");
        string password = passwordInput?.Text ?? string.Empty;

        try
        {
            await _viewModel.ToggleConnectionAsync(password);
        }
        finally
        {
            if (passwordInput is not null)
                passwordInput.Text = string.Empty;
        }
    }

    private async void PauseDownloadButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.PauseSelectedDownloadAsync();

    private async void ResumeDownloadButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ResumeSelectedDownloadAsync();

    private async void CancelDownloadButton_OnClick(object? sender, RoutedEventArgs e)
        => await ConfirmAndCancelSelectedDownloadAsync();

    private async void SearchButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.StartSearchAsync();

    private async void DownloadSearchResultButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.DownloadSelectedSearchEntryAsync();

    private void MainWindow_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        if (e.Source is not Control source)
            return;

        switch (source.DataContext)
        {
            case AjDownload download:
                _viewModel.SelectedDownload = download;
                break;
            case AjUserSource userSource:
                _selectedDownloadSourceForContext = userSource;
                break;
            case AjSearchEntry entry:
                _viewModel.SelectedSearchEntry = entry;
                break;
            case AjServer server:
                _selectedServerForContext = server;
                break;
        }
    }

    private void MainWindow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control source)
            return;

        ListBoxItem? item = source.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item is null)
            return;

        bool isDownloadRow;
        switch (item.DataContext)
        {
            case AjDownload download:
                _viewModel.SelectedDownload = download;
                isDownloadRow = true;
                break;
            case AjUserSource userSource:
                _selectedDownloadSourceForContext = userSource;
                isDownloadRow = false;
                break;
            case AjSearchEntry entry:
                _viewModel.SelectedSearchEntry = entry;
                isDownloadRow = false;
                break;
            case AjServer server:
                _selectedServerForContext = server;
                isDownloadRow = false;
                break;
            default:
                return;
        }

        foreach (var descendant in item.GetVisualDescendants())
        {
            if (descendant is not Control { ContextMenu: { } menu } contextHost)
                continue;

            if (isDownloadRow)
                ConfigureDownloadContextMenu(menu);

            menu.Open(contextHost);
            e.Handled = true;
            return;
        }
    }

    private void ConfigureDownloadContextMenu(ContextMenu menu)
    {
        bool canPause = _viewModel.CanPauseSelectedDownload;
        bool canResume = _viewModel.CanResumeSelectedDownload;
        bool canCancel = _viewModel.CanCancelSelectedDownload;
        bool canClean = _viewModel.CanCleanDownloadList;
        bool canRename = _viewModel.CanRenameSelectedDownload;
        bool canSetTargetDirectory = _viewModel.CanSetTargetDirectorySelectedDownload;
        bool canSetPowerDownload = _viewModel.CanSetPowerDownloadSelectedDownload;
        bool canShowPartList = _viewModel.CanShowSelectedDownloadPartList;
        bool hasControlActions = canPause || canResume || canCancel || canClean;
        bool hasMetadataActions = canRename || canSetTargetDirectory || canSetPowerDownload || canShowPartList;
        int separatorIndex = 0;

        foreach (object? rawItem in menu.Items)
        {
            if (rawItem is MenuItem item)
            {
                string header = item.Header?.ToString() ?? string.Empty;
                item.IsVisible = header switch
                {
                    "Pausieren" => canPause,
                    "Fortsetzen" => canResume,
                    "Download abbrechen…" => canCancel,
                    "Fertige/abgebrochene Downloads entfernen" => canClean,
                    "Umbenennen…" => canRename,
                    "Zielverzeichnis setzen…" => canSetTargetDirectory,
                    "Powerdownload setzen…" => canSetPowerDownload,
                    "Powerdownload löschen" => canSetPowerDownload,
                    "Partliste anzeigen…" => canShowPartList,
                    _ => true
                };
                continue;
            }

            if (rawItem is Separator separator)
            {
                separator.IsVisible = separatorIndex switch
                {
                    0 => hasControlActions && hasMetadataActions,
                    1 => hasControlActions || hasMetadataActions,
                    _ => true
                };
                separatorIndex++;
            }
        }
    }

    private void DownloadRow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: AjDownload download })
            _viewModel.SelectedDownload = download;
    }

    private void SearchResultRow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: AjSearchEntry entry })
            _viewModel.SelectedSearchEntry = entry;
    }

    private void ServerRow_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: AjServer server })
            _selectedServerForContext = server;
    }

    private async void ServerContextLogin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedServerForContext is not { } server || !_viewModel.IsConnected || _viewModel.IsBusy)
            return;

        bool rapidSwitchConfirmed = false;
        var evaluation = _viewModel.EvaluateServerLogin(server);
        if (evaluation.RequiresConfirmation)
        {
            int waitMinutes = Math.Max(1, (int)Math.Ceiling(evaluation.RecommendedWait.TotalMinutes));
            string connectedForText = evaluation.ConnectedFor.TotalMinutes >= 1
                ? $"{(int)evaluation.ConnectedFor.TotalMinutes} Min. {evaluation.ConnectedFor.Seconds} Sek."
                : $"{Math.Max(0, (int)evaluation.ConnectedFor.TotalSeconds)} Sek.";

            ConfirmDialog dialog = new(
                "Serverwechsel-Warnung",
                "Der Core ist noch keine 30 Minuten mit dem aktuellen Server verbunden.\n\n" +
                $"Zielserver: {server.Name} / ID {server.Id}\n" +
                $"Aktuelle Verbindungsdauer: {connectedForText}\n\n" +
                "Zu viele Serverwechsel in kurzer Zeit können eine 30-Minuten-Reconnect-Sperre auslösen.\n" +
                $"Empfehlung: noch ca. {waitMinutes} Minuten warten.\n\n" +
                "Trotzdem jetzt einen Serverlogin versuchen?",
                "Trotzdem verbinden",
                "Abbrechen");

            bool confirmed = await dialog.ShowDialog<bool>(this);
            if (!confirmed)
                return;

            rapidSwitchConfirmed = true;
        }

        await _viewModel.LoginServerAsync(server, rapidSwitchConfirmed);
    }

    private async void ServerContextRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedServerForContext is { } server)
            await _viewModel.RemoveServerAsync(server);
    }

    private async void DownloadContextPause_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.PauseSelectedDownloadAsync();

    private async void DownloadContextResume_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ResumeSelectedDownloadAsync();

    private async void DownloadContextCancel_OnClick(object? sender, RoutedEventArgs e)
        => await ConfirmAndCancelSelectedDownloadAsync();

    private async void DownloadContextClean_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.CleanTerminalDownloadsAsync();

    private async void DownloadContextRename_OnClick(object? sender, RoutedEventArgs e)
        => await RenameSelectedDownloadWithDialogAsync();

    private async void DownloadContextSetTargetDirectory_OnClick(object? sender, RoutedEventArgs e)
        => await SetSelectedDownloadTargetDirectoryWithDialogAsync();

    private async void DownloadContextSetPowerDownload_OnClick(object? sender, RoutedEventArgs e)
        => await SetSelectedDownloadPowerDownloadWithDialogAsync();

    private async void DownloadContextClearPowerDownload_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ClearSelectedDownloadPowerDownloadAsync();

    private async void DownloadContextShowPartList_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await _viewModel.LoadSelectedDownloadPartListAsync();
        if (!result.HasValue)
            return;

        var partList = result.Value;
        PartListDialog dialog = new(
            partList.Filename,
            partList.FileSize,
            partList.Parts,
            partList.SourcePartListCount,
            partList.SourceCandidateCount,
            partList.SourceErrorCount);
        await dialog.ShowDialog<bool>(this);
    }

    private async void DownloadsList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int requestVersion = ++_embeddedPartListRequestVersion;
        AjDownload? selected = (sender as ListBox)?.SelectedItem as AjDownload;
        _viewModel.SelectedDownload = selected;
        if (selected is null)
        {
            _embeddedPartListDownloadId = 0;
            ClearEmbeddedPartList("Markiere einen Download, um die Partliste zu laden.");
            return;
        }

        if (_embeddedPartListDownloadId == selected.Id)
            return;

        _embeddedPartListDownloadId = 0;
        ClearEmbeddedPartList("Partliste wird geladen…");
        await Task.Delay(140);
        if (requestVersion != _embeddedPartListRequestVersion)
            return;

        await LoadEmbeddedPartListAsync(requestVersion);
    }

    private async void EmbeddedPartListRefresh_OnClick(object? sender, RoutedEventArgs e)
    {
        int requestVersion = ++_embeddedPartListRequestVersion;
        _embeddedPartListDownloadId = 0;
        await LoadEmbeddedPartListAsync(requestVersion);
    }

    private async Task LoadEmbeddedPartListAsync(int requestVersion)
    {
        if (!_viewModel.CanShowSelectedDownloadPartList)
        {
            ClearEmbeddedPartList(
                _viewModel.SelectedDownload is null
                    ? "Markiere einen Download, um die Partliste zu laden."
                    : "Für diesen Download ist keine Partliste verfügbar.");
            return;
        }

        var result = await _viewModel.LoadSelectedDownloadPartListAsync();
        if (requestVersion != _embeddedPartListRequestVersion)
            return;

        if (!result.HasValue)
        {
            ClearEmbeddedPartList("Partliste konnte nicht geladen werden.");
            return;
        }

        var partList = result.Value;
        WrapPanel? segmentsPanel = this.FindControl<WrapPanel>("EmbeddedPartListSegmentsPanel");
        TextBlock? summaryText = this.FindControl<TextBlock>("EmbeddedPartListSummaryText");
        if (segmentsPanel is null || summaryText is null)
            return;

        segmentsPanel.Children.Clear();
        List<PartListDialog.VisualSegment> segments =
            PartListDialog.BuildVisualSegments(partList.Parts, partList.FileSize);
        foreach (PartListDialog.VisualSegment segment in segments)
        {
            segmentsPanel.Children.Add(new Border
            {
                Width = 12,
                Height = 18,
                Margin = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = PartListDialog.BrushForType(segment.Type)
            });
        }

        string sourceSummary = partList.SourceCandidateCount <= 0
            ? "keine Quellenpartlisten"
            : $"Quellenpartlisten {partList.SourcePartListCount:N0}/{partList.SourceCandidateCount:N0}";
        if (partList.SourceErrorCount > 0)
            sourceSummary += $", Fehler {partList.SourceErrorCount:N0}";

        summaryText.Text = $"{segments.Count:N0} Blöcke · {sourceSummary}";
        _embeddedPartListDownloadId = _viewModel.SelectedDownload?.Id ?? 0;
    }

    private void ClearEmbeddedPartList(string message)
    {
        WrapPanel? segmentsPanel = this.FindControl<WrapPanel>("EmbeddedPartListSegmentsPanel");
        TextBlock? summaryText = this.FindControl<TextBlock>("EmbeddedPartListSummaryText");
        if (segmentsPanel is not null)
            segmentsPanel.Children.Clear();
        if (summaryText is not null)
            summaryText.Text = message;
    }

    private async void SearchResultContextDownload_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.DownloadSelectedSearchEntryAsync();

    private async Task ConfirmAndCancelSelectedDownloadAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanCancelSelectedDownload)
            return;

        ConfirmDialog dialog = new(
            "Download abbrechen",
            $"Download abbrechen?\n\n{download.DisplayFilename}",
            "Abbrechen",
            "Zurück");

        bool confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed)
            await _viewModel.CancelSelectedDownloadAsync();
    }

    private async Task RenameSelectedDownloadWithDialogAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanRenameSelectedDownload)
            return;

        TextPromptDialog dialog = new(
            "Download umbenennen",
            "Neuer Dateiname:",
            download.DisplayFilename,
            "Der Dateiname wird an den verbundenen Core übergeben.",
            "Umbenennen");

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.RenameSelectedDownloadAsync(dialog.TextValue);
    }

    private async Task SetSelectedDownloadPowerDownloadWithDialogAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanSetPowerDownloadSelectedDownload)
            return;

        double currentFactor = download.PowerDownload <= 0
            ? 2.2
            : AjDownload.PowerDownloadRawToFactor(download.PowerDownload);
        TextPromptDialog dialog = new(
            "Powerdownload setzen",
            "Faktor 2,2 bis 50,0:",
            currentFactor.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            "Punkt oder Komma sind erlaubt. Werte außerhalb des Core-Bereichs werden begrenzt.",
            "Setzen");

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.SetSelectedDownloadPowerDownloadAsync(dialog.TextValue);
    }

    private async Task SetSelectedDownloadTargetDirectoryWithDialogAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanSetTargetDirectorySelectedDownload)
            return;

        string incomingDirectory = _viewModel.CoreIncomingDirectory == "-"
            ? string.Empty
            : _viewModel.CoreIncomingDirectory;
        TargetDirectoryDialog dialog = new(download.TargetDirectory, incomingDirectory, _viewModel.LoadCoreDirectoryAsync);

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.SetSelectedDownloadTargetDirectoryAsync(dialog.TargetDirectory, dialog.SelectedFromCoreBrowser);
    }

    private async void DownloadContextCopyAjfsp_OnClick(object? sender, RoutedEventArgs e)
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || string.IsNullOrWhiteSpace(download.Hash) || download.Size <= 0)
            return;

        string link = AjfspLinkBuilder.BuildFileLink(download.DisplayFilename, download.Hash, download.Size);
        await CopyTextAsync(link);
    }

    private async void DownloadContextCopyAjfspWithSource_OnClick(object? sender, RoutedEventArgs e)
    {
        string link = _viewModel.BuildSelectedDownloadAjfspLinkWithSource();
        if (!string.IsNullOrWhiteSpace(link))
            await CopyTextAsync(link);
    }

    private async void DownloadContextCopyFilename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDownload is { } download)
            await CopyTextAsync(download.DisplayFilename);
    }

    private async void DownloadContextCopyHash_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDownload is { } download)
            await CopyTextAsync(download.Hash);
    }

    private async void DownloadContextCopyId_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDownload is { } download)
            await CopyTextAsync(download.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async void DownloadSourceContextCopyNick_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedDownloadSourceForContext is { } source)
            await CopyTextAsync(source.NicknameText);
    }

    private async void DownloadSourceContextCopyFilename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedDownloadSourceForContext is { } source)
            await CopyTextAsync(source.Filename);
    }

    private async void DownloadSourceContextCopyDownloadId_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedDownloadSourceForContext is { } source)
            await CopyTextAsync(source.DownloadId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async void SearchResultContextCopyAjfsp_OnClick(object? sender, RoutedEventArgs e)
    {
        AjSearchEntry? entry = _viewModel.SelectedSearchEntry;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Checksum) || entry.Size <= 0)
            return;

        string link = AjfspLinkBuilder.BuildFileLink(entry.Filename, entry.Checksum, entry.Size);
        await CopyTextAsync(link);
    }

    private async void SearchResultContextCopyFilename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSearchEntry is { } entry)
            await CopyTextAsync(entry.Filename);
    }

    private async void SearchResultContextCopySize_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSearchEntry is { } entry)
            await CopyTextAsync(entry.SizeText);
    }

    private async void SearchResultContextCopyChecksum_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedSearchEntry is { } entry)
            await CopyTextAsync(entry.Checksum);
    }

    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
        => _viewModel.Dispose();
}
