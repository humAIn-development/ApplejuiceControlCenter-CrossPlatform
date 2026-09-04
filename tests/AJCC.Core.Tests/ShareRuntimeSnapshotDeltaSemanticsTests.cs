using System.Collections.ObjectModel;
using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ShareRuntimeSnapshotDeltaSemanticsTests
{
    [TestMethod]
    public void Apply_SameStructureUpdatesAttributesInPlace()
    {
        AjShareFile first = Share(1, @"C:\Share\one.bin", 10, "aa", priority: 1);
        AjShareFile second = Share(2, @"C:\Share\two.bin", 20, "bb", priority: 2);
        ObservableCollection<AjShareFile> current = [first, second];
        List<AjShareFile> incoming =
        [
            Share(1, @"C:\Share\one.bin", 10, "AA", priority: 7, lastAsked: 11, askCount: 12, searchCount: 13),
            Share(2, @"C:\Share\two.bin", 20, "bb", priority: 2)
        ];

        ShareRuntimeSnapshotApplyMode mode =
            ShareRuntimeSnapshotDeltaSemantics.Apply(current, incoming);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.AttributesOnly, mode);
        Assert.AreSame(first, current[0]);
        Assert.AreSame(second, current[1]);
        Assert.AreEqual(7, current[0].Priority);
        Assert.AreEqual(11L, current[0].LastAsked);
        Assert.AreEqual(12L, current[0].AskCount);
        Assert.AreEqual(13L, current[0].SearchCount);
    }

    [TestMethod]
    public void Apply_SmallDeltaPreservesUnaffectedObjects()
    {
        AjShareFile keepOne = Share(1, "one.bin", 10, "aa", priority: 1);
        AjShareFile remove = Share(2, "two.bin", 20, "bb", priority: 2);
        AjShareFile replace = Share(3, "three-old.bin", 30, "cc", priority: 3);
        AjShareFile keepFour = Share(4, "four.bin", 40, "dd", priority: 4);
        AjShareFile keepFive = Share(5, "five.bin", 50, "ee", priority: 5);
        AjShareFile keepSix = Share(6, "six.bin", 60, "ff", priority: 6);
        AjShareFile keepSeven = Share(7, "seven.bin", 70, "gg", priority: 7);
        AjShareFile keepEight = Share(8, "eight.bin", 80, "hh", priority: 8);
        AjShareFile keepNine = Share(9, "nine.bin", 90, "ii", priority: 9);
        AjShareFile keepTen = Share(10, "ten.bin", 100, "jj", priority: 10);
        ObservableCollection<AjShareFile> current =
        [
            keepOne, remove, replace, keepFour, keepFive,
            keepSix, keepSeven, keepEight, keepNine, keepTen
        ];
        AjShareFile replacement = Share(3, "three-new.bin", 30, "cc", priority: 33);
        AjShareFile added = Share(11, "eleven.bin", 110, "kk", priority: 11);
        List<AjShareFile> incoming =
        [
            Share(1, "one.bin", 10, "aa", priority: 21),
            replacement,
            Share(4, "four.bin", 40, "dd", priority: 4),
            Share(5, "five.bin", 50, "ee", priority: 5),
            Share(6, "six.bin", 60, "ff", priority: 6),
            Share(7, "seven.bin", 70, "gg", priority: 7),
            Share(8, "eight.bin", 80, "hh", priority: 8),
            Share(9, "nine.bin", 90, "ii", priority: 9),
            Share(10, "ten.bin", 100, "jj", priority: 10),
            added
        ];

        ShareRuntimeSnapshotApplyMode mode =
            ShareRuntimeSnapshotDeltaSemantics.Apply(
                current,
                incoming,
                fullRebuildAbsoluteThreshold: 100,
                fullRebuildRatioThreshold: 0.5);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.Delta, mode);
        Assert.AreEqual(10, current.Count);
        Assert.AreSame(keepOne, current.Single(item => item.Id == 1));
        Assert.AreEqual(21, keepOne.Priority);
        Assert.IsFalse(current.Any(item => item.Id == 2));
        Assert.AreSame(replacement, current.Single(item => item.Id == 3));
        Assert.AreSame(keepFour, current.Single(item => item.Id == 4));
        Assert.AreSame(added, current.Single(item => item.Id == 11));
    }

    [TestMethod]
    public void Apply_QuarterStructuralChangeUsesFullRebuild()
    {
        ObservableCollection<AjShareFile> current =
        [
            Share(1, "one.bin", 10, "aa"),
            Share(2, "two.bin", 20, "bb"),
            Share(3, "three.bin", 30, "cc"),
            Share(4, "four.bin", 40, "dd")
        ];
        List<AjShareFile> incoming =
        [
            Share(1, "one.bin", 10, "aa"),
            Share(2, "two.bin", 20, "bb"),
            Share(3, "three.bin", 30, "cc"),
            Share(5, "five.bin", 50, "ee")
        ];

        ShareRuntimeSnapshotApplyMode mode =
            ShareRuntimeSnapshotDeltaSemantics.Apply(current, incoming);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.FullRebuild, mode);
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 5 }, current.Select(item => item.Id).ToArray());
        Assert.AreSame(incoming[0], current[0]);
    }

    [TestMethod]
    public void Apply_DuplicateOrNonPositiveIdsFallBackToFullRebuild()
    {
        AjShareFile old = Share(1, "old.bin", 10, "aa");
        ObservableCollection<AjShareFile> current = [old, Share(2, "two.bin", 20, "bb")];
        List<AjShareFile> incoming =
        [
            Share(1, "new.bin", 10, "aa"),
            Share(1, "duplicate.bin", 20, "bb")
        ];

        ShareRuntimeSnapshotApplyMode mode =
            ShareRuntimeSnapshotDeltaSemantics.Apply(current, incoming);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.FullRebuild, mode);
        Assert.AreSame(incoming[0], current[0]);
        Assert.AreSame(incoming[1], current[1]);

        current = [Share(1, "one.bin", 10, "aa")];
        incoming = [Share(0, "invalid.bin", 10, "aa")];

        mode = ShareRuntimeSnapshotDeltaSemantics.Apply(current, incoming);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.FullRebuild, mode);
        Assert.AreSame(incoming[0], current[0]);
    }

    [TestMethod]
    public void Apply_EmptySnapshotsUseFullRebuild()
    {
        ObservableCollection<AjShareFile> current = [];
        List<AjShareFile> incoming = [Share(1, "one.bin", 10, "aa")];

        ShareRuntimeSnapshotApplyMode mode =
            ShareRuntimeSnapshotDeltaSemantics.Apply(current, incoming);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.FullRebuild, mode);
        Assert.AreSame(incoming[0], current[0]);

        incoming = [];
        mode = ShareRuntimeSnapshotDeltaSemantics.Apply(current, incoming);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.FullRebuild, mode);
        Assert.AreEqual(0, current.Count);
    }


    [TestMethod]
    public async Task ApplyBatchedAsync_LargeFullRebuildYieldsBetweenBatches()
    {
        ObservableCollection<AjShareFile> current = [];
        List<AjShareFile> incoming =
        [
            Share(1, "one.bin", 10, "aa"),
            Share(2, "two.bin", 20, "bb"),
            Share(3, "three.bin", 30, "cc"),
            Share(4, "four.bin", 40, "dd"),
            Share(5, "five.bin", 50, "ee")
        ];
        int yields = 0;

        ShareRuntimeSnapshotApplyMode mode =
            await ShareRuntimeSnapshotDeltaSemantics.ApplyBatchedAsync(
                current,
                incoming,
                () =>
                {
                    yields++;
                    return Task.CompletedTask;
                },
                largeShareThreshold: 4,
                batchSize: 2);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.FullRebuild, mode);
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5 }, current.Select(item => item.Id).ToArray());
        Assert.AreEqual(2, yields);
    }

    [TestMethod]
    public async Task ApplyBatchedAsync_LargeAttributeUpdatePreservesObjectsAndYields()
    {
        AjShareFile first = Share(1, "one.bin", 10, "aa", priority: 1);
        AjShareFile second = Share(2, "two.bin", 20, "bb", priority: 1);
        AjShareFile third = Share(3, "three.bin", 30, "cc", priority: 1);
        AjShareFile fourth = Share(4, "four.bin", 40, "dd", priority: 1);
        ObservableCollection<AjShareFile> current = [first, second, third, fourth];
        List<AjShareFile> incoming =
        [
            Share(1, "one.bin", 10, "aa", priority: 11),
            Share(2, "two.bin", 20, "bb", priority: 12),
            Share(3, "three.bin", 30, "cc", priority: 13),
            Share(4, "four.bin", 40, "dd", priority: 14)
        ];
        int yields = 0;

        ShareRuntimeSnapshotApplyMode mode =
            await ShareRuntimeSnapshotDeltaSemantics.ApplyBatchedAsync(
                current,
                incoming,
                () =>
                {
                    yields++;
                    return Task.CompletedTask;
                },
                largeShareThreshold: 4,
                batchSize: 2);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.AttributesOnly, mode);
        Assert.AreSame(first, current[0]);
        Assert.AreSame(second, current[1]);
        Assert.AreSame(third, current[2]);
        Assert.AreSame(fourth, current[3]);
        CollectionAssert.AreEqual(new[] { 11, 12, 13, 14 }, current.Select(item => item.Priority).ToArray());
        Assert.AreEqual(2, yields);
    }

    [TestMethod]
    public async Task ApplyBatchedAsync_SmallSnapshotUsesSynchronousPathWithoutYield()
    {
        ObservableCollection<AjShareFile> current = [Share(1, "one.bin", 10, "aa", priority: 1)];
        List<AjShareFile> incoming = [Share(1, "one.bin", 10, "aa", priority: 2)];
        int yields = 0;

        ShareRuntimeSnapshotApplyMode mode =
            await ShareRuntimeSnapshotDeltaSemantics.ApplyBatchedAsync(
                current,
                incoming,
                () =>
                {
                    yields++;
                    return Task.CompletedTask;
                },
                largeShareThreshold: 4,
                batchSize: 2);

        Assert.AreEqual(ShareRuntimeSnapshotApplyMode.AttributesOnly, mode);
        Assert.AreEqual(2, current[0].Priority);
        Assert.AreEqual(0, yields);
    }

    private static AjShareFile Share(
        long id,
        string filename,
        long size,
        string checksum,
        int priority = 1,
        long lastAsked = 0,
        long askCount = 0,
        long searchCount = 0)
        => new()
        {
            Id = id,
            Filename = filename,
            Size = size,
            Checksum = checksum,
            Priority = priority,
            LastAsked = lastAsked,
            AskCount = askCount,
            SearchCount = searchCount
        };
}
