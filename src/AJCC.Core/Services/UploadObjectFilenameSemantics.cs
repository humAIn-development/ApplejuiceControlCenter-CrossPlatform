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
