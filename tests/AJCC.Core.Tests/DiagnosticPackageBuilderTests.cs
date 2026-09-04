using System.IO.Compression;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class DiagnosticPackageBuilderTests
{
    [TestMethod]
    public void CreateZip_ContainsExactlyProductiveDiagnosticEntries()
    {
        byte[] bytes = DiagnosticPackageBuilder.CreateZip(
            new DiagnosticPackageContent(
                "summary",
                "developer",
                "snapshot"));

        Dictionary<string, string> entries = ReadEntries(bytes);

        CollectionAssert.AreEquivalent(
            new[]
            {
                DiagnosticPackageBuilder.SummaryFileName,
                DiagnosticPackageBuilder.DeveloperLogFileName,
                DiagnosticPackageBuilder.StateSnapshotFileName,
                DiagnosticPackageBuilder.PrivacyReadmeFileName
            },
            entries.Keys.ToArray());
        Assert.AreEqual(4, entries.Count);
    }

    [TestMethod]
    public void CreateZip_SanitizesEverySuppliedDiagnosticEntry()
    {
        byte[] bytes = DiagnosticPackageBuilder.CreateZip(
            new DiagnosticPackageContent(
                "Core 192.168.178.25:9851",
                "password=secret",
                "Filename=private-film.mkv"));

        Dictionary<string, string> entries = ReadEntries(bytes);

        Assert.AreEqual(
            "Core [MASKED_IP]",
            entries[DiagnosticPackageBuilder.SummaryFileName]);
        Assert.AreEqual(
            "password=******",
            entries[DiagnosticPackageBuilder.DeveloperLogFileName]);
        Assert.AreEqual(
            "Filename=[MASKED]",
            entries[DiagnosticPackageBuilder.StateSnapshotFileName]);

        string combined = string.Join("\n", entries.Values);
        Assert.IsFalse(combined.Contains("192.168.178.25", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(combined.Contains("private-film.mkv", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void BuildPrivacyReadme_StatesConservativePrivacyContract()
    {
        string text = DiagnosticPackageBuilder.BuildPrivacyReadme();

        StringAssert.Contains(text, "absichtlich konservativ anonymisiert");
        StringAssert.Contains(text, "keine Raw-Core-Payloads");
        StringAssert.Contains(text, "Datenschutz hat hier Vorrang");
    }

    private static Dictionary<string, string> ReadEntries(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);

        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using Stream entryStream = entry.Open();
                using StreamReader reader = new(entryStream);
                return reader.ReadToEnd();
            },
            StringComparer.Ordinal);
    }
}
