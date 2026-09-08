using AJCC.Core.Helpers;

namespace AJCC.Core.Models;

public sealed record ShareSnapshotSourceFile(string Path, long Size);

public sealed record ShareSnapshotSourceRoot(string Path, string ShareMode);

public sealed class ShareSnapshotDocument
{
    public int FormatVersion { get; set; } = 1;
    public DateTime CapturedAtUtc { get; set; }
    public string CoreHost { get; set; } = string.Empty;
    public int CorePort { get; set; }
    public List<ShareSnapshotFileEntry> Files { get; set; } = new();
    public List<ShareSnapshotRootEntry> Roots { get; set; } = new();

    public int FileCount => Files.Count;
    public int RootCount => Roots.Count;
}

public sealed class ShareSnapshotFileEntry
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed class ShareSnapshotRootEntry
{
    public string Path { get; set; } = string.Empty;
    public string ShareMode { get; set; } = string.Empty;
}

public sealed class ShareSnapshotLoadResult
{
    public ShareSnapshotDocument? Snapshot { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
}

public enum ShareSnapshotFileChangeKind
{
    Added,
    Removed,
    Changed
}

public sealed class ShareSnapshotFileChange
{
    public ShareSnapshotFileChangeKind Kind { get; init; }
    public string Path { get; init; } = string.Empty;
    public long PreviousSize { get; init; }
    public long CurrentSize { get; init; }

    public string Filename
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
                return string.Empty;

            int slash = Path.LastIndexOf('/');
            int backslash = Path.LastIndexOf('\\');
            int index = Math.Max(slash, backslash);
            return index >= 0 && index + 1 < Path.Length ? Path[(index + 1)..] : Path;
        }
    }

    public string DirectoryPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
                return string.Empty;

            int slash = Path.LastIndexOf('/');
            int backslash = Path.LastIndexOf('\\');
            int index = Math.Max(slash, backslash);
            return index > 0 ? Path[..index] : string.Empty;
        }
    }

    public string PreviousSizeText
        => Kind == ShareSnapshotFileChangeKind.Added
            ? "–"
            : DisplayFormatHelper.Bytes(Math.Max(0, PreviousSize));

    public string CurrentSizeText
        => Kind == ShareSnapshotFileChangeKind.Removed
            ? "–"
            : DisplayFormatHelper.Bytes(Math.Max(0, CurrentSize));

    public string ChangeText => Kind switch
    {
        ShareSnapshotFileChangeKind.Added => "Neu sichtbar",
        ShareSnapshotFileChangeKind.Removed => "Nicht mehr sichtbar",
        _ => PreviousSize != CurrentSize ? "Größe geändert" : "Metadaten geändert"
    };
}

public enum ShareSnapshotRootChangeKind
{
    Added,
    Removed,
    ModeChanged
}

public sealed class ShareSnapshotRootChange
{
    public ShareSnapshotRootChangeKind Kind { get; init; }
    public string Path { get; init; } = string.Empty;
    public string PreviousShareMode { get; init; } = string.Empty;
    public string CurrentShareMode { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public string ChangeText => Kind switch
    {
        ShareSnapshotRootChangeKind.Added => "Neue Freigabewurzel",
        ShareSnapshotRootChangeKind.Removed => "Freigabewurzel entfernt",
        _ => "Freigabemodus geändert"
    };

    public string PreviousModeText => FormatShareMode(PreviousShareMode);
    public string CurrentModeText => FormatShareMode(CurrentShareMode);

    private static string FormatShareMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "–";

        return string.Equals(value, "subdirectory", StringComparison.OrdinalIgnoreCase)
            ? "inkl. Unterordner"
            : "nur dieser Ordner";
    }
}

public sealed class ShareSnapshotDirectoryDelta
{
    public string DirectoryPath { get; init; } = string.Empty;
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public int ChangedCount { get; init; }
    public long AddedSize { get; init; }
    public long RemovedSize { get; init; }

    public int TotalChangeCount => AddedCount + RemovedCount + ChangedCount;
    public int NetFileCount => AddedCount - RemovedCount;
    public string AddedSizeText => DisplayFormatHelper.Bytes(Math.Max(0, AddedSize));
    public string RemovedSizeText => DisplayFormatHelper.Bytes(Math.Max(0, RemovedSize));
    public string NetFileCountText => NetFileCount > 0 ? $"+{NetFileCount:N0}" : NetFileCount.ToString("N0");
}

public enum ShareSnapshotNoticeSeverity
{
    Information,
    Review
}

public sealed class ShareSnapshotNotice
{
    public ShareSnapshotNoticeSeverity Severity { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string SeverityText => Severity == ShareSnapshotNoticeSeverity.Review ? "Prüfen" : "Hinweis";
}

public sealed class ShareSnapshotComparisonReport
{
    public ShareSnapshotDocument Current { get; init; } = new();
    public ShareSnapshotDocument? Baseline { get; init; }
    public IReadOnlyList<ShareSnapshotFileChange> AddedFiles { get; init; } = Array.Empty<ShareSnapshotFileChange>();
    public IReadOnlyList<ShareSnapshotFileChange> RemovedFiles { get; init; } = Array.Empty<ShareSnapshotFileChange>();
    public IReadOnlyList<ShareSnapshotFileChange> ChangedFiles { get; init; } = Array.Empty<ShareSnapshotFileChange>();
    public IReadOnlyList<ShareSnapshotRootChange> RootChanges { get; init; } = Array.Empty<ShareSnapshotRootChange>();
    public IReadOnlyList<ShareSnapshotDirectoryDelta> DirectoryDeltas { get; init; } = Array.Empty<ShareSnapshotDirectoryDelta>();
    public IReadOnlyList<ShareSnapshotNotice> Notices { get; init; } = Array.Empty<ShareSnapshotNotice>();
    public string StoragePath { get; init; } = string.Empty;

    public bool HasBaseline => Baseline is not null;
    public int TotalChangeCount => AddedFiles.Count + RemovedFiles.Count + ChangedFiles.Count + RootChanges.Count;
    public long CurrentTotalSize => SumSizes(Current.Files);
    public long BaselineTotalSize => Baseline is null ? 0 : SumSizes(Baseline.Files);
    public long NetSizeChange => CurrentTotalSize - BaselineTotalSize;
    public int NetFileCount => Current.FileCount - (Baseline?.FileCount ?? 0);
    public string CurrentTotalSizeText => DisplayFormatHelper.Bytes(Math.Max(0, CurrentTotalSize));
    public string BaselineTotalSizeText => Baseline is null ? "–" : DisplayFormatHelper.Bytes(Math.Max(0, BaselineTotalSize));
    public string NetFileCountText => NetFileCount > 0 ? $"+{NetFileCount:N0}" : NetFileCount.ToString("N0");

    public string NetSizeText
    {
        get
        {
            string value = DisplayFormatHelper.Bytes(Math.Abs(NetSizeChange));
            return NetSizeChange > 0 ? $"+{value}" : NetSizeChange < 0 ? $"−{value}" : value;
        }
    }

    private static long SumSizes(IEnumerable<ShareSnapshotFileEntry> files)
    {
        long total = 0;
        foreach (ShareSnapshotFileEntry file in files)
        {
            long value = Math.Max(0, file.Size);
            if (long.MaxValue - total < value)
                return long.MaxValue;

            total += value;
        }

        return total;
    }
}
