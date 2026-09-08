using System;
using System.Collections.Generic;
using System.Linq;
using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class SearchListCleanupSemantics
{
    public static bool CanRemove(AjSearch? search)
        => search is { Id: > 0, Running: false };

    public static IReadOnlyList<AjSearch> GetVisible(
        IEnumerable<AjSearch> searches,
        IReadOnlySet<long> hiddenIds)
    {
        ArgumentNullException.ThrowIfNull(searches);
        ArgumentNullException.ThrowIfNull(hiddenIds);

        return searches
            .Where(search => !hiddenIds.Contains(search.Id))
            .ToList();
    }

    public static int CountVisible(
        IEnumerable<AjSearch> searches,
        IReadOnlySet<long> hiddenIds)
        => GetVisible(searches, hiddenIds).Count;

    public static AjSearch? FindLastVisible(
        IEnumerable<AjSearch> searches,
        IReadOnlySet<long> hiddenIds)
        => GetVisible(searches, hiddenIds).LastOrDefault();

    public static bool TryHideAndRemove(
        ICollection<AjSearch> searches,
        ISet<long> hiddenIds,
        long searchId)
    {
        ArgumentNullException.ThrowIfNull(searches);
        ArgumentNullException.ThrowIfNull(hiddenIds);

        if (searchId <= 0)
            return false;

        AjSearch? search = searches.FirstOrDefault(item => item.Id == searchId);
        if (search?.Running == true)
            return false;

        hiddenIds.Add(searchId);
        if (search is not null)
            searches.Remove(search);

        return true;
    }

    public static bool RestoreVisibility(ISet<long> hiddenIds, long searchId)
    {
        ArgumentNullException.ThrowIfNull(hiddenIds);
        return searchId > 0 && hiddenIds.Remove(searchId);
    }
}
