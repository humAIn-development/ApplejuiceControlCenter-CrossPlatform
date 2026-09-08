using AJCC.Core.Parsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class PartListParserTests
{
    [TestMethod]
    public void ParseParts_ReadsOffsetsAndTypes()
    {
        const string xml = "<root><fileinformation filesize=\"987654321\"/><part fromposition=\"0\" type=\"1\"/><part fromposition=\"1048576\" type=\"2\"/><part fromposition=\"2097152\" type=\"3\"/></root>";

        var parts = AjXmlParser.ParseParts(xml);

        Assert.AreEqual(3, parts.Count);
        Assert.AreEqual(0L, parts[0].FromPosition);
        Assert.AreEqual(1, parts[0].Type);
        Assert.AreEqual(1048576L, parts[1].FromPosition);
        Assert.AreEqual(2, parts[1].Type);
        Assert.AreEqual(2097152L, parts[2].FromPosition);
        Assert.AreEqual(3, parts[2].Type);
    }

    [TestMethod]
    public void ParseFileSizeFromPartList_ReadsFileInformationSize()
    {
        const string xml = "<root><fileinformation filesize=\"987654321\"/><part fromposition=\"0\" type=\"1\"/></root>";

        long fileSize = AjXmlParser.ParseFileSizeFromPartList(xml);

        Assert.AreEqual(987654321L, fileSize);
    }
}
