using AJCC.Core.Links;
using AJCC.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjLinkTests
{
    private const string Checksum = "0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void BuildFileLink_WithoutSource_PreservesLegacyShape()
    {
        string link = AjfspLinkBuilder.BuildFileLink("example.bin", Checksum, 123456);

        Assert.AreEqual($"ajfsp://file|example.bin|{Checksum}|123456/", link);
    }

    [TestMethod]
    public void BuildFileLink_WithSource_AppendsLegacySourceField()
    {
        string link = AjfspLinkBuilder.BuildFileLink("example.bin", Checksum, 123456, "CoreNick");

        Assert.AreEqual($"ajfsp://file|example.bin|{Checksum}|123456|CoreNick/", link);
    }

    [TestMethod]
    public void BuildFileUri_UsesEncodedSeparators()
    {
        string link = AjfspLinkBuilder.BuildFileUri("example.bin", Checksum, 123456);

        Assert.AreEqual($"ajfsp://file%7Cexample.bin%7C{Checksum}%7C123456/", link);
    }

    [TestMethod]
    public void Parse_PlainLink_ParsesFileMetadata()
    {
        var result = AjLinkParser.Parse($"ajfsp://file|example.bin|{Checksum}|123456/");

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("example.bin", result.FileName);
        Assert.AreEqual(Checksum, result.Checksum);
        Assert.AreEqual(123456L, result.Size);
        Assert.IsFalse(result.HasSource);
    }

    [TestMethod]
    public void Parse_EncodedSeparators_RemainsBackwardCompatible()
    {
        string link = AjfspLinkBuilder.BuildFileUri("example.bin", Checksum, 123456);
        var result = AjLinkParser.Parse(link);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("example.bin", result.FileName);
        Assert.AreEqual(123456L, result.Size);
    }

    [TestMethod]
    public void Parse_TechnicalSource_ExtractsAllSourceFields()
    {
        var result = AjLinkParser.Parse($"ajfsp://file|example.bin|{Checksum}|123456|192.0.2.10:9850:core.example.org:9851/");

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.HasSource);
        Assert.IsTrue(result.HasTechnicalSource);
        Assert.AreEqual("192.0.2.10", result.SourceIp);
        Assert.AreEqual("9850", result.SourcePort);
        Assert.AreEqual("core.example.org", result.SourceHost);
        Assert.AreEqual("9851", result.SourceXmlPort);
    }

    [TestMethod]
    public void Parse_InvalidChecksum_IsRejected()
    {
        var result = AjLinkParser.Parse("ajfsp://file|example.bin|not-an-md5|123456/");

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error, "Checksum");
    }
}

[TestClass]
public sealed class AjCoreCompatibilityProfileTests
{
    [TestMethod]
    public void UnknownVersion_UsesMinimalProcessLinkShape()
    {
        var profile = AjCoreCompatibilityProfile.FromCoreVersion("unknown");

        Assert.AreEqual(AjProcessLinkStrategy.MinimalLinkOnly, profile.ProcessLinkStrategy);
        Assert.IsFalse(profile.SupportsProcessLinkSubdir);
    }

    [TestMethod]
    public void VersionBeforeThreshold_UsesMinimalProcessLinkShape()
    {
        var profile = AjCoreCompatibilityProfile.FromCoreVersion("0.31.148.9");

        Assert.AreEqual(AjProcessLinkStrategy.MinimalLinkOnly, profile.ProcessLinkStrategy);
    }

    [TestMethod]
    public void ThresholdVersion_EnablesOptionalSubdir()
    {
        var profile = AjCoreCompatibilityProfile.FromCoreVersion("AppleJuice Core 0.31.149.0");

        Assert.AreEqual(AjProcessLinkStrategy.LinkWithOptionalSubdir, profile.ProcessLinkStrategy);
        Assert.IsTrue(profile.SupportsProcessLinkSubdir);
    }
}
