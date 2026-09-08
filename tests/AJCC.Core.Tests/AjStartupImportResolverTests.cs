using AJCC.Core.Links;
using AJCC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjStartupImportResolverTests
{
    private const string Checksum = "0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void Resolve_CombinesDirectAjfspAndLegacyAjl()
    {
        string tempFile = Path.Combine(
            Path.GetTempPath(),
            "ajcc-startup-" + Guid.NewGuid().ToString("N") + ".ajl");

        try
        {
            File.WriteAllLines(
                tempFile,
                ["Quelle: test", "-----", "1", "from-list.bin", Checksum, "456"]);

            AjStartupImportRequest request = new();
            request.Links.Add($"ajfsp://file|direct.bin|{Checksum}|123/");
            request.LinkListFiles.Add(tempFile);

            AjStartupImportResolution result = AjStartupImportResolver.Resolve(request);

            Assert.AreEqual(2, result.Links.Count);
            Assert.AreEqual(0, result.Errors.Count);
            CollectionAssert.AreEquivalent(
                new[] { "direct.bin", "from-list.bin" },
                result.Links.Select(link => link.FileName).ToArray());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void Resolve_CollectsInvalidLinkAndUnreadableAjlAsErrors()
    {
        AjStartupImportRequest request = new();
        request.Links.Add("ajfsp://broken");
        request.LinkListFiles.Add(
            Path.Combine(
                Path.GetTempPath(),
                "ajcc-missing-" + Guid.NewGuid().ToString("N") + ".ajl"));

        AjStartupImportResolution result = AjStartupImportResolver.Resolve(request);

        Assert.AreEqual(0, result.Links.Count);
        Assert.AreEqual(2, result.Errors.Count);
    }
}
