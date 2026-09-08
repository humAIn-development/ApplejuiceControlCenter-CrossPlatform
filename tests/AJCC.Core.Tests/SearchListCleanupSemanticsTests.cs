using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class SearchListCleanupSemanticsTests
{
    [TestMethod]
    public void CanRemove_OnlyCompletedPositiveId()
    {
        Assert.IsFalse(SearchListCleanupSemantics.CanRemove(null));
        Assert.IsFalse(SearchListCleanupSemantics.CanRemove(Search(0, false)));
        Assert.IsFalse(SearchListCleanupSemantics.CanRemove(Search(1, true)));
        Assert.IsTrue(SearchListCleanupSemantics.CanRemove(Search(1, false)));
    }

    [TestMethod]
    public void TryHideAndRemove_CompletedSearchIsRemovedAndHidden()
    {
        ObservableCollection<AjSearch> searches = [Search(1, false), Search(2, false)];
        HashSet<long> hidden = [];

        bool removed = SearchListCleanupSemantics.TryHideAndRemove(searches, hidden, 1);

        Assert.IsTrue(removed);
        CollectionAssert.AreEqual(new long[] { 2 }, searches.Select(search => search.Id).ToArray());
        Assert.IsTrue(hidden.Contains(1));
    }

    [TestMethod]
    public void TryHideAndRemove_RunningSearchIsPreserved()
    {
        ObservableCollection<AjSearch> searches = [Search(1, true)];
        HashSet<long> hidden = [];

        bool removed = SearchListCleanupSemantics.TryHideAndRemove(searches, hidden, 1);

        Assert.IsFalse(removed);
        Assert.AreEqual(1, searches.Count);
        Assert.IsFalse(hidden.Contains(1));
    }

    [TestMethod]
    public void VisibleSearches_ExcludeHiddenAndKeepCollectionOrder()
    {
        List<AjSearch> searches = [Search(1, false), Search(2, false), Search(3, false)];
        HashSet<long> hidden = [2];

        IReadOnlyList<AjSearch> visible = SearchListCleanupSemantics.GetVisible(searches, hidden);

        CollectionAssert.AreEqual(new long[] { 1, 3 }, visible.Select(search => search.Id).ToArray());
        Assert.AreEqual(2, SearchListCleanupSemantics.CountVisible(searches, hidden));
        Assert.AreEqual(3L, SearchListCleanupSemantics.FindLastVisible(searches, hidden)?.Id);
    }

    [TestMethod]
    public void RestoreVisibility_ReusedCoreSearchIdBecomesVisibleAgain()
    {
        List<AjSearch> searches = [Search(4, false), Search(7, true)];
        HashSet<long> hidden = [7];

        bool restored = SearchListCleanupSemantics.RestoreVisibility(hidden, 7);

        Assert.IsTrue(restored);
        Assert.AreEqual(7L, SearchListCleanupSemantics.FindLastVisible(searches, hidden)?.Id);
    }

    private static AjSearch Search(long id, bool running)
        => new()
        {
            Id = id,
            SearchText = $"search-{id}",
            Running = running
        };
}
