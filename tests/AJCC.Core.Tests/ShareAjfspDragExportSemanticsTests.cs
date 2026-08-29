using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ShareAjfspDragExportSemanticsTests
{
    [TestMethod]
    public void BuildPlainTextLinkList_DeduplicatesAndUsesNaturalFilenameOrder()
    {
        AjShareFile[] shares =
        {
            new() { Filename = @"C:\Share\Film 10.mkv", Checksum = "hash10", Size = 10 },
            new() { Filename = @"C:\Share\Film 2.mkv", Checksum = "hash2", Size = 2 },
            new() { Filename = @"D:\Other\Film 2.mkv", Checksum = "hash2", Size = 2 },
            new() { Filename = @"C:\Share\invalid.mkv", Checksum = "", Size = 5 }
        };

        string text = ShareAjfspDragExportSemantics.BuildPlainTextLinkList(shares);
        string[] lines = text.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);

        CollectionAssert.AreEqual(
            new[]
            {
                "ajfsp://file|Film 2.mkv|hash2|2/",
                "ajfsp://file|Film 10.mkv|hash10|10/"
            },
            lines);
    }

    [TestMethod]
    public void SelectRecursiveDirectoryFiles_UsesCaseInsensitiveWindowsPaths()
    {
        AjShareFile[] shares =
        {
            new() { Filename = @"C:\Share\Root.mkv", Checksum = "a", Size = 1 },
            new() { Filename = @"c:\share\Series\Episode.mkv", Checksum = "b", Size = 2 },
            new() { Filename = @"C:\Other\NotIncluded.mkv", Checksum = "c", Size = 3 }
        };

        IReadOnlyList<AjShareFile> selected =
            ShareAjfspDragExportSemantics.SelectRecursiveDirectoryFiles(
                shares,
                @"C:\SHARE",
                '\\');

        Assert.AreEqual(2, selected.Count);
        CollectionAssert.AreEquivalent(
            new[] { "Root.mkv", "Episode.mkv" },
            selected.Select(share => share.DisplayFilename).ToArray());
    }

    [TestMethod]
    public void SelectRecursiveDirectoryFiles_UsesCaseSensitiveUnixPaths()
    {
        AjShareFile[] shares =
        {
            new() { Filename = "/share/root.mkv", Checksum = "a", Size = 1 },
            new() { Filename = "/share/series/episode.mkv", Checksum = "b", Size = 2 },
            new() { Filename = "/Share/wrong-case.mkv", Checksum = "c", Size = 3 }
        };

        IReadOnlyList<AjShareFile> selected =
            ShareAjfspDragExportSemantics.SelectRecursiveDirectoryFiles(
                shares,
                "/share",
                '/');

        Assert.AreEqual(2, selected.Count);
        CollectionAssert.AreEquivalent(
            new[] { "root.mkv", "episode.mkv" },
            selected.Select(share => share.DisplayFilename).ToArray());
    }
}
