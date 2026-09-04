using AJCC.Core.Models;

namespace AJCC.Core.Helpers;

public static class ShareDirectoryDraftSemantics
{
    public const string RecursiveShareMode = "subdirectory";
    public const string SingleDirectoryShareMode = "singledirectory";

    public static ShareDirectoryDraftResult Apply(
        IEnumerable<AjShareDirectory>? currentDirectories,
        string? path,
        string? shareMode)
    {
        string requestedPath = (path ?? string.Empty).Trim();
        if (requestedPath.Length == 0)
            throw new ArgumentException("Share directory path must not be empty.", nameof(path));

        string requestedMode = NormalizeRequestedMode(shareMode);
        List<AjShareDirectory> directories = Clone(currentDirectories);

        if (TryGetRecursiveAncestor(directories, requestedPath, ignoredDirectory: null, out string blockingAncestor))
        {
            return new ShareDirectoryDraftResult(
                directories,
                BlockedByRecursiveAncestor: true,
                Changed: false,
                BlockingAncestorPath: blockingAncestor,
                RemovedRedundantCount: 0);
        }

        AjShareDirectory? existing = directories.FirstOrDefault(directory => PathsEqual(directory.Name, requestedPath));
        bool changed;

        if (existing is null)
        {
            directories.Add(new AjShareDirectory { Name = requestedPath, ShareMode = requestedMode });
            changed = true;
        }
        else
        {
            changed = !existing.ShareMode.Equals(requestedMode, StringComparison.OrdinalIgnoreCase);
            existing.ShareMode = requestedMode;
        }

        int beforeNormalizeCount = directories.Count;
        IReadOnlyList<AjShareDirectory> normalized = Normalize(directories);
        int removedRedundantCount = Math.Max(0, beforeNormalizeCount - normalized.Count);

        return new ShareDirectoryDraftResult(
            normalized,
            BlockedByRecursiveAncestor: false,
            Changed: changed || removedRedundantCount > 0,
            BlockingAncestorPath: string.Empty,
            RemovedRedundantCount: removedRedundantCount);
    }

    public static IReadOnlyList<AjShareDirectory> Normalize(IEnumerable<AjShareDirectory>? directories)
    {
        List<AjShareDirectory> result = new();
        Dictionary<string, AjShareDirectory> firstByPath = new(StringComparer.OrdinalIgnoreCase);

        foreach (AjShareDirectory directory in Clone(directories))
        {
            string key = NormalizePath(directory.Name);
            if (key.Length == 0)
            {
                result.Add(directory);
                continue;
            }

            if (firstByPath.TryGetValue(key, out AjShareDirectory? first))
            {
                if (IsRecursive(directory) && !IsRecursive(first))
                    first.ShareMode = RecursiveShareMode;
                continue;
            }

            firstByPath[key] = directory;
            result.Add(directory);
        }

        foreach (AjShareDirectory candidate in result.ToList())
        {
            if (NormalizePath(candidate.Name).Length == 0)
                continue;

            if (TryGetRecursiveAncestor(result, candidate.Name, candidate, out _))
                result.Remove(candidate);
        }

        return result;
    }

    public static bool TryGetRecursiveAncestor(
        IEnumerable<AjShareDirectory>? directories,
        string? path,
        out string ancestorPath)
        => TryGetRecursiveAncestor(Clone(directories), path, ignoredDirectory: null, out ancestorPath);

    public static bool HasSharedDescendant(
        IEnumerable<AjShareDirectory>? directories,
        string? parentPath)
    {
        string normalizedParent = NormalizePath(parentPath);
        if (normalizedParent.Length == 0)
            return false;

        return directories?
            .Where(directory => directory is not null)
            .Any(directory => IsStrictChildDirectory(normalizedParent, directory.Name))
            ?? false;
    }

    public static ShareDirectoryVisualState GetVisualState(
        IEnumerable<AjShareDirectory>? directories,
        string? path)
    {
        string normalizedPath = NormalizePath(path);
        if (normalizedPath.Length == 0)
            return ShareDirectoryVisualState.NotShared;

        List<AjShareDirectory> snapshot = Clone(directories);
        AjShareDirectory? exact = snapshot.FirstOrDefault(
            directory => PathsEqual(directory.Name, normalizedPath));
        if (exact is not null)
        {
            return IsRecursive(exact)
                ? ShareDirectoryVisualState.RecursiveShared
                : ShareDirectoryVisualState.Shared;
        }

        return TryGetRecursiveAncestor(
            snapshot,
            normalizedPath,
            ignoredDirectory: null,
            out _)
            ? ShareDirectoryVisualState.RecursiveShared
            : ShareDirectoryVisualState.NotShared;
    }

    private static bool TryGetRecursiveAncestor(
        IReadOnlyList<AjShareDirectory> directories,
        string? path,
        AjShareDirectory? ignoredDirectory,
        out string ancestorPath)
    {
        string normalizedPath = NormalizePath(path);
        AjShareDirectory? ancestor = directories
            .Where(directory => !ReferenceEquals(directory, ignoredDirectory))
            .Where(IsRecursive)
            .Where(directory => IsStrictChildDirectory(directory.Name, normalizedPath))
            .OrderByDescending(directory => NormalizePath(directory.Name).Length)
            .FirstOrDefault();

        ancestorPath = ancestor?.Name ?? string.Empty;
        return ancestor is not null;
    }

    private static bool IsRecursive(AjShareDirectory directory)
        => directory.ShareMode.Equals(RecursiveShareMode, StringComparison.OrdinalIgnoreCase);

    private static bool IsStrictChildDirectory(string? parent, string? candidate)
    {
        string normalizedParent = NormalizePath(parent);
        string normalizedCandidate = NormalizePath(candidate);
        if (normalizedParent.Length == 0 || normalizedCandidate.Length == 0)
            return false;
        if (normalizedParent.Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            return false;

        string prefix = normalizedParent.EndsWith("/", StringComparison.Ordinal)
            ? normalizedParent
            : normalizedParent + "/";
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? left, string? right)
        => NormalizePath(left).Equals(NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRequestedMode(string? shareMode)
    {
        if (string.Equals(shareMode, RecursiveShareMode, StringComparison.OrdinalIgnoreCase))
            return RecursiveShareMode;
        if (string.Equals(shareMode, SingleDirectoryShareMode, StringComparison.OrdinalIgnoreCase))
            return SingleDirectoryShareMode;

        throw new ArgumentException("Share mode must be 'subdirectory' or 'singledirectory'.", nameof(shareMode));
    }

    private static List<AjShareDirectory> Clone(IEnumerable<AjShareDirectory>? directories)
        => directories?
            .Where(directory => directory is not null)
            .Select(directory => new AjShareDirectory
            {
                Name = directory.Name ?? string.Empty,
                ShareMode = directory.ShareMode ?? string.Empty
            })
            .ToList()
            ?? new List<AjShareDirectory>();

    private static string NormalizePath(string? path)
    {
        string value = (path ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
        if (value.Length == 0)
            return string.Empty;

        bool isUnc = value.StartsWith("//", StringComparison.Ordinal);
        bool isUnixRooted = !isUnc && value.StartsWith("/", StringComparison.Ordinal);
        bool hasDrive = value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':';

        string prefix;
        string remainder;
        if (isUnc)
        {
            prefix = "//";
            remainder = value.TrimStart('/');
        }
        else if (hasDrive)
        {
            prefix = char.ToUpperInvariant(value[0]) + ":/";
            remainder = value[2..].TrimStart('/');
        }
        else if (isUnixRooted)
        {
            prefix = "/";
            remainder = value.TrimStart('/');
        }
        else
        {
            prefix = string.Empty;
            remainder = value;
        }

        List<string> segments = new();
        foreach (string rawSegment in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawSegment == ".")
                continue;
            if (rawSegment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(rawSegment);
        }

        string suffix = string.Join('/', segments);
        if (prefix == "//")
            return suffix.Length == 0 ? "//" : "//" + suffix;
        if (prefix.Length > 0)
            return suffix.Length == 0 ? prefix : prefix + suffix;

        return suffix;
    }
}

public enum ShareDirectoryVisualState
{
    NotShared,
    Shared,
    RecursiveShared
}

public sealed record ShareDirectoryDraftResult(
    IReadOnlyList<AjShareDirectory> Directories,
    bool BlockedByRecursiveAncestor,
    bool Changed,
    string BlockingAncestorPath,
    int RemovedRedundantCount);
