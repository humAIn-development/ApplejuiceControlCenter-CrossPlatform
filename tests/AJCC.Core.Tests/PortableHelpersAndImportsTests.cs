using System.Xml.Linq;
using AJCC.Core.Helpers;
using AJCC.Core.Links;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class PortableHelpersAndImportsTests
{
    private const string Checksum = "0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void PowerDownloadFactor_NormalizesCommaInputAndRawValue()
    {
        Assert.IsTrue(PowerDownloadFactorHelper.TryNormalizeInput("2,26", out double factor));
        Assert.AreEqual(2.3, factor);
        Assert.AreEqual(13, PowerDownloadFactorHelper.ToRaw(factor));
    }

    [TestMethod]
    public void NaturalStringComparer_SortsNumericRunsNumerically()
    {
        Assert.IsTrue(NaturalStringComparer.Instance.Compare("10x02", "10x10") < 0);
    }

    [TestMethod]
    public void CoreTargetPathSanitizer_UsesExplicitSeparator()
    {
        string result = CoreTargetPathSanitizer.NormalizeRelativeTargetDirectory("shows\\season.1/episode: 2", '/', out bool changed);

        Assert.IsTrue(changed);
        Assert.AreEqual("shows/season1/episode - 2", result);
    }

    [TestMethod]
    public void XmlHelper_ReadsCaseInsensitiveAttributeName()
    {
        XElement element = XElement.Parse("<item Foo='42' enabled='yes' />");

        Assert.AreEqual(42, element.IntAttr("foo"));
        Assert.IsTrue(element.BoolAttr("ENABLED"));
    }

    [TestMethod]
    public void StartupArgumentParser_SeparatesLinksAndAjlFiles()
    {
        string link = $"ajfsp://file|example.bin|{Checksum}|123/";
        var request = AjStartupArgumentParser.Parse(new[] { $"\"{link}\"", "links.ajl", "ignored.txt" });

        Assert.IsTrue(request.HasItems);
        Assert.AreEqual(1, request.Links.Count);
        Assert.AreEqual(link, request.Links[0]);
        Assert.AreEqual(1, request.LinkListFiles.Count);
        Assert.AreEqual("links.ajl", request.LinkListFiles[0]);
    }

    [TestMethod]
    public void LinkListParser_ParsesLegacyThreeLineBlock()
    {
        string[] lines = { "Quelle: test", "-----", "1", "example.bin", Checksum, "123" };
        AjLinkListImportResult result = AjLinkListParser.ParseLines(lines);

        Assert.AreEqual("test", result.Source);
        Assert.AreEqual(1, result.CandidateCount);
        Assert.AreEqual(1, result.Links.Count);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual("example.bin", result.Links[0].FileName);
    }

    [TestMethod]
    public void ProcessLinkResult_ClassifiesKnownCoreResponses()
    {
        Assert.AreEqual(AjProcessLinkStatus.Accepted, AjProcessLinkResult.FromResponse("ok").Status);
        Assert.AreEqual(AjProcessLinkStatus.AlreadyDownloaded, AjProcessLinkResult.FromResponse("already downloaded").Status);
        Assert.AreEqual(AjProcessLinkStatus.IncorrectLink, AjProcessLinkResult.FromResponse("incorrect link").Status);
        Assert.AreEqual(AjProcessLinkStatus.Failure, AjProcessLinkResult.FromResponse("failure").Status);
    }
}
