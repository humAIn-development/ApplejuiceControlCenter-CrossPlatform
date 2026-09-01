using System;
using System.Collections.Generic;
using System.Linq;
using AJCC.Core.Models;

namespace AJCC.Core.Services;

public sealed record DownloadQueuePlan(
    int EligibleCount,
    IReadOnlyList<long> ShouldRunIds,
    IReadOnlyList<long> ResumeIds,
    IReadOnlyList<long> PauseIds);

public static class DownloadQueuePlanningSemantics
{
    public const int MinimumLimit = 1;
    public const int DefaultLimit = 5;
    public const int MaximumLimit = 100;
    public const int DefaultCommandCap = 5;

    public static DownloadQueuePlan BuildPlan(
        IEnumerable<AjDownload> downloads,
        int configuredLimit,
        int commandCap = DefaultCommandCap)
    {
        ArgumentNullException.ThrowIfNull(downloads);

        int limit = configuredLimit <= 0
            ? 0
            : Math.Clamp(configuredLimit, MinimumLimit, MaximumLimit);

        if (limit == 0)
            return new DownloadQueuePlan(0, Array.Empty<long>(), Array.Empty<long>(), Array.Empty<long>());

        List<AjDownload> eligible = downloads
            .Where(download => !IsTerminal(download))
            .OrderByDescending(download => download.ProgressPercent)
            .ThenByDescending(download => download.ActiveSourceCount)
            .ThenByDescending(download => download.SourceCount)
            .ThenBy(download => download.Id)
            .ToList();

        List<long> shouldRunIds = eligible
            .Take(limit)
            .Select(download => download.Id)
            .ToList();
        HashSet<long> shouldRun = shouldRunIds.ToHashSet();

        int actionLimit = Math.Max(0, commandCap);
        List<long> resumeIds = eligible
            .Where(download => shouldRun.Contains(download.Id) && IsPaused(download))
            .Take(actionLimit)
            .Select(download => download.Id)
            .ToList();
        List<long> pauseIds = eligible
            .Where(download => !shouldRun.Contains(download.Id) && !IsPaused(download))
            .Take(actionLimit)
            .Select(download => download.Id)
            .ToList();

        return new DownloadQueuePlan(eligible.Count, shouldRunIds, resumeIds, pauseIds);
    }

    public static bool IsTerminal(AjDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);

        if (download.Status is 14 or 15 or 17)
            return true;

        string statusText = (download.StatusText ?? string.Empty).Trim();
        return statusText.Contains("Fertig", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Abbruch", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Abgebrochen", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Cancelled", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Canceled", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Complete", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Done", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPaused(AjDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);

        if (download.Status == 18)
            return true;

        string statusText = (download.StatusText ?? string.Empty).Trim();
        return statusText.Contains("Pausiert", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Paused", StringComparison.OrdinalIgnoreCase);
    }
}
