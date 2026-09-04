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
    public const int DefaultLargeShareThreshold = 5_000;
    public const int DefaultLargeShareBatchSize = 2_500;

    private sealed record SnapshotPlan(
        ShareRuntimeSnapshotApplyMode Mode,
        IReadOnlyList<AjShareFile> Incoming,
        IReadOnlyList<long> RemovedIds,
        IReadOnlyList<AjShareFile> Added,
        IReadOnlyList<(AjShareFile Existing, AjShareFile Incoming)> StructureChanged,
        IReadOnlyList<(AjShareFile Existing, AjShareFile Incoming)> AttributeChanged);

    public static ShareRuntimeSnapshotApplyMode Apply(
        IList<AjShareFile> current,
        IReadOnlyList<AjShareFile> incoming,
        int fullRebuildAbsoluteThreshold = DefaultFullRebuildAbsoluteThreshold,
        double fullRebuildRatioThreshold = DefaultFullRebuildRatioThreshold)
    {
        SnapshotPlan plan = Analyze(
            current,
            incoming,
            fullRebuildAbsoluteThreshold,
            fullRebuildRatioThreshold);
        return ApplyPlan(current, plan);
    }

    public static async Task<ShareRuntimeSnapshotApplyMode> ApplyBatchedAsync(
        IList<AjShareFile> current,
        IReadOnlyList<AjShareFile> incoming,
        Func<Task> yieldAsync,
        int largeShareThreshold = DefaultLargeShareThreshold,
        int batchSize = DefaultLargeShareBatchSize,
        int fullRebuildAbsoluteThreshold = DefaultFullRebuildAbsoluteThreshold,
        double fullRebuildRatioThreshold = DefaultFullRebuildRatioThreshold)
    {
        ArgumentNullException.ThrowIfNull(yieldAsync);
        if (largeShareThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(largeShareThreshold));
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        SnapshotPlan plan = Analyze(
            current,
            incoming,
            fullRebuildAbsoluteThreshold,
            fullRebuildRatioThreshold);

        int baseline = Math.Max(current.Count, incoming.Count);
        if (baseline < largeShareThreshold)
            return ApplyPlan(current, plan);

        return await ApplyPlanBatchedAsync(
            current,
            plan,
            batchSize,
            yieldAsync).ConfigureAwait(true);
    }

    private static SnapshotPlan Analyze(
        IList<AjShareFile> current,
        IReadOnlyList<AjShareFile> incoming,
        int fullRebuildAbsoluteThreshold,
        double fullRebuildRatioThreshold)
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
            return FullRebuildPlan(incoming);

        Dictionary<long, AjShareFile> existingById = new(current.Count);
        foreach (AjShareFile existing in current)
        {
            if (existing.Id <= 0 || !existingById.TryAdd(existing.Id, existing))
                return FullRebuildPlan(incoming);
        }

        Dictionary<long, AjShareFile> incomingById = new(incoming.Count);
        List<AjShareFile> added = new();
        List<(AjShareFile Existing, AjShareFile Incoming)> structureChanged = new();
        List<(AjShareFile Existing, AjShareFile Incoming)> attributeChanged = new();

        foreach (AjShareFile candidate in incoming)
        {
            if (candidate.Id <= 0 || !incomingById.TryAdd(candidate.Id, candidate))
                return FullRebuildPlan(incoming);

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
            return new SnapshotPlan(
                ShareRuntimeSnapshotApplyMode.AttributesOnly,
                incoming,
                Array.Empty<long>(),
                Array.Empty<AjShareFile>(),
                Array.Empty<(AjShareFile Existing, AjShareFile Incoming)>(),
                attributeChanged);
        }

        int baseline = Math.Max(Math.Max(current.Count, incoming.Count), 1);
        bool requiresFullRebuild =
            structuralChangeCount >= fullRebuildAbsoluteThreshold
            || structuralChangeCount >= baseline * fullRebuildRatioThreshold;
        if (requiresFullRebuild)
            return FullRebuildPlan(incoming);

        return new SnapshotPlan(
            ShareRuntimeSnapshotApplyMode.Delta,
            incoming,
            removedIds,
            added,
            structureChanged,
            attributeChanged);
    }

    private static SnapshotPlan FullRebuildPlan(IReadOnlyList<AjShareFile> incoming)
        => new(
            ShareRuntimeSnapshotApplyMode.FullRebuild,
            incoming,
            Array.Empty<long>(),
            Array.Empty<AjShareFile>(),
            Array.Empty<(AjShareFile Existing, AjShareFile Incoming)>(),
            Array.Empty<(AjShareFile Existing, AjShareFile Incoming)>());

    private static ShareRuntimeSnapshotApplyMode ApplyPlan(
        IList<AjShareFile> current,
        SnapshotPlan plan)
    {
        if (plan.Mode == ShareRuntimeSnapshotApplyMode.FullRebuild)
        {
            ReplaceAll(current, plan.Incoming);
            return ShareRuntimeSnapshotApplyMode.FullRebuild;
        }

        if (plan.Mode == ShareRuntimeSnapshotApplyMode.AttributesOnly)
        {
            ApplyAttributeUpdates(plan.AttributeChanged);
            return ShareRuntimeSnapshotApplyMode.AttributesOnly;
        }

        if (plan.RemovedIds.Count > 0)
        {
            HashSet<long> removed = plan.RemovedIds.ToHashSet();
            for (int index = current.Count - 1; index >= 0; index--)
            {
                if (removed.Contains(current[index].Id))
                    current.RemoveAt(index);
            }
        }

        foreach ((AjShareFile existing, AjShareFile replacement) in plan.StructureChanged)
        {
            int index = IndexOfReference(current, existing);
            if (index < 0)
            {
                ReplaceAll(current, plan.Incoming);
                return ShareRuntimeSnapshotApplyMode.FullRebuild;
            }

            current[index] = replacement;
        }

        ApplyAttributeUpdates(plan.AttributeChanged);
        foreach (AjShareFile share in plan.Added)
            current.Add(share);

        return ShareRuntimeSnapshotApplyMode.Delta;
    }

    private static async Task<ShareRuntimeSnapshotApplyMode> ApplyPlanBatchedAsync(
        IList<AjShareFile> current,
        SnapshotPlan plan,
        int batchSize,
        Func<Task> yieldAsync)
    {
        if (plan.Mode == ShareRuntimeSnapshotApplyMode.FullRebuild)
        {
            await ReplaceAllBatchedAsync(
                current,
                plan.Incoming,
                batchSize,
                yieldAsync).ConfigureAwait(true);
            return ShareRuntimeSnapshotApplyMode.FullRebuild;
        }

        if (plan.Mode == ShareRuntimeSnapshotApplyMode.AttributesOnly)
        {
            await ApplyAttributeUpdatesBatchedAsync(
                plan.AttributeChanged,
                batchSize,
                yieldAsync).ConfigureAwait(true);
            return ShareRuntimeSnapshotApplyMode.AttributesOnly;
        }

        if (plan.RemovedIds.Count > 0)
        {
            HashSet<long> removed = plan.RemovedIds.ToHashSet();
            int changed = 0;
            for (int index = current.Count - 1; index >= 0; index--)
            {
                if (!removed.Contains(current[index].Id))
                    continue;

                current.RemoveAt(index);
                changed++;
                if (changed % batchSize == 0)
                    await yieldAsync().ConfigureAwait(true);
            }
        }

        int structureUpdates = 0;
        foreach ((AjShareFile existing, AjShareFile replacement) in plan.StructureChanged)
        {
            int index = IndexOfReference(current, existing);
            if (index < 0)
            {
                await ReplaceAllBatchedAsync(
                    current,
                    plan.Incoming,
                    batchSize,
                    yieldAsync).ConfigureAwait(true);
                return ShareRuntimeSnapshotApplyMode.FullRebuild;
            }

            current[index] = replacement;
            structureUpdates++;
            if (structureUpdates % batchSize == 0)
                await yieldAsync().ConfigureAwait(true);
        }

        await ApplyAttributeUpdatesBatchedAsync(
            plan.AttributeChanged,
            batchSize,
            yieldAsync).ConfigureAwait(true);

        int added = 0;
        foreach (AjShareFile share in plan.Added)
        {
            current.Add(share);
            added++;
            if (added % batchSize == 0)
                await yieldAsync().ConfigureAwait(true);
        }

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
            ApplyAttributeUpdate(existing, incoming);
    }

    private static async Task ApplyAttributeUpdatesBatchedAsync(
        IReadOnlyList<(AjShareFile Existing, AjShareFile Incoming)> updates,
        int batchSize,
        Func<Task> yieldAsync)
    {
        for (int index = 0; index < updates.Count; index++)
        {
            (AjShareFile existing, AjShareFile incoming) = updates[index];
            ApplyAttributeUpdate(existing, incoming);

            if ((index + 1) % batchSize == 0)
                await yieldAsync().ConfigureAwait(true);
        }
    }

    private static void ApplyAttributeUpdate(AjShareFile existing, AjShareFile incoming)
    {
        existing.Priority = incoming.Priority;
        existing.LastAsked = incoming.LastAsked;
        existing.AskCount = incoming.AskCount;
        existing.SearchCount = incoming.SearchCount;
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

    private static async Task ReplaceAllBatchedAsync(
        IList<AjShareFile> current,
        IReadOnlyList<AjShareFile> incoming,
        int batchSize,
        Func<Task> yieldAsync)
    {
        current.Clear();

        for (int index = 0; index < incoming.Count; index++)
        {
            current.Add(incoming[index]);
            if ((index + 1) % batchSize == 0)
                await yieldAsync().ConfigureAwait(true);
        }
    }
}
