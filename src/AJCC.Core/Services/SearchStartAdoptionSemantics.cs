using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class SearchStartAdoptionSemantics
{
    public static AjSearch? FindCandidate(
        IEnumerable<AjSearch> searches,
        string? searchText,
        long previousMaxSearchId,
        bool allowExistingFallback)
    {
        ArgumentNullException.ThrowIfNull(searches);

        long baseline = Math.Max(0, previousMaxSearchId);
        List<AjSearch> available = searches
            .Where(search => search.Id > 0)
            .ToList();

        AjSearch? newSearch = available
            .Where(search => search.Id > baseline)
            .OrderByDescending(search => search.Id)
            .FirstOrDefault();
        if (newSearch is not null)
            return newSearch;

        if (!allowExistingFallback)
            return null;

        string normalizedText = (searchText ?? string.Empty).Trim();
        if (normalizedText.Length == 0)
            return null;

        return available
            .Where(search => search.Id <= baseline)
            .Where(search => string.Equals(
                (search.SearchText ?? string.Empty).Trim(),
                normalizedText,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(search => search.Running)
            .ThenByDescending(search => search.Id)
            .FirstOrDefault();
    }
}
