using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class SearchResultFilterSemanticsTests
{
    [TestMethod]
    public void Matches_EmptyFilterShowsEntry()
    {
        AjSearchEntry entry = CreateEntry();

        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, string.Empty));
        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, "   "));
    }

    [TestMethod]
    public void Matches_ProductiveSearchFieldsCaseInsensitively()
    {
        AjSearchEntry entry = CreateEntry();

        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, "episode 12"));
        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, "ABCDEF"));
        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, "alice"));
        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, entry.SizeText));
        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, "4711"));
        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, "77"));
        Assert.IsTrue(SearchResultFilterSemantics.Matches(entry, "23"));
    }

    [TestMethod]
    public void Matches_UnknownTextDoesNotMatch()
    {
        AjSearchEntry entry = CreateEntry();

        Assert.IsFalse(SearchResultFilterSemantics.Matches(entry, "definitely-not-present"));
    }

    [TestMethod]
    public void Matches_NullEntryDoesNotMatch()
        => Assert.IsFalse(SearchResultFilterSemantics.Matches(null, "anything"));

    private static AjSearchEntry CreateEntry()
        => new()
        {
            Id = 4711,
            SearchId = 77,
            Filename = "Series Episode 12.mkv",
            Checksum = "abcdef0123456789abcdef0123456789",
            SourceText = "Alice",
            Size = 734003200,
            FilenameUsers = 23
        };
}
