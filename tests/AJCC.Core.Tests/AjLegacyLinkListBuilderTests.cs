using AJCC.Core.Links;
using AJCC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjLegacyLinkListBuilderTests
{
    [TestMethod]
    public void BuildLegacyContent_PreservesProductiveAjlShape()
    {
        AjShareFile first = new()
        {
            Filename = " Example.bin ",
            Checksum = "ABCDEF0123456789ABCDEF0123456789",
            Size = 123456
        };
        AjShareFile second = new()
        {
            Filename = "Second.iso",
            Checksum = "0123456789ABCDEF0123456789ABCDEF",
            Size = 987654321
        };

        string content = AjLegacyLinkListBuilder.BuildLegacyContent(new[] { first, second });
        string nl = Environment.NewLine;
        string expected =
            "Quelle: Applejuice-Control-Center Share-Export" + nl +
            nl +
            "-----" + nl +
            "100" + nl +
            "Example.bin" + nl +
            "abcdef0123456789abcdef0123456789" + nl +
            "123456" + nl +
            "Second.iso" + nl +
            "0123456789abcdef0123456789abcdef" + nl +
            "987654321" + nl;

        Assert.AreEqual(expected, content);
    }

    [TestMethod]
    public void IsValidShareEntry_RejectsInvalidLegacyEntries()
    {
        Assert.IsFalse(AjLegacyLinkListBuilder.IsValidShareEntry(null));
        Assert.IsFalse(AjLegacyLinkListBuilder.IsValidShareEntry(new AjShareFile
        {
            Filename = "bad|name.bin",
            Checksum = "0123456789abcdef0123456789abcdef",
            Size = 1
        }));
        Assert.IsFalse(AjLegacyLinkListBuilder.IsValidShareEntry(new AjShareFile
        {
            Filename = "empty-checksum.bin",
            Checksum = " ",
            Size = 1
        }));
        Assert.IsFalse(AjLegacyLinkListBuilder.IsValidShareEntry(new AjShareFile
        {
            Filename = "zero-size.bin",
            Checksum = "0123456789abcdef0123456789abcdef",
            Size = 0
        }));
    }

    [TestMethod]
    public void BuildShareIdentityKey_MatchesProductiveDeduplicationKey()
    {
        AjShareFile share = new()
        {
            Filename = "file.bin",
            Checksum = "ABCDEF0123456789ABCDEF0123456789",
            Size = 42
        };

        Assert.AreEqual(
            "ABCDEF0123456789ABCDEF0123456789|42|file.bin",
            AjLegacyLinkListBuilder.BuildShareIdentityKey(share));
    }

    [TestMethod]
    public void PrepareShareExport_FiltersDeduplicatesAndNaturallySorts()
    {
        AjShareFile file10 = new()
        {
            Id = 10,
            Filename = "file10.bin",
            Checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Size = 10
        };
        AjShareFile file2 = new()
        {
            Id = 2,
            Filename = "file2.bin",
            Checksum = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Size = 20
        };
        AjShareFile duplicateFile2 = new()
        {
            Id = 200,
            Filename = "file2.bin",
            Checksum = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Size = 20
        };
        AjShareFile invalid = new()
        {
            Id = 3,
            Filename = "bad|name.bin",
            Checksum = "cccccccccccccccccccccccccccccccc",
            Size = 30
        };

        IReadOnlyList<AjShareFile> prepared = AjLegacyLinkListBuilder.PrepareShareExport(
            new AjShareFile?[] { file10, invalid, duplicateFile2, file2, null });

        Assert.AreEqual(2, prepared.Count);
        Assert.AreEqual("file2.bin", prepared[0].DisplayFilename);
        Assert.AreEqual("file10.bin", prepared[1].DisplayFilename);
        Assert.AreSame(duplicateFile2, prepared[0]);
    }
}
