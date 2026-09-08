using System.Collections.Generic;
using System.Linq;
using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class DownloadQueuePlanningSemanticsTests
{
    [TestMethod]
    public void BuildPlan_DisabledQueueProducesNoActions()
    {
        List<AjDownload> downloads = [Download(1, status: 0, ready: 500, activeSources: 1, sources: 3)];

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(downloads, configuredLimit: 0);

        Assert.AreEqual(0, plan.EligibleCount);
        Assert.AreEqual(0, plan.ShouldRunIds.Count);
        Assert.AreEqual(0, plan.ResumeIds.Count);
        Assert.AreEqual(0, plan.PauseIds.Count);
    }

    [TestMethod]
    public void BuildPlan_UsesProductiveAutomaticOrdering()
    {
        List<AjDownload> downloads =
        [
            Download(1, status: 18, ready: 900, activeSources: 0, sources: 1),
            Download(2, status: 0, ready: 900, activeSources: 1, sources: 1),
            Download(3, status: 18, ready: 900, activeSources: 1, sources: 5),
            Download(4, status: 0, ready: 800, activeSources: 4, sources: 9)
        ];

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(downloads, configuredLimit: 3);

        CollectionAssert.AreEqual(new long[] { 3, 2, 1 }, plan.ShouldRunIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 3, 1 }, plan.ResumeIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 4 }, plan.PauseIds.ToArray());
    }

    [TestMethod]
    public void BuildPlan_ExcludesTerminalDownloads()
    {
        List<AjDownload> downloads =
        [
            Download(1, status: 14, ready: 1000, activeSources: 5, sources: 5),
            Download(2, status: 15, ready: 950, activeSources: 5, sources: 5),
            Download(3, status: 17, ready: 900, activeSources: 5, sources: 5),
            Download(4, status: 18, ready: 500, activeSources: 0, sources: 2),
            Download(5, status: 0, ready: 400, activeSources: 1, sources: 3)
        ];

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(downloads, configuredLimit: 1);

        Assert.AreEqual(2, plan.EligibleCount);
        CollectionAssert.AreEqual(new long[] { 4 }, plan.ShouldRunIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 4 }, plan.ResumeIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 5 }, plan.PauseIds.ToArray());
    }

    [TestMethod]
    public void BuildPlan_DefaultCommandCapLimitsResumeAndPauseSeparately()
    {
        List<AjDownload> downloads = [];
        for (int index = 0; index < 6; index++)
        {
            downloads.Add(Download(
                id: index + 1,
                status: 18,
                ready: 1000 - index * 10,
                activeSources: 2,
                sources: 4));
        }

        for (int index = 0; index < 6; index++)
        {
            downloads.Add(Download(
                id: index + 101,
                status: 0,
                ready: 500 - index * 10,
                activeSources: 1,
                sources: 3));
        }

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(downloads, configuredLimit: 6);

        Assert.AreEqual(5, plan.ResumeIds.Count);
        Assert.AreEqual(5, plan.PauseIds.Count);
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5 }, plan.ResumeIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 101, 102, 103, 104, 105 }, plan.PauseIds.ToArray());
    }

    [TestMethod]
    public void BuildPlan_ClampsPositiveLimitToProductiveMaximum()
    {
        List<AjDownload> downloads = Enumerable.Range(1, 105)
            .Select(index => Download(
                id: index,
                status: 0,
                ready: 1000 - index,
                activeSources: 1,
                sources: 1))
            .ToList();

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(
            downloads,
            configuredLimit: 500,
            commandCap: int.MaxValue);

        Assert.AreEqual(105, plan.EligibleCount);
        Assert.AreEqual(DownloadQueuePlanningSemantics.MaximumLimit, plan.ShouldRunIds.Count);
        Assert.AreEqual(5, plan.PauseIds.Count);
    }

    [TestMethod]
    public void BuildPlan_UsesPriorityGroupsBeforeAutomaticOrdering()
    {
        AjDownload high = Download(1, status: 18, ready: 100, activeSources: 0, sources: 1, hash: "AA");
        AjDownload normal = Download(2, status: 18, ready: 700, activeSources: 2, sources: 4, shareId: 22);
        AjDownload low = Download(3, status: 0, ready: 950, activeSources: 5, sources: 8);
        Dictionary<string, string> priorities = new(StringComparer.OrdinalIgnoreCase)
        {
            [DownloadQueuePlanningSemantics.GetPriorityKey(high)] = DownloadQueuePlanningSemantics.PriorityHigh,
            [DownloadQueuePlanningSemantics.GetPriorityKey(low)] = DownloadQueuePlanningSemantics.PriorityLow
        };

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(
            [high, normal, low],
            configuredLimit: 2,
            commandCap: int.MaxValue,
            priorities: priorities);

        CollectionAssert.AreEqual(new long[] { 1, 2 }, plan.ShouldRunIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 1, 2 }, plan.ResumeIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 3 }, plan.PauseIds.ToArray());
    }

    [TestMethod]
    public void BuildPlan_UsesListOrderWithinSamePriorityGroup()
    {
        AjDownload firstInList = Download(
            1,
            status: 18,
            ready: 100,
            activeSources: 0,
            sources: 1);
        AjDownload automaticFavorite = Download(
            2,
            status: 0,
            ready: 950,
            activeSources: 5,
            sources: 8);
        AjDownload secondInList = Download(
            3,
            status: 18,
            ready: 200,
            activeSources: 1,
            sources: 2);

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(
            [automaticFavorite, secondInList, firstInList],
            configuredLimit: 2,
            commandCap: int.MaxValue,
            listOrder: [firstInList.Id, secondInList.Id, automaticFavorite.Id]);

        CollectionAssert.AreEqual(new long[] { 1, 3 }, plan.ShouldRunIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 1, 3 }, plan.ResumeIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 2 }, plan.PauseIds.ToArray());
    }

    [TestMethod]
    public void BuildPlan_PriorityGroupsTakePrecedenceOverListOrder()
    {
        AjDownload high = Download(1, status: 18, ready: 100, activeSources: 0, sources: 1, hash: "HIGH");
        AjDownload normal = Download(2, status: 18, ready: 500, activeSources: 2, sources: 4);
        AjDownload low = Download(3, status: 0, ready: 950, activeSources: 5, sources: 8, hash: "LOW");
        Dictionary<string, string> priorities = new(StringComparer.OrdinalIgnoreCase)
        {
            [DownloadQueuePlanningSemantics.GetPriorityKey(high)] =
                DownloadQueuePlanningSemantics.PriorityHigh,
            [DownloadQueuePlanningSemantics.GetPriorityKey(low)] =
                DownloadQueuePlanningSemantics.PriorityLow
        };

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(
            [low, normal, high],
            configuredLimit: 2,
            commandCap: int.MaxValue,
            priorities: priorities,
            listOrder: [low.Id, normal.Id, high.Id]);

        CollectionAssert.AreEqual(new long[] { 1, 2 }, plan.ShouldRunIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 1, 2 }, plan.ResumeIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 3 }, plan.PauseIds.ToArray());
    }

    [TestMethod]
    public void BuildPlan_ExcludedActiveDownloadConsumesCapacityButReceivesNoCommands()
    {
        AjDownload excluded = Download(1, status: 0, ready: 950, activeSources: 4, sources: 8, hash: "EX");
        AjDownload preferred = Download(2, status: 18, ready: 800, activeSources: 2, sources: 5);
        AjDownload waiting = Download(3, status: 0, ready: 700, activeSources: 1, sources: 3);
        Dictionary<string, string> priorities = new(StringComparer.OrdinalIgnoreCase)
        {
            [DownloadQueuePlanningSemantics.GetPriorityKey(excluded)] =
                DownloadQueuePlanningSemantics.PriorityExcluded
        };

        DownloadQueuePlan plan = DownloadQueuePlanningSemantics.BuildPlan(
            [excluded, preferred, waiting],
            configuredLimit: 2,
            commandCap: int.MaxValue,
            priorities: priorities);

        Assert.AreEqual(3, plan.EligibleCount);
        CollectionAssert.AreEqual(new long[] { 2 }, plan.ShouldRunIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 2 }, plan.ResumeIds.ToArray());
        CollectionAssert.AreEqual(new long[] { 3 }, plan.PauseIds.ToArray());
        CollectionAssert.DoesNotContain(plan.ResumeIds.ToList(), excluded.Id);
        CollectionAssert.DoesNotContain(plan.PauseIds.ToList(), excluded.Id);
    }

    [TestMethod]
    public void PriorityKey_PrefersHashThenShareIdThenDownloadId()
    {
        Assert.AreEqual(
            "hash:abcdef",
            DownloadQueuePlanningSemantics.GetPriorityKey(
                Download(7, 0, 0, 0, 0, hash: " AbCdEf ", shareId: 55)));
        Assert.AreEqual(
            "share:55",
            DownloadQueuePlanningSemantics.GetPriorityKey(
                Download(7, 0, 0, 0, 0, shareId: 55)));
        Assert.AreEqual(
            "id:7",
            DownloadQueuePlanningSemantics.GetPriorityKey(
                Download(7, 0, 0, 0, 0)));
    }

    [TestMethod]
    public void StatusHelpers_FollowProductiveQueueStatusRules()
    {
        Assert.IsTrue(DownloadQueuePlanningSemantics.IsTerminal(Download(1, 14, 0, 0, 0)));
        Assert.IsTrue(DownloadQueuePlanningSemantics.IsTerminal(Download(2, 15, 0, 0, 0)));
        Assert.IsTrue(DownloadQueuePlanningSemantics.IsTerminal(Download(3, 17, 0, 0, 0)));
        Assert.IsFalse(DownloadQueuePlanningSemantics.IsTerminal(Download(4, 18, 0, 0, 0)));
        Assert.IsTrue(DownloadQueuePlanningSemantics.IsPaused(Download(5, 18, 0, 0, 0)));
        Assert.IsFalse(DownloadQueuePlanningSemantics.IsPaused(Download(6, 0, 0, 0, 0)));
    }

    private static AjDownload Download(
        long id,
        int status,
        long ready,
        int activeSources,
        int sources,
        string hash = "",
        long shareId = 0)
        => new()
        {
            Id = id,
            ShareId = shareId,
            Hash = hash,
            Status = status,
            Size = 1000,
            Ready = ready,
            ActiveSourceCount = activeSources,
            SourceCount = sources
        };
}
