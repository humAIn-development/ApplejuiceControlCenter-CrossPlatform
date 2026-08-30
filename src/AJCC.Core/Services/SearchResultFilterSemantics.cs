using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class SearchResultFilterSemantics
{
    public static bool Matches(AjSearchEntry? entry, string? filterText)
    {
        if (entry is null)
            return false;

        string filter = (filterText ?? string.Empty).Trim();
        if (filter.Length == 0)
            return true;

        return Contains(entry.Filename, filter)
            || Contains(entry.Checksum, filter)
            || Contains(entry.SourceText, filter)
            || Contains(entry.SizeText, filter)
            || entry.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.SearchId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.FilenameUsers.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string? value, string filter)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
