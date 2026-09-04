using System.Xml.Linq;
using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class UploadObjectFilenameSemantics
{
    public static IReadOnlyList<long> GetCandidateShareIds(
        IEnumerable<AjUpload> uploads,
        IReadOnlyDictionary<long, string> cachedByShareId,
        IReadOnlyDictionary<long, DateTime> failedAtUtc,
        DateTime nowUtc,
        TimeSpan retryDelay,
        int maxPerSweep)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(cachedByShareId);
        ArgumentNullException.ThrowIfNull(failedAtUtc);
        if (retryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        if (maxPerSweep <= 0)
            return Array.Empty<long>();

        return uploads
            .Where(upload => upload.ShareId > 0 && !AjStateUpdater.IsUsableUploadFilename(upload.Filename))
            .Select(upload => upload.ShareId)
            .Distinct()
            .Where(shareId => !cachedByShareId.ContainsKey(shareId))
            .Where(shareId => !failedAtUtc.TryGetValue(shareId, out DateTime failedAt)
                || nowUtc - failedAt >= retryDelay)
            .Take(maxPerSweep)
            .ToList();
    }

    public static bool ApplyCachedFilenames(
        IEnumerable<AjUpload> uploads,
        IReadOnlyDictionary<long, string> cachedByShareId)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(cachedByShareId);

        bool changed = false;
        foreach (AjUpload upload in uploads)
        {
            if (upload.ShareId <= 0 || AjStateUpdater.IsUsableUploadFilename(upload.Filename))
                continue;

            if (cachedByShareId.TryGetValue(upload.ShareId, out string? filename)
                && AjStateUpdater.IsUsableUploadFilename(filename))
            {
                upload.Filename = filename.Trim();
                changed = true;
            }
        }

        return changed;
    }

    public static bool ApplyDownloadFilenameFallbacks(
        IEnumerable<AjUpload> uploads,
        IEnumerable<AjDownload> downloads,
        IDictionary<long, string> cachedByShareId)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(cachedByShareId);

        Dictionary<long, string> downloadFilenameByShareId = downloads
            .Where(download => download.ShareId > 0)
            .Select(download => new
            {
                download.ShareId,
                Filename = GetUsableDownloadFilenameForUploadFallback(download)
            })
            .Where(item => AjStateUpdater.IsUsableUploadFilename(item.Filename))
            .GroupBy(item => item.ShareId)
            .ToDictionary(group => group.Key, group => group.First().Filename!);

        if (downloadFilenameByShareId.Count == 0)
            return false;

        bool changed = false;
        foreach (AjUpload upload in uploads)
        {
            if (upload.ShareId <= 0 || AjStateUpdater.IsUsableUploadFilename(upload.Filename))
                continue;

            if (!downloadFilenameByShareId.TryGetValue(upload.ShareId, out string? filename)
                || !AjStateUpdater.IsUsableUploadFilename(filename))
            {
                continue;
            }

            string usableFilename = filename.Trim();
            upload.Filename = usableFilename;
            cachedByShareId[upload.ShareId] = usableFilename;
            changed = true;
        }

        return changed;
    }

    private static string? GetUsableDownloadFilenameForUploadFallback(AjDownload download)
    {
        if (AjStateUpdater.IsUsableUploadFilename(download.DisplayFilename))
            return download.DisplayFilename.Trim();

        if (AjStateUpdater.IsUsableUploadFilename(download.Filename))
            return download.Filename.Trim();

        return null;
    }

    public static bool ApplyFilename(IEnumerable<AjUpload> uploads, long shareId, string? filename)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        if (shareId <= 0 || !AjStateUpdater.IsUsableUploadFilename(filename))
            return false;

        string usableFilename = filename!.Trim();
        bool changed = false;
        foreach (AjUpload upload in uploads.Where(upload => upload.ShareId == shareId))
        {
            if (AjStateUpdater.IsUsableUploadFilename(upload.Filename))
                continue;

            upload.Filename = usableFilename;
            changed = true;
        }

        return changed;
    }

    public static string? TryExtractUsableFilename(string? objectXml)
    {
        if (string.IsNullOrWhiteSpace(objectXml))
            return null;

        try
        {
            XDocument document = XDocument.Parse(objectXml);
            XElement? root = document.Root;
            if (root is null)
                return null;

            foreach (XElement element in root.DescendantsAndSelf())
            {
                string filename = ((string?)element.Attribute("filename")
                    ?? (string?)element.Attribute("name")
                    ?? string.Empty).Trim();

                if (AjStateUpdater.IsUsableUploadFilename(filename))
                    return filename;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
