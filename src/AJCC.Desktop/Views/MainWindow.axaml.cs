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
            case AjSearchEntry entry:
                _viewModel.SelectedSearchEntry = entry;
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

        switch (item.DataContext)
        {
            case AjDownload download:
                _viewModel.SelectedDownload = download;
                break;
            case AjSearchEntry entry:
                _viewModel.SelectedSearchEntry = entry;
                break;
            default:
                return;
        }

        foreach (var descendant in item.GetVisualDescendants())
        {
            if (descendant is not Control { ContextMenu: { } menu } contextHost)
                continue;

            menu.Open(contextHost);
            e.Handled = true;
            return;
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

    private async void DownloadContextPause_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.PauseSelectedDownloadAsync();

    private async void DownloadContextResume_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.ResumeSelectedDownloadAsync();

    private async void DownloadContextCancel_OnClick(object? sender, RoutedEventArgs e)
        => await ConfirmAndCancelSelectedDownloadAsync();

    private async void DownloadContextRename_OnClick(object? sender, RoutedEventArgs e)
        => await RenameSelectedDownloadWithDialogAsync();

    private async void DownloadContextSetTargetDirectory_OnClick(object? sender, RoutedEventArgs e)
        => await SetSelectedDownloadTargetDirectoryWithDialogAsync();

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

    private async Task SetSelectedDownloadTargetDirectoryWithDialogAsync()
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || !_viewModel.CanSetTargetDirectorySelectedDownload)
            return;

        string incomingDirectory = _viewModel.CoreIncomingDirectory == "-"
            ? string.Empty
            : _viewModel.CoreIncomingDirectory;
        TargetDirectoryDialog dialog = new(download.TargetDirectory, incomingDirectory);

        bool accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            await _viewModel.SetSelectedDownloadTargetDirectoryAsync(dialog.TargetDirectory);
    }

    private async void DownloadContextCopyAjfsp_OnClick(object? sender, RoutedEventArgs e)
    {
        AjDownload? download = _viewModel.SelectedDownload;
        if (download is null || string.IsNullOrWhiteSpace(download.Hash) || download.Size <= 0)
            return;

        string link = AjfspLinkBuilder.BuildFileLink(download.DisplayFilename, download.Hash, download.Size);
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
