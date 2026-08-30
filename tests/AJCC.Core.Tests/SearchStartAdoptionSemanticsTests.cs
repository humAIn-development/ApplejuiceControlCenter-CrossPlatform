using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class SearchStartAdoptionSemanticsTests
{
    [TestMethod]
    public void FindCandidate_NewCoreSearchId_WinsBeforeExistingFallback()
    {
        AjSearch[] searches =
        {
            new() { Id = 7, SearchText = "linux", Running = true },
            new() { Id = 9, SearchText = "another client", Running = false }
        };

        AjSearch? candidate = SearchStartAdoptionSemantics.FindCandidate(
            searches,
            "linux",
            previousMaxSearchId: 7,
            allowExistingFallback: true);

        Assert.IsNotNull(candidate);
        Assert.AreEqual(9L, candidate.Id);
    }

    [TestMethod]
    public void FindCandidate_ExistingSearch_IsNotUsedBeforeFallbackIsAllowed()
    {
        AjSearch[] searches =
        {
            new() { Id = 7, SearchText = " linux ", Running = true }
        };

        AjSearch? candidate = SearchStartAdoptionSemantics.FindCandidate(
            searches,
            "linux",
            previousMaxSearchId: 7,
            allowExistingFallback: false);

        Assert.IsNull(candidate);
    }

    [TestMethod]
    public void FindCandidate_ExistingFallback_MatchesTrimmedTextIgnoringCase()
    {
        AjSearch[] searches =
        {
            new() { Id = 5, SearchText = "other", Running = true },
            new() { Id = 7, SearchText = "  LiNuX ", Running = false }
        };

        AjSearch? candidate = SearchStartAdoptionSemantics.FindCandidate(
            searches,
            " linux ",
            previousMaxSearchId: 7,
            allowExistingFallback: true);

        Assert.IsNotNull(candidate);
        Assert.AreEqual(7L, candidate.Id);
    }

    [TestMethod]
    public void FindCandidate_ExistingFallback_PrefersRunningThenHighestId()
    {
        AjSearch[] searches =
        {
            new() { Id = 4, SearchText = "linux", Running = true },
            new() { Id = 6, SearchText = "linux", Running = false },
            new() { Id = 5, SearchText = "linux", Running = true }
        };

        AjSearch? candidate = SearchStartAdoptionSemantics.FindCandidate(
            searches,
            "linux",
            previousMaxSearchId: 6,
            allowExistingFallback: true);

        Assert.IsNotNull(candidate);
        Assert.AreEqual(5L, candidate.Id);
    }

    [TestMethod]
    public void FindCandidate_ExistingFallback_DoesNotAdoptDifferentSearchText()
    {
        AjSearch[] searches =
        {
            new() { Id = 7, SearchText = "windows", Running = true }
        };

        AjSearch? candidate = SearchStartAdoptionSemantics.FindCandidate(
            searches,
            "linux",
            previousMaxSearchId: 7,
            allowExistingFallback: true);

        Assert.IsNull(candidate);
    }

    [TestMethod]
    public void FindCandidate_BlankText_DoesNotAdoptExistingFallback()
    {
        AjSearch[] searches =
        {
            new() { Id = 7, SearchText = "linux", Running = true }
        };

        AjSearch? candidate = SearchStartAdoptionSemantics.FindCandidate(
            searches,
            " ",
            previousMaxSearchId: 7,
            allowExistingFallback: true);

        Assert.IsNull(candidate);
    }
}
