using AJCC.Core.Models;

namespace AJCC.Core.Services;

public enum ShareRuntimeSnapshotApplyMode
{
    FullRebuild,
    AttributesOnly,
    Delta
}

public static class ShareRuntimeSnapshotDeltaSemantics
{
    public const int DefaultFullRebuildAbsoluteThreshold = 10_000;
    public const double DefaultFullRebuildRatioThreshold = 0.25;

    public static ShareRuntimeSnapshotApplyMode Apply(
        IList<AjShareFile> current,
        IReadOnlyList<AjShareFile> incoming,
        int fullRebuildAbsoluteThreshold = DefaultFullRebuildAbsoluteThreshold,
        double fullRebuildRatioThreshold = DefaultFullRebuildRatioThreshold)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        if (fullRebuildAbsoluteThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(fullRebuildAbsoluteThreshold));
        if (double.IsNaN(fullRebuildRatioThreshold)
            || fullRebuildRatioThreshold <= 0
            || fullRebuildRatioThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fullRebuildRatioThreshold));
        }

        if (current.Count == 0 || incoming.Count == 0)
        {
            ReplaceAll(current, incoming);
            return ShareRuntimeSnapshotApplyMode.FullRebuild;
        }

        Dictionary<long, AjShareFile> existingById = new(current.Count);
        foreach (AjShareFile existing in current)
        {
            if (existing.Id <= 0 || !existingById.TryAdd(existing.Id, existing))
            {
                ReplaceAll(current, incoming);
                return ShareRuntimeSnapshotApplyMode.FullRebuild;
            }
        }

        Dictionary<long, AjShareFile> incomingById = new(incoming.Count);
        List<AjShareFile> added = new();
        List<(AjShareFile Existing, AjShareFile Incoming)> structureChanged = new();
        List<(AjShareFile Existing, AjShareFile Incoming)> attributeChanged = new();

        foreach (AjShareFile candidate in incoming)
        {
            if (candidate.Id <= 0 || !incomingById.TryAdd(candidate.Id, candidate))
            {
                ReplaceAll(current, incoming);
                return ShareRuntimeSnapshotApplyMode.FullRebuild;
            }

            if (!existingById.TryGetValue(candidate.Id, out AjShareFile? existing))
            {
                added.Add(candidate);
                continue;
            }

            if (!StructureEquals(existing, candidate))
            {
                structureChanged.Add((existing, candidate));
                continue;
            }

            if (!VolatileAttributesEqual(existing, candidate))
                attributeChanged.Add((existing, candidate));
        }

        List<long> removedIds = existingById.Keys
            .Where(id => !incomingById.ContainsKey(id))
            .ToList();
        int structuralChangeCount = added.Count + removedIds.Count + structureChanged.Count;

        if (structuralChangeCount == 0)
        {
            ApplyAttributeUpdates(attributeChanged);
            return ShareRuntimeSnapshotApplyMode.AttributesOnly;
        }

        int baseline = Math.Max(Math.Max(current.Count, incoming.Count), 1);
        bool requiresFullRebuild =
            structuralChangeCount >= fullRebuildAbsoluteThreshold
            || structuralChangeCount >= baseline * fullRebuildRatioThreshold;
        if (requiresFullRebuild)
        {
            ReplaceAll(current, incoming);
            return ShareRuntimeSnapshotApplyMode.FullRebuild;
        }

        if (removedIds.Count > 0)
        {
            HashSet<long> removed = removedIds.ToHashSet();
            for (int index = current.Count - 1; index >= 0; index--)
            {
                if (removed.Contains(current[index].Id))
                    current.RemoveAt(index);
            }
        }

        foreach ((AjShareFile existing, AjShareFile replacement) in structureChanged)
        {
            int index = IndexOfReference(current, existing);
            if (index < 0)
            {
                ReplaceAll(current, incoming);
                return ShareRuntimeSnapshotApplyMode.FullRebuild;
            }

            current[index] = replacement;
        }

        ApplyAttributeUpdates(attributeChanged);
        foreach (AjShareFile share in added)
            current.Add(share);

        return ShareRuntimeSnapshotApplyMode.Delta;
    }

    private static int IndexOfReference(IList<AjShareFile> items, AjShareFile target)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], target))
                return index;
        }

        return -1;
    }

    private static void ApplyAttributeUpdates(
        IEnumerable<(AjShareFile Existing, AjShareFile Incoming)> updates)
    {
        foreach ((AjShareFile existing, AjShareFile incoming) in updates)
        {
            existing.Priority = incoming.Priority;
            existing.LastAsked = incoming.LastAsked;
            existing.AskCount = incoming.AskCount;
            existing.SearchCount = incoming.SearchCount;
        }
    }

    private static bool StructureEquals(AjShareFile existing, AjShareFile incoming)
        => existing.Id == incoming.Id
            && existing.Size == incoming.Size
            && string.Equals(existing.Filename, incoming.Filename, StringComparison.Ordinal)
            && string.Equals(existing.Checksum, incoming.Checksum, StringComparison.OrdinalIgnoreCase);

    private static bool VolatileAttributesEqual(AjShareFile existing, AjShareFile incoming)
        => existing.Priority == incoming.Priority
            && existing.LastAsked == incoming.LastAsked
            && existing.AskCount == incoming.AskCount
            && existing.SearchCount == incoming.SearchCount;

    private static void ReplaceAll(IList<AjShareFile> current, IReadOnlyList<AjShareFile> incoming)
    {
        current.Clear();
        foreach (AjShareFile share in incoming)
            current.Add(share);
    }
}
