using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class DownloadPartListAggregator
{
    public const int ActiveDownloadPartType = -2;

    public static List<AjPart> Aggregate(
        IReadOnlyList<AjPart> downloadParts,
        IReadOnlyList<IReadOnlyList<AjPart>> sourcePartLists,
        long fileSize,
        IReadOnlyList<(long From, long To)>? activeTransferRanges = null)
    {
        if (fileSize <= 0)
            return downloadParts
                .Where(part => part.FromPosition >= 0)
                .OrderBy(part => part.FromPosition)
                .ToList();

        List<AjPart> orderedDownloadParts = NormalizePartList(downloadParts, fileSize);
        List<List<AjPart>> orderedSourcePartLists = sourcePartLists
            .Select(parts => NormalizePartList(parts, fileSize))
            .Where(parts => parts.Count > 0)
            .ToList();

        SortedSet<long> boundaries = new() { 0 };
        AddPartBoundaries(boundaries, orderedDownloadParts, fileSize);
        foreach (List<AjPart> sourceParts in orderedSourcePartLists)
            AddPartBoundaries(boundaries, sourceParts, fileSize);
        AddActiveTransferBoundaries(boundaries, activeTransferRanges, fileSize);

        List<long> positions = boundaries
            .Where(position => position >= 0 && position < fileSize)
            .OrderBy(position => position)
            .ToList();

        List<AjPart> aggregated = new(positions.Count);
        for (int index = 0; index < positions.Count; index++)
        {
            long start = positions[index];
            long end = index + 1 < positions.Count ? positions[index + 1] : fileSize;
            if (end <= start)
                continue;

            long midpoint = start + Math.Max(0, end - start) / 2;
            int downloadType = PartTypeAt(orderedDownloadParts, midpoint);
            int aggregateType;

            if (IsInsideActiveTransferRange(activeTransferRanges, midpoint))
            {
                aggregateType = ActiveDownloadPartType;
            }
            else if (downloadType < 0)
            {
                aggregateType = -1;
            }
            else
            {
                int sourceCount = 0;
                foreach (List<AjPart> sourceParts in orderedSourcePartLists)
                {
                    if (PartTypeAt(sourceParts, midpoint) != 0)
                        sourceCount++;
                }

                aggregateType = sourceCount;
            }

            if (aggregated.Count > 0 && aggregated[^1].Type == aggregateType)
                continue;

            aggregated.Add(new AjPart
            {
                FromPosition = start,
                Type = aggregateType
            });
        }

        return aggregated.Count > 0 ? aggregated : orderedDownloadParts;
    }

    private static List<AjPart> NormalizePartList(IReadOnlyList<AjPart> parts, long fileSize)
    {
        return parts
            .Where(part => part.FromPosition >= 0 && part.FromPosition < fileSize)
            .GroupBy(part => part.FromPosition)
            .Select(group => group.Last())
            .OrderBy(part => part.FromPosition)
            .Select(part => new AjPart
            {
                FromPosition = part.FromPosition,
                Type = part.Type
            })
            .ToList();
    }

    private static void AddPartBoundaries(SortedSet<long> boundaries, IReadOnlyList<AjPart> parts, long fileSize)
    {
        foreach (AjPart part in parts)
        {
            if (part.FromPosition >= 0 && part.FromPosition < fileSize)
                boundaries.Add(part.FromPosition);
        }
    }

    private static void AddActiveTransferBoundaries(
        SortedSet<long> boundaries,
        IReadOnlyList<(long From, long To)>? activeTransferRanges,
        long fileSize)
    {
        if (activeTransferRanges is null)
            return;

        foreach ((long from, long to) in activeTransferRanges)
        {
            if (from >= 0 && from < fileSize)
                boundaries.Add(from);
            if (to > 0 && to < fileSize)
                boundaries.Add(to);
        }
    }

    private static int PartTypeAt(IReadOnlyList<AjPart> parts, long position)
    {
        int type = 0;
        foreach (AjPart part in parts)
        {
            if (part.FromPosition > position)
                break;

            type = part.Type;
        }

        return type;
    }

    private static bool IsInsideActiveTransferRange(
        IReadOnlyList<(long From, long To)>? activeTransferRanges,
        long position)
    {
        if (activeTransferRanges is null)
            return false;

        foreach ((long from, long to) in activeTransferRanges)
        {
            if (position >= from && position < to)
                return true;
        }

        return false;
    }
}
