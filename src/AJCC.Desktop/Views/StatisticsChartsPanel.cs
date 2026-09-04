using AJCC.Core.Helpers;
using AJCC.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AJCC.Desktop.Views;

internal sealed class StatisticsChartsPanel : UserControl
{
    private const int MaximumHistorySamples = 90;

    private sealed record TransferSample(
        DateTimeOffset Timestamp,
        long DownloadSpeed,
        long UploadSpeed,
        int OpenConnections);

    private sealed record ShareTypeStat(
        string TypeLabel,
        int Count,
        double Percentage,
        int PaletteIndex)
    {
        public string PercentageText => $"{Percentage:0.0} %";
    }

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x4D, 0xA3, 0xFF),
        Color.FromRgb(0xF2, 0xA9, 0x3B),
        Color.FromRgb(0x57, 0xD3, 0x8C),
        Color.FromRgb(0xD9, 0x6C, 0xFF),
        Color.FromRgb(0xFF, 0x6B, 0x6B),
        Color.FromRgb(0x8B, 0xC3, 0x4A),
        Color.FromRgb(0xFF, 0xD1, 0x66),
        Color.FromRgb(0x06, 0xD6, 0xA0),
        Color.FromRgb(0x9F, 0xA8, 0xDA),
        Color.FromRgb(0xFF, 0x9F, 0x1C)
    };

    private readonly Queue<TransferSample> _history = new();
    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private readonly StatisticsShareChart _shareChart = new();
    private readonly StatisticsSpeedGraph _speedGraph = new();
    private readonly StatisticsConnectionsGraph _connectionsGraph = new();
    private readonly TextBlock _shareSummary = CreateSummaryText("Noch keine Share-Daten geladen.");
    private readonly TextBlock _shareCenterLabel = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        FontSize = 14,
        FontWeight = FontWeight.SemiBold,
        IsHitTestVisible = false
    };
    private readonly TextBlock _speedSummary = CreateSummaryText("Noch keine Geschwindigkeitsdaten vorhanden.");
    private readonly TextBlock _connectionsSummary = CreateSummaryText("Noch keine Verbindungsdaten vorhanden.");
    private readonly TextBlock[] _speedScaleLabels = CreateScaleLabels(58);
    private readonly TextBlock[] _connectionScaleLabels = CreateScaleLabels(38);
    private readonly RadioButton _pieRadioButton = new()
    {
        Content = "Kuchen",
        GroupName = "StatisticsShareChartType",
        IsChecked = true,
        Margin = new Thickness(0, 0, 12, 0)
    };
    private readonly RadioButton _barRadioButton = new()
    {
        Content = "Balken",
        GroupName = "StatisticsShareChartType"
    };

    private MainWindowViewModel? _viewModel;
    private string _historyEndpoint = string.Empty;
    private bool _shareChartIsBar;

    public StatisticsChartsPanel()
    {
        Margin = new Thickness(0, 10, 0, 0);

        ToolTip.SetTip(_pieRadioButton, "Zeigt die aktuelle Statistikverteilung als Kuchendiagramm.");
        ToolTip.SetTip(_barRadioButton, "Zeigt die aktuelle Statistikverteilung als Balkendiagramm.");
        _pieRadioButton.IsCheckedChanged += ShareChartType_OnChanged;
        _barRadioButton.IsCheckedChanged += ShareChartType_OnChanged;

        WrapPanel cards = new()
        {
            Orientation = Orientation.Horizontal
        };
        cards.Children.Add(BuildShareCard());
        cards.Children.Add(BuildSpeedCard());
        cards.Children.Add(BuildConnectionsCard());
        Content = cards;

        DataContextChanged += StatisticsChartsPanel_OnDataContextChanged;
        _refreshTimer.Tick += RefreshTimer_OnTick;
        _refreshTimer.Start();

        RefreshVisuals();
    }

    private Border BuildShareCard()
    {
        TextBlock title = CreateTitle("Share-Verteilung");
        TextBlock modeLabel = new()
        {
            Text = "Share:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        modeLabel.Classes.Add("muted");

        StackPanel mode = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        mode.Children.Add(modeLabel);
        mode.Children.Add(_pieRadioButton);
        mode.Children.Add(_barRadioButton);

        Grid summaryRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 5)
        };
        summaryRow.Children.Add(_shareSummary);
        Grid.SetColumn(mode, 1);
        summaryRow.Children.Add(mode);

        Grid chartHost = new()
        {
            Height = 176
        };
        chartHost.Children.Add(_shareChart);
        chartHost.Children.Add(_shareCenterLabel);

        StackPanel content = new();
        content.Children.Add(title);
        content.Children.Add(summaryRow);
        content.Children.Add(chartHost);

        return CreateCard(content);
    }

    private Border BuildSpeedCard()
    {
        TextBlock title = CreateTitle("Geschwindigkeit");
        StackPanel legend = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 3)
        };
        legend.Children.Add(BuildLegendItem("Download", Color.FromRgb(0x4D, 0xA3, 0xFF), new Thickness(0, 0, 14, 0)));
        legend.Children.Add(BuildLegendItem("Upload", Color.FromRgb(0xF2, 0xA9, 0x3B), new Thickness(0)));

        StackPanel content = new();
        content.Children.Add(title);
        content.Children.Add(legend);
        content.Children.Add(_speedSummary);
        content.Children.Add(BuildScaleHost(_speedGraph, _speedScaleLabels));

        return CreateCard(content);
    }

    private Border BuildConnectionsCard()
    {
        TextBlock title = CreateTitle("Verbindungen");
        StackPanel legend = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 3)
        };
        legend.Children.Add(BuildLegendItem("offen", Color.FromRgb(0x57, 0xD3, 0x8C), new Thickness(0)));

        StackPanel content = new();
        content.Children.Add(title);
        content.Children.Add(legend);
        content.Children.Add(_connectionsSummary);
        content.Children.Add(BuildScaleHost(_connectionsGraph, _connectionScaleLabels));

        return CreateCard(content);
    }

    private static Border CreateCard(Control content)
    {
        Border border = new()
        {
            Width = 370,
            MinHeight = 220,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 8),
            Child = content
        };
        border.Classes.Add("panel");
        return border;
    }

    private static TextBlock CreateTitle(string text)
        => new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };

    private static TextBlock CreateSummaryText(string text)
    {
        TextBlock result = new()
        {
            Text = text,
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4)
        };
        result.Classes.Add("muted");
        return result;
    }

    private static TextBlock[] CreateScaleLabels(double minWidth)
    {
        TextBlock[] labels = new TextBlock[5];
        for (int index = 0; index < labels.Length; index++)
        {
            TextBlock label = new()
            {
                Text = "-",
                FontSize = 10,
                MinWidth = minWidth,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = index switch
                {
                    0 => VerticalAlignment.Top,
                    4 => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Center
                },
                Margin = new Thickness(0, 0, 6, 0)
            };
            label.Classes.Add("muted");
            labels[index] = label;
        }

        return labels;
    }

    private static Grid BuildScaleHost(Control graph, IReadOnlyList<TextBlock> labels)
    {
        Grid host = new()
        {
            Height = 150,
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("*,*,*,*,*")
        };

        for (int index = 0; index < labels.Count; index++)
        {
            TextBlock label = labels[index];
            Grid.SetRow(label, index);
            host.Children.Add(label);
        }

        Grid.SetColumn(graph, 1);
        Grid.SetRowSpan(graph, 5);
        host.Children.Add(graph);
        return host;
    }

    private static StackPanel BuildLegendItem(string text, Color color, Thickness margin)
    {
        Border line = new()
        {
            Width = 14,
            Height = 3,
            Background = new SolidColorBrush(color),
            Margin = new Thickness(0, 8, 5, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        TextBlock label = new()
        {
            Text = text,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel item = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = margin
        };
        item.Children.Add(line);
        item.Children.Add(label);
        return item;
    }

    private void StatisticsChartsPanel_OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        ResetHistoryForEndpointChange(force: true);
        AppendHistorySample();
        RefreshVisuals();
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            RefreshVisuals();
        else
            Dispatcher.UIThread.Post(RefreshVisuals);
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        ResetHistoryForEndpointChange(force: false);
        AppendHistorySample();
        RefreshVisuals();
    }

    private void ResetHistoryForEndpointChange(bool force)
    {
        string endpoint = _viewModel?.EndpointText?.Trim() ?? string.Empty;
        if (!force && string.Equals(endpoint, _historyEndpoint, StringComparison.OrdinalIgnoreCase))
            return;

        _historyEndpoint = endpoint;
        _history.Clear();
    }

    private void AppendHistorySample()
    {
        MainWindowViewModel? vm = _viewModel;
        if (vm is null)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TransferSample sample = new(
            now,
            Math.Max(0L, vm.StatisticsDownloadSpeed),
            Math.Max(0L, vm.StatisticsUploadSpeed),
            Math.Max(0, vm.StatisticsOpenConnections));

        if (_history.Count > 0)
        {
            TransferSample last = _history.Last();
            if (last.DownloadSpeed == sample.DownloadSpeed
                && last.UploadSpeed == sample.UploadSpeed
                && last.OpenConnections == sample.OpenConnections
                && now - last.Timestamp < TimeSpan.FromSeconds(1))
            {
                return;
            }
        }

        _history.Enqueue(sample);
        while (_history.Count > MaximumHistorySamples)
            _history.Dequeue();
    }

    private void ShareChartType_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_barRadioButton.IsChecked == true)
            _shareChartIsBar = true;
        else if (_pieRadioButton.IsChecked == true)
            _shareChartIsBar = false;
        else
            return;

        RefreshShareVisuals();
    }

    private void RefreshVisuals()
    {
        RefreshShareVisuals();
        RefreshHistoryVisuals();
    }

    private void RefreshShareVisuals()
    {
        MainWindowViewModel? vm = _viewModel;
        if (vm is null)
        {
            _shareSummary.Text = "Noch keine Share-Daten geladen.";
            _shareCenterLabel.Text = string.Empty;
            _shareChart.SetData(Array.Empty<ShareTypeStat>(), isBar: _shareChartIsBar);
            ToolTip.SetTip(_shareChart, "Noch keine Share-Daten geladen.");
            return;
        }

        List<ShareTypeStat> stats = BuildShareTypeStats(vm);
        int totalShares = vm.Shares.Count();
        if (totalShares == 0)
        {
            _shareSummary.Text = "Noch keine Share-Daten geladen.";
            _shareCenterLabel.Text = string.Empty;
            ToolTip.SetTip(_shareChart, "Noch keine Share-Daten geladen.");
        }
        else
        {
            _shareSummary.Text = $"Gesamtanzahl Shares: {totalShares:N0} · Dateitypen: {stats.Count:N0}";
            _shareCenterLabel.Text = _shareChartIsBar ? string.Empty : $"{totalShares:N0}\nShares";
            ToolTip.SetTip(_shareChart, BuildShareOverviewTooltip(stats, totalShares));
        }

        _shareChart.SetData(stats, isBar: _shareChartIsBar);
    }

    private void RefreshHistoryVisuals()
    {
        TransferSample[] samples = _history.ToArray();
        _speedGraph.SetSamples(samples);
        _connectionsGraph.SetSamples(samples);

        if (samples.Length == 0)
        {
            _speedSummary.Text = "Noch keine Geschwindigkeitsdaten vorhanden.";
            _connectionsSummary.Text = "Noch keine Verbindungsdaten vorhanden.";
            SetEmptyScale(_speedScaleLabels);
            SetEmptyScale(_connectionScaleLabels);
            return;
        }

        TransferSample last = samples[^1];
        _speedSummary.Text =
            $"Download: {DisplayFormatHelper.BytesPerSecond(last.DownloadSpeed)} · "
            + $"Upload: {DisplayFormatHelper.BytesPerSecond(last.UploadSpeed)}";
        _connectionsSummary.Text = $"Offene Verbindungen: {last.OpenConnections:N0}";

        long maxSpeed = Math.Max(1L, samples.Max(static sample => Math.Max(sample.DownloadSpeed, sample.UploadSpeed)));
        int maxConnections = Math.Max(1, samples.Max(static sample => sample.OpenConnections));
        for (int index = 0; index < 5; index++)
        {
            double factor = (4 - index) / 4.0;
            _speedScaleLabels[index].Text =
                DisplayFormatHelper.BytesPerSecond((long)Math.Round(maxSpeed * factor));
            _connectionScaleLabels[index].Text =
                Math.Round(maxConnections * factor).ToString("N0");
        }
    }

    private static void SetEmptyScale(IEnumerable<TextBlock> labels)
    {
        foreach (TextBlock label in labels)
            label.Text = "-";
    }

    private static List<ShareTypeStat> BuildShareTypeStats(MainWindowViewModel vm)
    {
        int totalShares = vm.Shares.Count();
        if (totalShares <= 0)
            return new List<ShareTypeStat>();

        return vm.Shares
            .GroupBy(static share => NormalizeFileType(share.FileType))
            .Select((group, index) => new ShareTypeStat(
                group.Key,
                group.Count(),
                group.Count() * 100.0 / totalShares,
                index))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.TypeLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeFileType(string? fileType)
    {
        string value = (fileType ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(value) ? "ohne Endung" : value;
    }

    private static string BuildShareOverviewTooltip(IReadOnlyList<ShareTypeStat> stats, int totalShares)
    {
        if (stats.Count == 0 || totalShares <= 0)
            return "Noch keine Share-Daten geladen.";

        return "Gesamtanzahl Shares: "
            + totalShares.ToString("N0")
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                stats.Select(static item =>
                    $"{item.TypeLabel}: {item.Count:N0} · {item.PercentageText}"));
    }

    private abstract class StatisticsHistoryGraph : Control
    {
        protected static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(18, 27, 35));
        protected static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromRgb(58, 78, 92)), 1.0);
        protected static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 50, 58)), 1.0);

        protected TransferSample[] Samples { get; private set; } = Array.Empty<TransferSample>();

        public void SetSamples(IReadOnlyList<TransferSample> samples)
        {
            Samples = samples.ToArray();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width)
                ? 280.0
                : Math.Max(120.0, availableSize.Width);
            return new Size(width, 150.0);
        }

        protected void DrawBackgroundAndGrid(DrawingContext context)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;
            if (width <= 2 || height <= 2)
                return;

            Rect bounds = new(0.5, 0.5, Math.Max(0, width - 1.0), Math.Max(0, height - 1.0));
            context.DrawRectangle(BackgroundBrush, BorderPen, bounds, 3.0, 3.0);

            double left = 8.0;
            double right = Math.Max(left, width - 8.0);
            for (int gridIndex = 0; gridIndex <= 4; gridIndex++)
            {
                double y = 8.0 + Math.Max(1.0, height - 16.0) * gridIndex / 4.0;
                context.DrawLine(GridPen, new Point(left, y), new Point(right, y));
            }
        }

        protected static void DrawSmoothLine(
            DrawingContext context,
            IReadOnlyList<Point> points,
            IPen pen)
        {
            if (points.Count == 0)
                return;

            if (points.Count == 1)
            {
                context.DrawLine(pen, points[0], new Point(points[0].X + 0.01, points[0].Y));
                return;
            }

            StreamGeometry geometry = new();
            using (StreamGeometryContext geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(points[0], isFilled: false);
                for (int index = 1; index < points.Count; index++)
                {
                    Point previous = points[index - 1];
                    Point current = points[index];
                    double controlX = previous.X + (current.X - previous.X) * 0.5;
                    geometryContext.CubicBezierTo(
                        new Point(controlX, previous.Y),
                        new Point(controlX, current.Y),
                        current);
                }
                geometryContext.EndFigure(isClosed: false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }

    private sealed class StatisticsSpeedGraph : StatisticsHistoryGraph
    {
        private static readonly IPen DownloadPen = new Pen(new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)), 2.0);
        private static readonly IPen UploadPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xA9, 0x3B)), 2.0);

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            DrawBackgroundAndGrid(context);
            if (Samples.Length == 0)
                return;

            double width = Bounds.Width;
            double height = Bounds.Height;
            double left = 8.0;
            double top = 8.0;
            double plotWidth = Math.Max(1.0, width - 16.0);
            double plotHeight = Math.Max(1.0, height - 16.0);
            long maxSpeed = Math.Max(1L, Samples.Max(static sample => Math.Max(sample.DownloadSpeed, sample.UploadSpeed)));

            List<Point> downloadPoints = new(Samples.Length);
            List<Point> uploadPoints = new(Samples.Length);
            for (int index = 0; index < Samples.Length; index++)
            {
                TransferSample sample = Samples[index];
                double x = left + (Samples.Length == 1 ? 0 : plotWidth * index / (Samples.Length - 1.0));
                double downloadY = top + plotHeight - plotHeight * sample.DownloadSpeed / maxSpeed;
                double uploadY = top + plotHeight - plotHeight * sample.UploadSpeed / maxSpeed;
                downloadPoints.Add(new Point(x, downloadY));
                uploadPoints.Add(new Point(x, uploadY));
            }

            DrawSmoothLine(context, downloadPoints, DownloadPen);
            DrawSmoothLine(context, uploadPoints, UploadPen);
        }
    }

    private sealed class StatisticsConnectionsGraph : StatisticsHistoryGraph
    {
        private static readonly IPen ConnectionsPen = new Pen(new SolidColorBrush(Color.FromRgb(0x57, 0xD3, 0x8C)), 2.0);

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            DrawBackgroundAndGrid(context);
            if (Samples.Length == 0)
                return;

            double width = Bounds.Width;
            double height = Bounds.Height;
            double left = 8.0;
            double top = 8.0;
            double plotWidth = Math.Max(1.0, width - 16.0);
            double plotHeight = Math.Max(1.0, height - 16.0);
            int maxConnections = Math.Max(1, Samples.Max(static sample => sample.OpenConnections));

            List<Point> points = new(Samples.Length);
            for (int index = 0; index < Samples.Length; index++)
            {
                TransferSample sample = Samples[index];
                double x = left + (Samples.Length == 1 ? 0 : plotWidth * index / (Samples.Length - 1.0));
                double y = top + plotHeight - plotHeight * sample.OpenConnections / Math.Max(1.0, maxConnections);
                points.Add(new Point(x, y));
            }

            DrawSmoothLine(context, points, ConnectionsPen);
        }
    }

    private sealed class StatisticsShareChart : Control
    {
        private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(18, 27, 35));
        private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromRgb(58, 78, 92)), 1.0);
        private static readonly IPen SliceBorderPen = new Pen(new SolidColorBrush(Color.FromRgb(17, 19, 24)), 1.0);
        private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1.0);

        private ShareTypeStat[] _items = Array.Empty<ShareTypeStat>();
        private bool _isBar;

        public void SetData(IReadOnlyList<ShareTypeStat> items, bool isBar)
        {
            _items = items.ToArray();
            _isBar = isBar;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width)
                ? 340.0
                : Math.Max(140.0, availableSize.Width);
            return new Size(width, 176.0);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            double width = Bounds.Width;
            double height = Bounds.Height;
            if (width <= 2 || height <= 2)
                return;

            Rect bounds = new(0.5, 0.5, Math.Max(0, width - 1.0), Math.Max(0, height - 1.0));
            context.DrawRectangle(BackgroundBrush, BorderPen, bounds, 3.0, 3.0);

            if (_items.Length == 0)
                return;

            if (_isBar)
                DrawBars(context, width, height);
            else
                DrawPie(context, width, height);
        }

        private void DrawBars(DrawingContext context, double width, double height)
        {
            double left = 12.0;
            double top = 10.0;
            double right = 12.0;
            double bottom = 12.0;
            double plotWidth = Math.Max(20.0, width - left - right);
            double plotHeight = Math.Max(20.0, height - top - bottom);
            int maxCount = Math.Max(1, _items.Max(static item => item.Count));
            double slotWidth = plotWidth / Math.Max(1, _items.Length);
            double barWidth = Math.Max(3.0, slotWidth * 0.65);

            context.DrawLine(AxisPen, new Point(left, top + plotHeight), new Point(left + plotWidth, top + plotHeight));
            context.DrawLine(AxisPen, new Point(left, top), new Point(left, top + plotHeight));

            for (int index = 0; index < _items.Length; index++)
            {
                ShareTypeStat item = _items[index];
                double barHeight = plotHeight * item.Count / Math.Max(1.0, maxCount);
                double x = left + index * slotWidth + (slotWidth - barWidth) / 2.0;
                double y = top + plotHeight - barHeight;
                Rect bar = new(x, y, barWidth, Math.Max(2.0, barHeight));
                context.DrawRectangle(GetPaletteBrush(item.PaletteIndex), null, bar, 3.0, 3.0);
            }
        }

        private void DrawPie(DrawingContext context, double width, double height)
        {
            double radius = Math.Max(20.0, Math.Min(width, height) / 2.0 - 12.0);
            Point center = new(width / 2.0, height / 2.0);
            int total = Math.Max(1, _items.Sum(static item => item.Count));
            double startAngle = -90.0;

            foreach (ShareTypeStat item in _items)
            {
                double sweepAngle = 360.0 * item.Count / total;
                StreamGeometry geometry = CreatePieSliceGeometry(center, radius, startAngle, sweepAngle);
                context.DrawGeometry(GetPaletteBrush(item.PaletteIndex), SliceBorderPen, geometry);
                startAngle += sweepAngle;
            }
        }

        private static StreamGeometry CreatePieSliceGeometry(
            Point center,
            double radius,
            double startAngle,
            double sweepAngle)
        {
            StreamGeometry geometry = new();
            using StreamGeometryContext geometryContext = geometry.Open();
            geometryContext.BeginFigure(center, isFilled: true);

            int segments = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweepAngle) / 8.0));
            for (int index = 0; index <= segments; index++)
            {
                double angle = startAngle + sweepAngle * index / segments;
                double radians = angle * Math.PI / 180.0;
                geometryContext.LineTo(new Point(
                    center.X + Math.Cos(radians) * radius,
                    center.Y + Math.Sin(radians) * radius));
            }

            geometryContext.LineTo(center);
            geometryContext.EndFigure(isClosed: true);
            return geometry;
        }

        private static IBrush GetPaletteBrush(int index)
        {
            Color color = Palette[Math.Abs(index) % Palette.Length];
            return new SolidColorBrush(color);
        }
    }
}
