using System.Globalization;
using AJCC.Core.Helpers;
using AJCC.Core.Links;
using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class ShareAjfspDragExportSemantics
{
    public static string BuildPlainTextLinkList(IEnumerable<AjShareFile>? shares)
    {
        List<AjShareFile> selected = NormalizeForExport(shares);
        return string.Join(
            Environment.NewLine,
            selected.Select(share =>
                AjfspLinkBuilder.BuildFileLink(
                    share.DisplayFilename.Trim(),
                    share.Checksum.Trim(),
                    share.Size)));
    }

    public static IReadOnlyList<AjShareFile> SelectRecursiveDirectoryFiles(
        IEnumerable<AjShareFile>? shares,
        string? directoryPath,
        char separator)
    {
        if (separator is not ('\\' or '/'))
            throw new ArgumentOutOfRangeException(nameof(separator), "Separator must be '/' or '\\'.");

        string target = NormalizePath(directoryPath, separator);
        if (target.Length == 0)
            return Array.Empty<AjShareFile>();

        StringComparison comparison = separator == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string prefix = target.EndsWith(separator)
            ? target
            : target + separator;

        return (shares ?? Array.Empty<AjShareFile>())
            .Where(share => share is not null)
            .Where(share =>
            {
                string path = NormalizePath(share.DirectoryPath, separator);
                return path.Equals(target, comparison)
                    || path.StartsWith(prefix, comparison);
            })
            .ToList();
    }

    public static string BuildRecursiveDirectoryPlainTextLinkList(
        IEnumerable<AjShareFile>? shares,
        string? directoryPath,
        char separator)
        => BuildPlainTextLinkList(
            SelectRecursiveDirectoryFiles(shares, directoryPath, separator));

    private static List<AjShareFile> NormalizeForExport(IEnumerable<AjShareFile>? shares)
        => (shares ?? Array.Empty<AjShareFile>())
            .Where(IsValidExportEntry)
            .DistinctBy(share =>
                share.Checksum.Trim()
                + "|"
                + share.Size.ToString(CultureInfo.InvariantCulture)
                + "|"
                + share.DisplayFilename.Trim())
            .OrderBy(
                share => share.DisplayFilename.Trim(),
                NaturalStringComparer.Instance)
            .ThenBy(share => share.Size)
            .ThenBy(share => share.Checksum.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsValidExportEntry(AjShareFile? share)
        => share is not null
            && !string.IsNullOrWhiteSpace(share.DisplayFilename)
            && !string.IsNullOrWhiteSpace(share.Checksum)
            && share.Size >= 0;

    private static string NormalizePath(string? value, char separator)
    {
        string path = (value ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0)
            return string.Empty;

        char other = separator == '/' ? '\\' : '/';
        path = path.Replace(other, separator);

        if (separator == '/' && path == "/")
            return path;

        if (separator == '\\'
            && path.Length == 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && path[2] == '\\')
        {
            return path;
        }

        return path.TrimEnd(separator);
    }
}
