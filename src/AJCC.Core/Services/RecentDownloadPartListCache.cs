namespace AJCC.Core.Services;

public sealed class RecentDownloadPartListCache<TValue>
    where TValue : class
{
    public const int MaximumEntries = 12;
    public static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(2);

    private readonly Dictionary<long, CacheEntry> _entries = new();

    public int Count => _entries.Count;

    public bool TryGet(
        long downloadId,
        DateTimeOffset nowUtc,
        out TValue? value)
    {
        value = null;
        if (downloadId <= 0 || !_entries.TryGetValue(downloadId, out CacheEntry? entry))
            return false;

        if (nowUtc - entry.StoredAtUtc > EntryLifetime)
        {
            _entries.Remove(downloadId);
            return false;
        }

        value = entry.Value;
        return true;
    }

    public void Remember(
        long downloadId,
        TValue value,
        DateTimeOffset nowUtc)
    {
        if (downloadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(downloadId));
        ArgumentNullException.ThrowIfNull(value);

        _entries[downloadId] = new CacheEntry(value, nowUtc);
        if (_entries.Count <= MaximumEntries)
            return;

        foreach (long staleId in _entries
                     .OrderBy(pair => pair.Value.StoredAtUtc)
                     .ThenBy(pair => pair.Key)
                     .Take(_entries.Count - MaximumEntries)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _entries.Remove(staleId);
        }
    }

    public void Clear()
        => _entries.Clear();

    private sealed record CacheEntry(TValue Value, DateTimeOffset StoredAtUtc);
}
