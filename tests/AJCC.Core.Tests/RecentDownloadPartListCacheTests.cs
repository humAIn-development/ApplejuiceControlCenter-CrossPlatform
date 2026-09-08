using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class RecentDownloadPartListCacheTests
{
    [TestMethod]
    public void TryGet_ReturnsFreshEntryAndExpiresAfterTwoMinutes()
    {
        RecentDownloadPartListCache<string> cache = new();
        DateTimeOffset storedAt = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        cache.Remember(7, "cached", storedAt);

        Assert.IsTrue(cache.TryGet(7, storedAt.AddMinutes(2), out string? fresh));
        Assert.AreEqual("cached", fresh);

        Assert.IsFalse(cache.TryGet(7, storedAt.AddMinutes(2).AddTicks(1), out _));
        Assert.AreEqual(0, cache.Count);
    }

    [TestMethod]
    public void Remember_EvictsOldestEntryBeyondTwelveDownloads()
    {
        RecentDownloadPartListCache<string> cache = new();
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        for (int id = 1; id <= RecentDownloadPartListCache<string>.MaximumEntries + 1; id++)
            cache.Remember(id, $"value-{id}", now.AddSeconds(id));

        Assert.AreEqual(RecentDownloadPartListCache<string>.MaximumEntries, cache.Count);
        Assert.IsFalse(cache.TryGet(1, now.AddSeconds(30), out _));
        Assert.IsTrue(cache.TryGet(13, now.AddSeconds(30), out string? newest));
        Assert.AreEqual("value-13", newest);
    }

    [TestMethod]
    public void Remember_ReplacingEntryRefreshesAgeWithoutGrowingCache()
    {
        RecentDownloadPartListCache<string> cache = new();
        DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        cache.Remember(42, "old", now);
        cache.Remember(42, "new", now.AddMinutes(1));

        Assert.AreEqual(1, cache.Count);
        Assert.IsTrue(cache.TryGet(42, now.AddMinutes(2).AddSeconds(30), out string? value));
        Assert.AreEqual("new", value);
    }

    [TestMethod]
    public void Clear_RemovesAllEntries()
    {
        RecentDownloadPartListCache<string> cache = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        cache.Remember(1, "one", now);
        cache.Remember(2, "two", now);

        cache.Clear();

        Assert.AreEqual(0, cache.Count);
        Assert.IsFalse(cache.TryGet(1, now, out _));
    }
}
