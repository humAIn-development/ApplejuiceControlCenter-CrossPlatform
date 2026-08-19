using System;
using System.Collections.Generic;
using System.Linq;
using AJCC.Core.Helpers;
using AJCC.Core.Models;
using AJCC.Core.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AJCC.Desktop.Views;

public sealed partial class PartListDialog : Window
{
    private const int MaxVisualSegments = 300;

    public PartListDialog()
    {
        InitializeComponent();
    }

    public PartListDialog(
        string filename,
        long fileSize,
        IReadOnlyList<AjPart> parts,
        int sourcePartListCount = 0,
        int sourceCandidateCount = 0,
        int sourceErrorCount = 0)
        : this()
    {
        string displayName = string.IsNullOrWhiteSpace(filename) ? "unbekannte Datei" : filename;
        Title = "Partliste Download · " + displayName;

        TextBlock? fileNameText = this.FindControl<TextBlock>("FileNameText");
        TextBlock? summaryText = this.FindControl<TextBlock>("SummaryText");
        StackPanel? segmentsPanel = this.FindControl<StackPanel>("SegmentsPanel");

        if (fileNameText is not null)
            fileNameText.Text = displayName;

        List<VisualSegment> segments = BuildVisualSegments(parts, fileSize);
        long loadedBytes = EstimateBytes(parts, fileSize, -1);
        double loadedPercent = fileSize <= 0 ? 0 : loadedBytes * 100.0 / fileSize;

        if (summaryText is not null)
        {
            string sourceSummary = sourceCandidateCount <= 0
                ? "keine Quellenpartlisten"
                : $"Quellenpartlisten {sourcePartListCount:N0}/{sourceCandidateCount:N0}";
            if (sourceErrorCount > 0)
                sourceSummary += $", Fehler {sourceErrorCount:N0}";

            summaryText.Text =
                $"{DisplayFormatHelper.Bytes(fileSize)} · {segments.Count:N0} Anzeigeblöcke · geladen ca. {loadedPercent:N1} % · {sourceSummary}\n" +
                "Grün: geladen · Blau: wird geladen · Gelb: 1 Quelle · Rot: mehrere Quellen · Schwarz: nicht geladen";
        }

        if (segmentsPanel is null)
            return;

        foreach (VisualSegment segment in segments)
        {
            segmentsPanel.Children.Add(new Border
            {
                Width = 16,
                Height = 28,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = BrushForType(segment.Type)
            });
        }
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private static List<VisualSegment> BuildVisualSegments(IReadOnlyList<AjPart> parts, long fileSize)
    {
        if (parts.Count == 0 || fileSize <= 0)
            return new List<VisualSegment>();

        List<AjPart> orderedParts = parts
            .Where(part => part.FromPosition >= 0)
            .OrderBy(part => part.FromPosition)
            .ToList();
        if (orderedParts.Count == 0)
            return new List<VisualSegment>();

        int visualCount = (int)Math.Min(
            MaxVisualSegments,
            Math.Max(1, fileSize / Math.Max(1, fileSize / MaxVisualSegments)));
        if (fileSize < MaxVisualSegments)
            visualCount = (int)Math.Max(1, fileSize);
        visualCount = Math.Clamp(visualCount, 1, MaxVisualSegments);

        List<VisualSegment> result = new(visualCount);
        int partIndex = 0;

        for (int index = 0; index < visualCount; index++)
        {
            long start = index * fileSize / visualCount;
            long end = (index + 1) * fileSize / visualCount;
            long midpoint = start + Math.Max(0, end - start) / 2;

            while (partIndex + 1 < orderedParts.Count && orderedParts[partIndex + 1].FromPosition <= midpoint)
                partIndex++;

            int type = TypeForVisualRange(orderedParts, partIndex, start, end, fileSize);
            result.Add(new VisualSegment(start, end, type));
        }

        return result;
    }

    private static int TypeForVisualRange(
        IReadOnlyList<AjPart> orderedParts,
        int midpointPartIndex,
        long start,
        long end,
        long fileSize)
    {
        for (int index = Math.Max(0, midpointPartIndex - 2); index < orderedParts.Count; index++)
        {
            long partStart = orderedParts[index].FromPosition;
            if (partStart >= end)
                break;

            long partEnd = index + 1 < orderedParts.Count
                ? orderedParts[index + 1].FromPosition
                : fileSize;
            if (partEnd <= start)
                continue;

            if (orderedParts[index].Type == DownloadPartListAggregator.ActiveDownloadPartType)
                return DownloadPartListAggregator.ActiveDownloadPartType;
        }

        return orderedParts[midpointPartIndex].Type;
    }

    private static long EstimateBytes(IReadOnlyList<AjPart> parts, long fileSize, int type)
    {
        if (parts.Count == 0 || fileSize <= 0)
            return 0;

        List<AjPart> orderedParts = parts
            .Where(part => part.FromPosition >= 0)
            .OrderBy(part => part.FromPosition)
            .ToList();

        long sum = 0;
        for (int index = 0; index < orderedParts.Count; index++)
        {
            long start = orderedParts[index].FromPosition;
            long end = index + 1 < orderedParts.Count
                ? orderedParts[index + 1].FromPosition
                : fileSize;
            if (orderedParts[index].Type == type && end > start)
                sum += end - start;
        }

        return sum;
    }

    private static IBrush BrushForType(int type)
    {
        if (type == DownloadPartListAggregator.ActiveDownloadPartType)
            return new SolidColorBrush(Color.FromRgb(129, 212, 250));
        if (type < 0)
            return new SolidColorBrush(Color.FromRgb(46, 125, 50));
        if (type == 0)
            return new SolidColorBrush(Color.FromRgb(5, 5, 5));
        if (type == 1)
            return new SolidColorBrush(Color.FromRgb(255, 224, 130));

        int clamped = Math.Clamp(type, 2, 8);
        double t = (clamped - 2) / 6.0;
        byte r = (byte)Math.Round(239 - (56 * t));
        byte g = (byte)Math.Round(154 - (126 * t));
        byte b = (byte)Math.Round(154 - (126 * t));
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private readonly record struct VisualSegment(long Start, long End, int Type);
}
