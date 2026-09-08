using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class SearchExistingDownloadSemanticsTests
{
    [TestMethod]
    public void Apply_MarksMatchingChecksumTrimmedAndCaseInsensitive()
    {
        AjSearch search = Search("  ABCDEF0123456789ABCDEF0123456789  ");
        List<AjDownload> downloads =
        [
            new() { Hash = "abcdef0123456789abcdef0123456789" }
        ];

        int changed = SearchExistingDownloadSemantics.Apply([search], downloads);

        Assert.AreEqual(1, changed);
        Assert.IsTrue(search.Entries[0].IsExistingDownload);
        Assert.IsFalse(search.Entries[0].CanImportAsDownload);
        Assert.AreEqual("ist bereits in der Downloadliste", search.Entries[0].DownloadActionText);
        Assert.AreEqual("Diese Datei ist bereits in der Downloadliste.", search.Entries[0].ExistingDownloadToolTip);
    }

    [TestMethod]
    public void Apply_ClearsMarkerWhenDownloadDisappears()
    {
        AjSearch search = Search("0123456789abcdef0123456789abcdef");
        AjSearchEntry entry = search.Entries[0];

        SearchExistingDownloadSemantics.Apply(
            [search],
            [new AjDownload { Hash = "0123456789ABCDEF0123456789ABCDEF" }]);
        int changed = SearchExistingDownloadSemantics.Apply([search], []);

        Assert.AreEqual(1, changed);
        Assert.IsFalse(entry.IsExistingDownload);
        Assert.IsTrue(entry.CanImportAsDownload);
        Assert.AreEqual("Als Download übernehmen", entry.DownloadActionText);
        Assert.AreEqual(string.Empty, entry.ExistingDownloadToolTip);
    }

    [TestMethod]
    public void Apply_EmptyChecksumsNeverMatch()
    {
        AjSearch search = Search("  ");
        List<AjDownload> downloads =
        [
            new() { Hash = "" },
            new() { Hash = "   " }
        ];

        int changed = SearchExistingDownloadSemantics.Apply([search], downloads);

        Assert.AreEqual(0, changed);
        Assert.IsFalse(search.Entries[0].IsExistingDownload);
    }

    [TestMethod]
    public void IsExistingDownload_UsesChecksumOnly()
    {
        AjSearchEntry entry = new()
        {
            Filename = "anderer-name.bin",
            Size = 123,
            Checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };

        bool exists = SearchExistingDownloadSemantics.IsExistingDownload(
            entry,
            [
                new AjDownload
                {
                    Filename = "datei.bin",
                    Size = 999,
                    Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
                }
            ]);

        Assert.IsTrue(exists);
    }

    private static AjSearch Search(string checksum)
    {
        AjSearch search = new() { Id = 7, SearchText = "test", Running = false };
        search.Entries.Add(new AjSearchEntry
        {
            Id = 11,
            SearchId = 7,
            Filename = "datei.bin",
            Size = 123,
            Checksum = checksum
        });
        return search;
    }
}
