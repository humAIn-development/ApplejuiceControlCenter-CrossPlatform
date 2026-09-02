using System;
using System.Collections.Generic;
using System.Globalization;
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
    public const string PriorityHigh = "High";
    public const string PriorityLow = "Low";
    public const string PriorityExcluded = "Excluded";

    public static DownloadQueuePlan BuildPlan(
        IEnumerable<AjDownload> downloads,
        int configuredLimit,
        int commandCap = DefaultCommandCap,
        IReadOnlyDictionary<string, string>? priorities = null)
    {
        ArgumentNullException.ThrowIfNull(downloads);

        int limit = configuredLimit <= 0
            ? 0
            : Math.Clamp(configuredLimit, MinimumLimit, MaximumLimit);

        if (limit == 0)
            return new DownloadQueuePlan(0, Array.Empty<long>(), Array.Empty<long>(), Array.Empty<long>());

        List<AjDownload> nonTerminal = downloads
            .Where(download => !IsTerminal(download))
            .ToList();

        int excludedActiveCount = nonTerminal
            .Where(download => GetPriority(download, priorities) == PriorityExcluded)
            .Count(download => !IsPaused(download));
        int managedLimit = Math.Max(0, limit - excludedActiveCount);

        List<AjDownload> managed = nonTerminal
            .Where(download => GetPriority(download, priorities) != PriorityExcluded)
            .OrderBy(download => GetPriorityRank(download, priorities))
            .ThenByDescending(download => download.ProgressPercent)
            .ThenByDescending(download => download.ActiveSourceCount)
            .ThenByDescending(download => download.SourceCount)
            .ThenBy(download => download.Id)
            .ToList();

        List<long> shouldRunIds = managed
            .Take(managedLimit)
            .Select(download => download.Id)
            .ToList();
        HashSet<long> shouldRun = shouldRunIds.ToHashSet();

        int actionLimit = Math.Max(0, commandCap);
        List<long> resumeIds = managed
            .Where(download => shouldRun.Contains(download.Id) && IsPaused(download))
            .Take(actionLimit)
            .Select(download => download.Id)
            .ToList();
        List<long> pauseIds = managed
            .Where(download => !shouldRun.Contains(download.Id) && !IsPaused(download))
            .Take(actionLimit)
            .Select(download => download.Id)
            .ToList();

        return new DownloadQueuePlan(nonTerminal.Count, shouldRunIds, resumeIds, pauseIds);
    }

    public static string GetPriorityKey(AjDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);

        string hash = (download.Hash ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(hash))
            return "hash:" + hash.ToLowerInvariant();

        if (download.ShareId > 0)
            return "share:" + download.ShareId.ToString(CultureInfo.InvariantCulture);

        return "id:" + download.Id.ToString(CultureInfo.InvariantCulture);
    }

    public static string NormalizePriority(string? priority)
    {
        if (string.Equals(priority, PriorityHigh, StringComparison.OrdinalIgnoreCase))
            return PriorityHigh;
        if (string.Equals(priority, PriorityLow, StringComparison.OrdinalIgnoreCase))
            return PriorityLow;
        if (string.Equals(priority, PriorityExcluded, StringComparison.OrdinalIgnoreCase))
            return PriorityExcluded;
        return string.Empty;
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

    private static string GetPriority(
        AjDownload download,
        IReadOnlyDictionary<string, string>? priorities)
    {
        if (priorities is null
            || !priorities.TryGetValue(GetPriorityKey(download), out string? priority))
        {
            return string.Empty;
        }

        return NormalizePriority(priority);
    }

    private static int GetPriorityRank(
        AjDownload download,
        IReadOnlyDictionary<string, string>? priorities)
        => GetPriority(download, priorities) switch
        {
            PriorityHigh => 0,
            PriorityLow => 2,
            _ => 1
        };
}
