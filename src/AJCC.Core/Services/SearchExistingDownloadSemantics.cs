using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class SearchExistingDownloadSemantics
{
    public static int Apply(
        IEnumerable<AjSearch> searches,
        IEnumerable<AjDownload> downloads)
    {
        ArgumentNullException.ThrowIfNull(searches);
        ArgumentNullException.ThrowIfNull(downloads);

        HashSet<string> downloadChecksums = downloads
            .Select(download => NormalizeChecksum(download.Hash))
            .Where(checksum => checksum.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int changed = 0;
        foreach (AjSearch search in searches)
        {
            foreach (AjSearchEntry entry in search.Entries)
            {
                bool exists = downloadChecksums.Contains(NormalizeChecksum(entry.Checksum));
                if (entry.IsExistingDownload == exists)
                    continue;

                entry.IsExistingDownload = exists;
                changed++;
            }
        }

        return changed;
    }

    public static bool IsExistingDownload(
        AjSearchEntry? entry,
        IEnumerable<AjDownload> downloads)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        if (entry is null)
            return false;

        string checksum = NormalizeChecksum(entry.Checksum);
        if (checksum.Length == 0)
            return false;

        return downloads.Any(download =>
            string.Equals(
                NormalizeChecksum(download.Hash),
                checksum,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeChecksum(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
