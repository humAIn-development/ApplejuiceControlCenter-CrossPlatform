using System.Globalization;
using System.Text;
using AJCC.Core.Models;
using AJCC.Core.Protocol;
using AJCC.Core.Services;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class ShareSnapshotDiffDialog : Window
{
    private const int MaxDisplayedItems = 5000;
    private const int MaxCopiedItemsPerSection = 2000;

    private ShareSnapshotComparisonReport _report = new();
    private ShareSnapshotDocument _currentSnapshot = new();
    private string _coreEndpoint = string.Empty;
    private string _loadErrorMessage = string.Empty;

    public ShareSnapshotDiffDialog()
    {
        InitializeComponent();
    }

    public ShareSnapshotDiffDialog(
        ShareSnapshotComparisonReport report,
        ShareSnapshotDocument currentSnapshot,
        string coreEndpoint,
        string? loadErrorMessage = null)
        : this()
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _currentSnapshot = currentSnapshot ?? throw new ArgumentNullException(nameof(currentSnapshot));
        _coreEndpoint = CoreEndpoint.Parse(coreEndpoint).BaseUri.AbsoluteUri;
        _loadErrorMessage = loadErrorMessage ?? string.Empty;

        Populate();
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void Populate()
    {
        SetItems("NoticesList", _report.Notices);
        SetItems("AddedFilesList", _report.AddedFiles.Take(MaxDisplayedItems).ToList());
        SetItems("RemovedFilesList", _report.RemovedFiles.Take(MaxDisplayedItems).ToList());
        SetItems("ChangedFilesList", _report.ChangedFiles.Take(MaxDisplayedItems).ToList());
        SetItems("DirectoriesList", _report.DirectoryDeltas.Take(MaxDisplayedItems).ToList());
        SetItems("RootsList", _report.RootChanges);

        SetText("ResultCountText", _report.TotalChangeCount.ToString("N0", CultureInfo.CurrentCulture));
        SetText("CurrentFileCountText", _report.Current.FileCount.ToString("N0", CultureInfo.CurrentCulture));
        SetText("CurrentSizeText", _report.CurrentTotalSizeText);

        if (!_report.HasBaseline)
        {
            SetText(
                "ResultHeadlineText",
                string.IsNullOrWhiteSpace(_loadErrorMessage)
                    ? "Noch keine Vergleichsbasis gespeichert."
                    : "Vergleichsbasis konnte nicht gelesen werden.");
            SetText(
                "ResultDetailText",
                string.IsNullOrWhiteSpace(_loadErrorMessage)
                    ? "Der aktuell geladene Sharezustand kann bewusst als lokaler Ausgangspunkt gespeichert werden."
                    : "Der aktuelle Stand wird ohne Vergleichsbasis angezeigt. Eine bewusst gespeicherte neue Basis ersetzt die beschädigte lokale Datei.");
            SetText("FileDeltaText", "–");
            SetText("SizeDeltaText", "–");
        }
        else
        {
            int reviewNoticeCount = _report.Notices.Count(
                notice => notice.Severity == ShareSnapshotNoticeSeverity.Review);

            if (_report.TotalChangeCount == 0)
            {
                SetText("ResultHeadlineText", "Keine Unterschiede zur Vergleichsbasis.");
            }
            else if (reviewNoticeCount > 0)
            {
                SetText(
                    "ResultHeadlineText",
                    $"{_report.TotalChangeCount:N0} Änderungen gefunden – {reviewNoticeCount:N0} Bereich(e) bewusst prüfen.");
            }
            else
            {
                SetText(
                    "ResultHeadlineText",
                    _report.TotalChangeCount == 1
                        ? "Eine Änderung zur Vergleichsbasis gefunden."
                        : $"{_report.TotalChangeCount:N0} Änderungen zur Vergleichsbasis gefunden.");
            }

            SetText(
                "ResultDetailText",
                $"Vergleichsbasis: {FormatTimestamp(_report.Baseline!.CapturedAtUtc)} · aktueller geladener Stand: {FormatTimestamp(_report.Current.CapturedAtUtc)}.");
            SetText("FileDeltaText", _report.NetFileCountText);
            SetText("SizeDeltaText", _report.NetSizeText);
        }

        string footer = _report.HasBaseline
            ? $"Vergleichsbasis vom {FormatTimestamp(_report.Baseline!.CapturedAtUtc)} · rein lokale Auswertung · keine Core-Anfrage"
            : "Keine Vergleichsbasis vorhanden · rein lokale Auswertung · keine Core-Anfrage";
        if (!string.IsNullOrWhiteSpace(_loadErrorMessage))
            footer += " · Lesefehler: " + _loadErrorMessage;
        SetText("FooterStatusText", footer);

        SetTabHeader("AddedFilesTab", $"Neue Dateien ({_report.AddedFiles.Count:N0})");
        SetTabHeader("RemovedFilesTab", $"Entfernte Dateien ({_report.RemovedFiles.Count:N0})");
        SetTabHeader("ChangedFilesTab", $"Veränderte Dateien ({_report.ChangedFiles.Count:N0})");
        SetTabHeader("DirectoriesTab", $"Ordneränderungen ({_report.DirectoryDeltas.Count:N0})");
        SetTabHeader("RootsTab", $"Freigabewurzeln ({_report.RootChanges.Count:N0})");
    }

    private void SetItems<T>(string controlName, IEnumerable<T> items)
    {
        ListBox? list = this.FindControl<ListBox>(controlName);
        if (list is not null)
            list.ItemsSource = items;
    }

    private void SetText(string controlName, string text)
    {
        TextBlock? block = this.FindControl<TextBlock>(controlName);
        if (block is not null)
            block.Text = text;
    }

    private void SetTabHeader(string controlName, string header)
    {
        TabItem? tab = this.FindControl<TabItem>(controlName);
        if (tab is not null)
            tab.Header = header;
    }

    private async void SaveBaselineButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_report.HasBaseline)
        {
            ConfirmDialog confirm = new(
                "Share-Vergleich",
                "Die bisherige lokale Vergleichsbasis wird durch den aktuell geladenen Sharezustand ersetzt.\n\nFortfahren?",
                "Ersetzen",
                "Zurück");

            if (!await confirm.ShowDialog<bool>(this))
                return;
        }

        Button? button = this.FindControl<Button>("SaveBaselineButton");
        object? previousContent = button?.Content;

        try
        {
            if (button is not null)
            {
                button.IsEnabled = false;
                button.Content = "Vergleichsbasis wird gespeichert…";
            }

            await ShareSnapshotService.SaveAsync(
                _currentSnapshot,
                coreEndpoint: _coreEndpoint);
            Close(true);
        }
        catch (Exception ex)
        {
            SetText(
                "FooterStatusText",
                "Vergleichsbasis konnte nicht gespeichert werden: " + ex.Message);
        }
        finally
        {
            if (button is not null)
            {
                button.Content = previousContent;
                button.IsEnabled = true;
            }
        }
    }

    private async void CopyReportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                SetText("FooterStatusText", "Zwischenablage ist nicht verfügbar.");
                return;
            }

            await clipboard.SetTextAsync(BuildReportText());
            SetText("FooterStatusText", "Vergleichsbericht wurde in die Zwischenablage kopiert.");
        }
        catch (Exception ex)
        {
            SetText("FooterStatusText", "Vergleichsbericht konnte nicht kopiert werden: " + ex.Message);
        }
    }

    private string BuildReportText()
    {
        StringBuilder text = new();
        text.AppendLine("AJCC-X Share-Vergleich");
        text.AppendLine($"Erstellt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Core: {_report.Current.CoreHost}:{_report.Current.CorePort}");
        text.AppendLine($"Aktueller geladener Stand: {FormatTimestamp(_report.Current.CapturedAtUtc)}");
        text.AppendLine(
            "Vergleichsbasis: "
            + (_report.Baseline is null
                ? "nicht vorhanden"
                : FormatTimestamp(_report.Baseline.CapturedAtUtc)));
        text.AppendLine(
            $"Dateien aktuell: {_report.Current.FileCount:N0} · {_report.CurrentTotalSizeText}");
        text.AppendLine(
            $"Neu: {_report.AddedFiles.Count:N0} · Entfernt: {_report.RemovedFiles.Count:N0} · Verändert: {_report.ChangedFiles.Count:N0} · Freigabewurzeln: {_report.RootChanges.Count:N0}");
        text.AppendLine();

        text.AppendLine("Hinweise:");
        foreach (ShareSnapshotNotice notice in _report.Notices)
            text.AppendLine($"- [{notice.SeverityText}] {notice.Category}: {notice.Message}");

        AppendFileSection(text, "Neue Dateien", _report.AddedFiles, useCurrentSize: true);
        AppendFileSection(text, "Nicht mehr sichtbare Dateien", _report.RemovedFiles, useCurrentSize: false);
        AppendChangedFileSection(text, _report.ChangedFiles);

        text.AppendLine();
        text.AppendLine("Änderungen an Freigabewurzeln:");
        if (_report.RootChanges.Count == 0)
        {
            text.AppendLine("- keine");
        }
        else
        {
            foreach (ShareSnapshotRootChange change in _report.RootChanges.Take(MaxCopiedItemsPerSection))
            {
                text.AppendLine(
                    $"- {change.ChangeText}: {change.Path} · vorher: {change.PreviousModeText} · aktuell: {change.CurrentModeText} · {change.Detail}");
            }
        }

        text.AppendLine();
        text.AppendLine(
            "Hinweis: Rein lokaler Vergleich der bereits geladenen Share-Dateiliste und der konfigurierten Freigabewurzeln. Dateiinhalte werden nicht gelesen. Keine Core-Anfrage, keine Änderung am Share.");
        return text.ToString();
    }

    private static void AppendFileSection(
        StringBuilder text,
        string title,
        IReadOnlyList<ShareSnapshotFileChange> files,
        bool useCurrentSize)
    {
        text.AppendLine();
        text.AppendLine(title + ":");

        if (files.Count == 0)
        {
            text.AppendLine("- keine");
            return;
        }

        foreach (ShareSnapshotFileChange file in files.Take(MaxCopiedItemsPerSection))
        {
            string sizeText = useCurrentSize ? file.CurrentSizeText : file.PreviousSizeText;
            text.AppendLine($"- {file.Path} · {sizeText}");
        }

        if (files.Count > MaxCopiedItemsPerSection)
        {
            text.AppendLine(
                $"- ... {files.Count - MaxCopiedItemsPerSection:N0} weitere Einträge nicht in den Textbericht übernommen");
        }
    }

    private static void AppendChangedFileSection(
        StringBuilder text,
        IReadOnlyList<ShareSnapshotFileChange> files)
    {
        text.AppendLine();
        text.AppendLine("Veränderte Dateien:");

        if (files.Count == 0)
        {
            text.AppendLine("- keine");
            return;
        }

        foreach (ShareSnapshotFileChange file in files.Take(MaxCopiedItemsPerSection))
        {
            text.AppendLine(
                $"- {file.Path} · {file.PreviousSizeText} → {file.CurrentSizeText} · {file.ChangeText}");
        }

        if (files.Count > MaxCopiedItemsPerSection)
        {
            text.AppendLine(
                $"- ... {files.Count - MaxCopiedItemsPerSection:N0} weitere Einträge nicht in den Textbericht übernommen");
        }
    }

    private static string FormatTimestamp(DateTime utcTimestamp)
        => utcTimestamp == default
            ? "unbekannt"
            : utcTimestamp.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture);

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
