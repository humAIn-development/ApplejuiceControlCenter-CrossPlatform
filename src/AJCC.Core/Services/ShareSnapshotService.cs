using AJCC.Core.Helpers;
using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class ShareSnapshotService
{
    private const int CurrentFormatVersion = 1;

    public static ShareSnapshotDocument CreateSnapshot(
        string coreHost,
        int corePort,
        IEnumerable<ShareSnapshotSourceFile> files,
        IEnumerable<ShareSnapshotSourceRoot> roots)
    {
        List<ShareSnapshotFileEntry> normalizedFiles = (files ?? Array.Empty<ShareSnapshotSourceFile>())
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .GroupBy(file => NormalizePathKey(file.Path), StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(file => new ShareSnapshotFileEntry
            {
                Path = file.Path.Trim(),
                Size = Math.Max(0, file.Size)
            })
            .OrderBy(file => file.Path, NaturalStringComparer.Instance)
            .ToList();

        List<ShareSnapshotRootEntry> normalizedRoots = (roots ?? Array.Empty<ShareSnapshotSourceRoot>())
            .Where(root => !string.IsNullOrWhiteSpace(root.Path))
            .GroupBy(root => NormalizePathKey(root.Path), StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(root => new ShareSnapshotRootEntry
            {
                Path = root.Path.Trim(),
                ShareMode = NormalizeShareMode(root.ShareMode)
            })
            .OrderBy(root => root.Path, NaturalStringComparer.Instance)
            .ToList();

        return new ShareSnapshotDocument
        {
            FormatVersion = CurrentFormatVersion,
            CapturedAtUtc = DateTime.UtcNow,
            CoreHost = string.IsNullOrWhiteSpace(coreHost) ? "127.0.0.1" : coreHost.Trim(),
            CorePort = corePort,
            Files = normalizedFiles,
            Roots = normalizedRoots
        };
    }

    public static ShareSnapshotComparisonReport Compare(
        ShareSnapshotDocument current,
        ShareSnapshotDocument? baseline,
        string storagePath = "")
    {
        ArgumentNullException.ThrowIfNull(current);

        if (baseline is null)
        {
            return new ShareSnapshotComparisonReport
            {
                Current = current,
                StoragePath = storagePath,
                Notices = new[]
                {
                    new ShareSnapshotNotice
                    {
                        Severity = ShareSnapshotNoticeSeverity.Information,
                        Category = "Vergleichsbasis",
                        Message = "Es wurde noch keine lokale Vergleichsbasis gespeichert. Der aktuelle Stand kann jetzt bewusst als Ausgangspunkt übernommen werden."
                    }
                }
            };
        }

        Dictionary<string, ShareSnapshotFileEntry> currentFiles = BuildFileMap(current.Files);
        Dictionary<string, ShareSnapshotFileEntry> baselineFiles = BuildFileMap(baseline.Files);
        List<ShareSnapshotFileChange> addedFiles = new();
        List<ShareSnapshotFileChange> removedFiles = new();
        List<ShareSnapshotFileChange> changedFiles = new();

        foreach ((string key, ShareSnapshotFileEntry currentFile) in currentFiles)
        {
            if (!baselineFiles.TryGetValue(key, out ShareSnapshotFileEntry? previousFile))
            {
                addedFiles.Add(new ShareSnapshotFileChange
                {
                    Kind = ShareSnapshotFileChangeKind.Added,
                    Path = currentFile.Path,
                    CurrentSize = currentFile.Size
                });
                continue;
            }

            if (previousFile.Size != currentFile.Size)
            {
                changedFiles.Add(new ShareSnapshotFileChange
                {
                    Kind = ShareSnapshotFileChangeKind.Changed,
                    Path = currentFile.Path,
                    PreviousSize = previousFile.Size,
                    CurrentSize = currentFile.Size
                });
            }
        }

        foreach ((string key, ShareSnapshotFileEntry previousFile) in baselineFiles)
        {
            if (currentFiles.ContainsKey(key))
                continue;

            removedFiles.Add(new ShareSnapshotFileChange
            {
                Kind = ShareSnapshotFileChangeKind.Removed,
                Path = previousFile.Path,
                PreviousSize = previousFile.Size
            });
        }

        SortFileChanges(addedFiles);
        SortFileChanges(removedFiles);
        SortFileChanges(changedFiles);

        List<ShareSnapshotRootChange> rootChanges = CompareRoots(current.Roots, baseline.Roots);
        List<ShareSnapshotDirectoryDelta> directoryDeltas =
            BuildDirectoryDeltas(addedFiles, removedFiles, changedFiles);
        List<ShareSnapshotNotice> notices =
            BuildNotices(current, baseline, addedFiles, removedFiles, changedFiles, rootChanges);

        return new ShareSnapshotComparisonReport
        {
            Current = current,
            Baseline = baseline,
            AddedFiles = addedFiles,
            RemovedFiles = removedFiles,
            ChangedFiles = changedFiles,
            RootChanges = rootChanges,
            DirectoryDeltas = directoryDeltas,
            Notices = notices,
            StoragePath = storagePath
        };
    }

    private static Dictionary<string, ShareSnapshotFileEntry> BuildFileMap(
        IEnumerable<ShareSnapshotFileEntry> files)
    {
        Dictionary<string, ShareSnapshotFileEntry> result = new(StringComparer.Ordinal);
        foreach (ShareSnapshotFileEntry file in files ?? Array.Empty<ShareSnapshotFileEntry>())
        {
            if (string.IsNullOrWhiteSpace(file.Path))
                continue;

            result[NormalizePathKey(file.Path)] = file;
        }

        return result;
    }

    private static void SortFileChanges(List<ShareSnapshotFileChange> changes)
        => changes.Sort((left, right) => NaturalStringComparer.Instance.Compare(left.Path, right.Path));

    private static List<ShareSnapshotRootChange> CompareRoots(
        IEnumerable<ShareSnapshotRootEntry> currentRoots,
        IEnumerable<ShareSnapshotRootEntry> baselineRoots)
    {
        Dictionary<string, ShareSnapshotRootEntry> currentMap = BuildRootMap(currentRoots);
        Dictionary<string, ShareSnapshotRootEntry> baselineMap = BuildRootMap(baselineRoots);
        List<ShareSnapshotRootChange> result = new();

        foreach ((string key, ShareSnapshotRootEntry currentRoot) in currentMap)
        {
            if (!baselineMap.TryGetValue(key, out ShareSnapshotRootEntry? previousRoot))
            {
                bool containsPreviousRoot = baselineMap.Values.Any(root =>
                    IsParentPath(currentRoot.Path, root.Path));
                result.Add(new ShareSnapshotRootChange
                {
                    Kind = ShareSnapshotRootChangeKind.Added,
                    Path = currentRoot.Path,
                    CurrentShareMode = currentRoot.ShareMode,
                    Detail = containsPreviousRoot
                        ? "Die neue Freigabewurzel liegt oberhalb mindestens einer früheren Wurzel und kann dadurch einen größeren Verzeichnisbereich umfassen."
                        : "Diese Freigabewurzel war in der gespeicherten Vergleichsbasis nicht vorhanden."
                });
                continue;
            }

            if (!string.Equals(
                    NormalizeShareMode(previousRoot.ShareMode),
                    NormalizeShareMode(currentRoot.ShareMode),
                    StringComparison.OrdinalIgnoreCase))
            {
                bool expanded =
                    !string.Equals(previousRoot.ShareMode, "subdirectory", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(currentRoot.ShareMode, "subdirectory", StringComparison.OrdinalIgnoreCase);

                result.Add(new ShareSnapshotRootChange
                {
                    Kind = ShareSnapshotRootChangeKind.ModeChanged,
                    Path = currentRoot.Path,
                    PreviousShareMode = previousRoot.ShareMode,
                    CurrentShareMode = currentRoot.ShareMode,
                    Detail = expanded
                        ? "Die Freigabe umfasst jetzt auch Unterordner und wurde damit erweitert."
                        : "Der Freigabemodus unterscheidet sich von der gespeicherten Vergleichsbasis."
                });
            }
        }

        foreach ((string key, ShareSnapshotRootEntry previousRoot) in baselineMap)
        {
            if (currentMap.ContainsKey(key))
                continue;

            result.Add(new ShareSnapshotRootChange
            {
                Kind = ShareSnapshotRootChangeKind.Removed,
                Path = previousRoot.Path,
                PreviousShareMode = previousRoot.ShareMode,
                Detail = "Diese Freigabewurzel ist im aktuellen konfigurierten Stand nicht mehr vorhanden."
            });
        }

        return result
            .OrderBy(change => change.Path, NaturalStringComparer.Instance)
            .ToList();
    }

    private static Dictionary<string, ShareSnapshotRootEntry> BuildRootMap(
        IEnumerable<ShareSnapshotRootEntry> roots)
    {
        Dictionary<string, ShareSnapshotRootEntry> result = new(StringComparer.Ordinal);
        foreach (ShareSnapshotRootEntry root in roots ?? Array.Empty<ShareSnapshotRootEntry>())
        {
            if (string.IsNullOrWhiteSpace(root.Path))
                continue;

            result[NormalizePathKey(root.Path)] = root;
        }

        return result;
    }

    private static List<ShareSnapshotDirectoryDelta> BuildDirectoryDeltas(
        IEnumerable<ShareSnapshotFileChange> addedFiles,
        IEnumerable<ShareSnapshotFileChange> removedFiles,
        IEnumerable<ShareSnapshotFileChange> changedFiles)
    {
        Dictionary<string, MutableDirectoryDelta> deltas = new(StringComparer.Ordinal);

        foreach (ShareSnapshotFileChange change in addedFiles)
        {
            MutableDirectoryDelta delta = GetDirectoryDelta(deltas, change.DirectoryPath);
            delta.AddedCount++;
            delta.AddedSize = SafeAdd(delta.AddedSize, change.CurrentSize);
        }

        foreach (ShareSnapshotFileChange change in removedFiles)
        {
            MutableDirectoryDelta delta = GetDirectoryDelta(deltas, change.DirectoryPath);
            delta.RemovedCount++;
            delta.RemovedSize = SafeAdd(delta.RemovedSize, change.PreviousSize);
        }

        foreach (ShareSnapshotFileChange change in changedFiles)
            GetDirectoryDelta(deltas, change.DirectoryPath).ChangedCount++;

        return deltas.Values
            .Select(delta => new ShareSnapshotDirectoryDelta
            {
                DirectoryPath = delta.DirectoryPath,
                AddedCount = delta.AddedCount,
                RemovedCount = delta.RemovedCount,
                ChangedCount = delta.ChangedCount,
                AddedSize = delta.AddedSize,
                RemovedSize = delta.RemovedSize
            })
            .OrderByDescending(delta => delta.TotalChangeCount)
            .ThenBy(delta => delta.DirectoryPath, NaturalStringComparer.Instance)
            .ToList();
    }

    private static MutableDirectoryDelta GetDirectoryDelta(
        Dictionary<string, MutableDirectoryDelta> deltas,
        string directoryPath)
    {
        string displayPath =
            string.IsNullOrWhiteSpace(directoryPath) ? "(ohne Verzeichnisangabe)" : directoryPath;
        string key = NormalizePathKey(displayPath);

        if (!deltas.TryGetValue(key, out MutableDirectoryDelta? delta))
        {
            delta = new MutableDirectoryDelta { DirectoryPath = displayPath };
            deltas[key] = delta;
        }

        return delta;
    }

    private static List<ShareSnapshotNotice> BuildNotices(
        ShareSnapshotDocument current,
        ShareSnapshotDocument baseline,
        IReadOnlyList<ShareSnapshotFileChange> addedFiles,
        IReadOnlyList<ShareSnapshotFileChange> removedFiles,
        IReadOnlyList<ShareSnapshotFileChange> changedFiles,
        IReadOnlyList<ShareSnapshotRootChange> rootChanges)
    {
        List<ShareSnapshotNotice> notices = new();
        int netFileCount = current.FileCount - baseline.FileCount;

        if (baseline.FileCount > 0
            && netFileCount >= 100
            && current.FileCount >= baseline.FileCount * 1.10)
        {
            notices.Add(new ShareSnapshotNotice
            {
                Severity = ShareSnapshotNoticeSeverity.Review,
                Category = "Dateimenge",
                Message =
                    $"Die geladene Share-Dateiliste enthält {netFileCount:N0} Dateien mehr als die Vergleichsbasis ({baseline.FileCount:N0} → {current.FileCount:N0}). Der Zuwachs sollte bewusst geprüft werden."
            });
        }

        foreach (ShareSnapshotRootChange rootChange in rootChanges)
        {
            if (rootChange.Kind == ShareSnapshotRootChangeKind.Added
                && rootChange.Detail.Contains("oberhalb", StringComparison.OrdinalIgnoreCase))
            {
                notices.Add(new ShareSnapshotNotice
                {
                    Severity = ShareSnapshotNoticeSeverity.Review,
                    Category = "Freigabebereich",
                    Message =
                        $"Neue übergeordnete Freigabewurzel: {rootChange.Path}. Sie kann zusätzliche Unterordner sichtbar machen."
                });
            }
            else if (rootChange.Kind == ShareSnapshotRootChangeKind.ModeChanged
                     && string.Equals(
                         rootChange.CurrentShareMode,
                         "subdirectory",
                         StringComparison.OrdinalIgnoreCase))
            {
                notices.Add(new ShareSnapshotNotice
                {
                    Severity = ShareSnapshotNoticeSeverity.Review,
                    Category = "Freigabemodus",
                    Message = $"Die Freigabewurzel {rootChange.Path} umfasst jetzt Unterordner."
                });
            }
        }

        if (notices.Count == 0)
        {
            string message =
                addedFiles.Count == 0
                && removedFiles.Count == 0
                && changedFiles.Count == 0
                && rootChanges.Count == 0
                    ? "Der aktuelle geladene Sharezustand entspricht der gespeicherten Vergleichsbasis."
                    : "Es wurden Änderungen gefunden, aber keine der bewusst konservativen Auffälligkeitsregeln wurde ausgelöst.";

            notices.Add(new ShareSnapshotNotice
            {
                Severity = ShareSnapshotNoticeSeverity.Information,
                Category = "Vergleich",
                Message = message
            });
        }

        return notices;
    }

    private static bool IsParentPath(string possibleParent, string possibleChild)
    {
        PathComparisonInfo parent = GetPathComparisonInfo(possibleParent);
        PathComparisonInfo child = GetPathComparisonInfo(possibleChild);
        if (parent.IsWindowsLike != child.IsWindowsLike)
            return false;

        StringComparison comparison =
            parent.IsWindowsLike ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(parent.NormalizedPath, child.NormalizedPath, comparison))
            return false;

        string prefix = parent.NormalizedPath.EndsWith("/", StringComparison.Ordinal)
            ? parent.NormalizedPath
            : parent.NormalizedPath + "/";

        return child.NormalizedPath.StartsWith(prefix, comparison);
    }

    private static string NormalizePathKey(string path)
    {
        PathComparisonInfo info = GetPathComparisonInfo(path);
        string normalized =
            info.IsWindowsLike ? info.NormalizedPath.ToUpperInvariant() : info.NormalizedPath;

        return (info.IsWindowsLike ? "W:" : "U:") + normalized;
    }

    private static PathComparisonInfo GetPathComparisonInfo(string path)
    {
        string value = (path ?? string.Empty).Trim().Replace('\\', '/');
        bool windowsLike =
            (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
            || (path ?? string.Empty).Contains('\\')
            || value.StartsWith("//", StringComparison.Ordinal);

        while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
            value = value[..^1];

        return new PathComparisonInfo(value, windowsLike);
    }

    private static string NormalizeShareMode(string? shareMode)
        => string.Equals(shareMode?.Trim(), "subdirectory", StringComparison.OrdinalIgnoreCase)
            ? "subdirectory"
            : "directory";

    private static long SafeAdd(long current, long value)
    {
        value = Math.Max(0, value);
        return long.MaxValue - current < value ? long.MaxValue : current + value;
    }

    private readonly record struct PathComparisonInfo(string NormalizedPath, bool IsWindowsLike);

    private sealed class MutableDirectoryDelta
    {
        public string DirectoryPath { get; init; } = string.Empty;
        public int AddedCount { get; set; }
        public int RemovedCount { get; set; }
        public int ChangedCount { get; set; }
        public long AddedSize { get; set; }
        public long RemovedSize { get; set; }
    }
}
