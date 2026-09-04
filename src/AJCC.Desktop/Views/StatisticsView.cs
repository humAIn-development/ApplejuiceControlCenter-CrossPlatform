using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AJCC.Desktop.Services;
using AJCC.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AJCC.Desktop.Views;

public sealed class StatisticsView : UserControl
{
    private sealed record TileTextBlocks(TextBlock Primary, TextBlock Secondary);

    private readonly StatisticsTileConfigurationStore _configurationStore = new();
    private readonly WrapPanel _tilesPanel = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _detailTitle = new()
    {
        FontSize = 16,
        FontWeight = FontWeight.SemiBold
    };
    private readonly TextBlock _detailText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0)
    };
    private readonly Dictionary<string, TileTextBlocks> _tileTextBlocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(DateTimeOffset At, string Download, string Upload, bool Connected)> _history =
        new();
    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private MainWindowViewModel? _viewModel;
    private string[] _selectedKeys;
    private string _selectedDetailKey;
    private DateTimeOffset _lastCpuSampleUtc;
    private TimeSpan _lastCpuTime;
    private double _cpuPercent;
    private long _workingSetBytes;
    private long _privateMemoryBytes;
    private long _managedMemoryBytes;
    private int _threadCount;
    private int _handleCount = -1;
    private DateTimeOffset _processStartedUtc = DateTimeOffset.UtcNow;

    public StatisticsView()
    {
        _selectedKeys = _configurationStore.Load().SelectedKeys;
        _selectedDetailKey = _selectedKeys.FirstOrDefault()
            ?? StatisticsTileCatalog.DefaultSelectedKeys[0];

        InitializeProcessSample();

        TextBlock title = new()
        {
            Text = "Statistik",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button configureButton = new()
        {
            Content = "Kacheln...",
            MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        configureButton.Click += ConfigureButton_OnClick;

        DockPanel header = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(configureButton, Dock.Right);
        header.Children.Add(configureButton);
        header.Children.Add(title);

        StackPanel detailStack = new();
        detailStack.Children.Add(_detailTitle);
        detailStack.Children.Add(_detailText);

        Border detailBorder = new()
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 0),
            Child = detailStack
        };
        detailBorder.Classes.Add("panel");

        StatisticsChartsPanel chartsPanel = new();

        StackPanel root = new()
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        root.Children.Add(header);
        root.Children.Add(_tilesPanel);
        root.Children.Add(detailBorder);
        root.Children.Add(chartsPanel);

        Content = new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        DataContextChanged += StatisticsView_OnDataContextChanged;
        _refreshTimer.Tick += RefreshTimer_OnTick;
        _refreshTimer.Start();

        RebuildTiles();
        RefreshData();
    }

    private void StatisticsView_OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        AddHistorySample();
        RefreshData();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshData();
        else
            Dispatcher.UIThread.Post(RefreshData);
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        SampleProcess();
        AddHistorySample();
        RefreshData();
    }

    private async void ConfigureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        StatisticsTileConfigurationDialog dialog = new(_selectedKeys);
        bool accepted = await dialog.ShowDialog<bool>(owner);
        if (!accepted)
            return;

        StatisticsTileConfiguration configuration = new(dialog.SelectedKeys);
        if (!_configurationStore.TrySave(configuration, out string errorMessage))
        {
            _viewModel?.SetStatusMessage("Statistik-Kacheln konnten nicht gespeichert werden: " + errorMessage);
            _detailTitle.Text = "Statistik-Konfiguration";
            _detailText.Text = "Speichern fehlgeschlagen: " + errorMessage;
            return;
        }

        _selectedKeys = _configurationStore.Load().SelectedKeys;
        if (!_selectedKeys.Contains(_selectedDetailKey, StringComparer.OrdinalIgnoreCase))
            _selectedDetailKey = _selectedKeys[0];

        RebuildTiles();
        RefreshData();
        _viewModel?.SetStatusMessage("Statistik-Kacheln übernommen.");
    }

    private void RebuildTiles()
    {
        _tilesPanel.Children.Clear();
        _tileTextBlocks.Clear();

        foreach (string key in _selectedKeys)
        {
            StatisticsTileDefinition? definition = StatisticsTileCatalog.Find(key);
            if (definition is null)
                continue;

            TextBlock title = new()
            {
                Text = definition.Title,
                FontWeight = FontWeight.SemiBold
            };

            TextBlock primary = new()
            {
                Text = "-",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 6, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };

            TextBlock secondary = new()
            {
                Text = "-",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            secondary.Classes.Add("muted");

            StackPanel content = new();
            content.Children.Add(title);
            content.Children.Add(primary);
            content.Children.Add(secondary);

            Border tile = new()
            {
                Width = 250,
                MinHeight = 104,
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 8, 8),
                Child = content
            };
            ToolTip.SetTip(tile, definition.Description);
            tile.Classes.Add("metric");
            string capturedKey = definition.Key;
            tile.PointerPressed += (_, _) => SelectDetail(capturedKey);

            _tileTextBlocks[definition.Key] = new TileTextBlocks(primary, secondary);
            _tilesPanel.Children.Add(tile);
        }
    }

    private void SelectDetail(string key)
    {
        _selectedDetailKey = key;
        RefreshDetail();
    }

    private void RefreshData()
    {
        MainWindowViewModel? vm = _viewModel;
        foreach ((string key, TileTextBlocks controls) in _tileTextBlocks)
        {
            (string primary, string secondary) = BuildSummary(key, vm);
            controls.Primary.Text = primary;
            controls.Secondary.Text = secondary;
        }

        RefreshDetail();
    }

    private void RefreshDetail()
    {
        StatisticsTileDefinition? definition = StatisticsTileCatalog.Find(_selectedDetailKey);
        if (definition is null)
        {
            _detailTitle.Text = "Statistik";
            _detailText.Text = "Keine Kachel ausgewählt.";
            return;
        }

        (string primary, string secondary) = BuildSummary(definition.Key, _viewModel);
        _detailTitle.Text = definition.Title;
        _detailText.Text =
            definition.Description
            + Environment.NewLine
            + Environment.NewLine
            + primary
            + Environment.NewLine
            + secondary;
    }

    private (string Primary, string Secondary) BuildSummary(
        string key,
        MainWindowViewModel? vm)
    {
        if (vm is null)
            return ("-", "Noch keine GUI-/Core-Daten verfügbar.");

        int downloadCount = vm.Downloads.Count();
        int uploadCount = vm.Uploads.Count();
        int activeUploadCount = vm.ActiveUploads.Count();
        int shareCount = vm.Shares.Count();
        int searchCount = vm.Searches.Count();
        long sourceCount = vm.Downloads.Sum(static download => Math.Max(0L, download.SourceCount));

        return key switch
        {
            "connection" => (
                vm.ConnectionStateText,
                $"{vm.FooterServerText} · Core-Port {vm.CorePortText}"),

            "transfer" => (
                $"↓ {vm.DownloadSpeedText} · ↑ {vm.UploadSpeedText}",
                $"{downloadCount:N0} Downloads · {activeUploadCount:N0} aktive Uploads"),

            "activity" => (
                $"{downloadCount:N0} DL · {activeUploadCount:N0} aktive UL",
                $"{sourceCount:N0} Quellen · {shareCount:N0} Shares · {searchCount:N0} Suchen"),

            "session" => (
                $"{vm.FooterSessionDownloadText} · {vm.FooterSessionUploadText}",
                vm.FooterCreditsText),

            "network" => (
                $"{vm.NetworkUsersText} Nutzer",
                $"{vm.NetworkFilesText} Dateien · {vm.ServerCountText} Server"),

            "core" => (
                vm.CoreVersion,
                $"Nick {vm.CoreNick} · XML-Port {vm.CoreXmlPortText} · Timestamp {vm.CoreTimestampText}"),

            "gui" => (
                ShortOsDescription(),
                $"Working Set {FormatBytes(_workingSetBytes)} · .NET {Environment.Version}"),

            "health" => (
                vm.IsConnected ? "Online" : "Offline",
                string.IsNullOrWhiteSpace(vm.StatusText) ? "-" : vm.StatusText),

            "downloads" => (
                $"{downloadCount:N0} Downloads",
                $"Aktuell ↓ {vm.DownloadSpeedText} · Quellen {sourceCount:N0}"),

            "uploads" => (
                $"{uploadCount:N0} Uploads",
                $"{activeUploadCount:N0} aktiv · ↑ {vm.UploadSpeedText}"),

            "sources" => (
                $"{sourceCount:N0} Quellen",
                $"{downloadCount:N0} Downloads in aktueller Core-Sicht"),

            "shares" => (
                vm.ShareCountText,
                vm.ShareSizeText),

            "networksize" => (
                $"{vm.NetworkFilesText} Dateien",
                $"{vm.NetworkUsersText} Nutzer in der aktuellen Core-Netzsicht"),

            "guiruntime" => (
                FormatDuration(DateTimeOffset.UtcNow - _processStartedUtc),
                $".NET {Environment.Version} · {RuntimeInformation.ProcessArchitecture}"),

            "guidisplay" => (
                BuildDisplaySummary(),
                BuildScalingSummary()),

            "guicpu" => (
                $"{_cpuPercent:0.0} %",
                $"{Environment.ProcessorCount:N0} logische Prozessoren"),

            "guimemory" => (
                $"Working Set {FormatBytes(_workingSetBytes)}",
                $"Managed {FormatBytes(_managedMemoryBytes)} · privat {FormatBytes(_privateMemoryBytes)}"),

            "guiprocess" => (
                $"{_threadCount:N0} Threads",
                _handleCount >= 0
                    ? $"{_handleCount:N0} Handles · privat {FormatBytes(_privateMemoryBytes)}"
                    : $"Handles n/a · privat {FormatBytes(_privateMemoryBytes)}"),

            "history" => (
                $"{_history.Count:N0} Samples",
                _history.Count == 0
                    ? "Noch keine Kurzzeithistorie."
                    : $"Letztes Sample: ↓ {_history.Last().Download} · ↑ {_history.Last().Upload}"),

            _ => ("-", "Keine Statistikdaten für diese Kachel.")
        };
    }

    private void AddHistorySample()
    {
        MainWindowViewModel? vm = _viewModel;
        if (vm is null)
            return;

        _history.Enqueue((
            DateTimeOffset.UtcNow,
            vm.DownloadSpeedText,
            vm.UploadSpeedText,
            vm.IsConnected));

        while (_history.Count > 30)
            _history.Dequeue();
    }

    private void InitializeProcessSample()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            _processStartedUtc = new DateTimeOffset(process.StartTime.ToUniversalTime());
            _lastCpuTime = process.TotalProcessorTime;
            _lastCpuSampleUtc = DateTimeOffset.UtcNow;
            UpdateProcessMetrics(process);
        }
        catch
        {
            _lastCpuSampleUtc = DateTimeOffset.UtcNow;
        }
    }

    private void SampleProcess()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TimeSpan cpu = process.TotalProcessorTime;
            double elapsedMs = (now - _lastCpuSampleUtc).TotalMilliseconds;
            double cpuMs = (cpu - _lastCpuTime).TotalMilliseconds;
            if (elapsedMs > 0 && Environment.ProcessorCount > 0)
            {
                _cpuPercent = Math.Clamp(
                    cpuMs / (elapsedMs * Environment.ProcessorCount) * 100.0,
                    0.0,
                    100.0);
            }

            _lastCpuTime = cpu;
            _lastCpuSampleUtc = now;
            UpdateProcessMetrics(process);
        }
        catch
        {
            // Statistik darf GUI/Core-Betrieb niemals beeinträchtigen.
        }
    }

    private void UpdateProcessMetrics(Process process)
    {
        _workingSetBytes = Math.Max(0L, process.WorkingSet64);
        _privateMemoryBytes = Math.Max(0L, process.PrivateMemorySize64);
        _managedMemoryBytes = Math.Max(0L, GC.GetTotalMemory(forceFullCollection: false));

        try
        {
            _threadCount = Math.Max(0, process.Threads.Count);
        }
        catch
        {
            _threadCount = 0;
        }

        try
        {
            _handleCount = Math.Max(0, process.HandleCount);
        }
        catch
        {
            _handleCount = -1;
        }
    }

    private string BuildDisplaySummary()
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
                return "Anzeige n/a";

            var screens = topLevel.Screens;
            if (screens is null)
                return "Anzeige n/a";

            var screen = screens.ScreenFromTopLevel(topLevel);
            return screen is null
                ? "Anzeige n/a"
                : $"{screen.Bounds.Width:N0} × {screen.Bounds.Height:N0} px";
        }
        catch
        {
            return "Anzeige n/a";
        }
    }

    private string BuildScalingSummary()
    {
        try
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
                return "Skalierung n/a";

            var screens = topLevel.Screens;
            if (screens is null)
                return $"Render-Skalierung {topLevel.RenderScaling:0.##}×";

            var screen = screens.ScreenFromTopLevel(topLevel);
            return screen is null
                ? $"Render-Skalierung {topLevel.RenderScaling:0.##}×"
                : $"Skalierung {screen.Scaling:0.##}× · Render {topLevel.RenderScaling:0.##}×";
        }
        catch
        {
            return "Skalierung n/a";
        }
    }

    private static string ShortOsDescription()
    {
        string value = RuntimeInformation.OSDescription.Trim();
        return value.Length == 0 ? Environment.OSVersion.Platform.ToString() : value;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays} d {duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatBytes(long value)
    {
        double size = Math.Max(0L, value);
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        int unit = 0;
        while (size >= 1024.0 && unit < units.Length - 1)
        {
            size /= 1024.0;
            unit++;
        }

        return unit == 0
            ? $"{size:0} {units[unit]}"
            : $"{size:0.##} {units[unit]}";
    }
}
