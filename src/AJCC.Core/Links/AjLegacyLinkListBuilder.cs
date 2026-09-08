using System.Globalization;
using System.Text;
using AJCC.Core.Helpers;
using AJCC.Core.Models;

namespace AJCC.Core.Links;

public static class AjLegacyLinkListBuilder
{
    public static bool IsValidShareEntry(AjShareFile? share)
        => share is not null
           && !string.IsNullOrWhiteSpace(share.DisplayFilename)
           && !string.IsNullOrWhiteSpace(share.Checksum)
           && share.Size > 0
           && !share.DisplayFilename.Contains('|');

    public static string BuildShareIdentityKey(AjShareFile share)
    {
        ArgumentNullException.ThrowIfNull(share);
        return share.Checksum + "|" + share.Size.ToString(CultureInfo.InvariantCulture) + "|" + share.DisplayFilename;
    }

    public static IReadOnlyList<AjShareFile> PrepareShareExport(IEnumerable<AjShareFile?> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);

        return shares
            .Where(IsValidShareEntry)
            .Select(share => share!)
            .DistinctBy(BuildShareIdentityKey, StringComparer.Ordinal)
            .OrderBy(share => share.DisplayFilename, NaturalStringComparer.Instance)
            .ThenBy(share => share.Size)
            .ThenBy(share => share.Checksum, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildLegacyContent(IReadOnlyList<AjShareFile> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);

        StringBuilder builder = new();
        builder.AppendLine("Quelle: Applejuice-Control-Center Share-Export");
        builder.AppendLine();
        builder.AppendLine("-----");
        builder.AppendLine("100");

        foreach (AjShareFile share in shares)
        {
            builder.AppendLine(share.DisplayFilename.Trim());
            builder.AppendLine(share.Checksum.Trim().ToLowerInvariant());
            builder.AppendLine(share.Size.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
