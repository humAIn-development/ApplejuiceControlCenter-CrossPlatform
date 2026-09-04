using AJCC.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class CoreTargetDirectoryTests
{
    [TestMethod]
    public void EmptyTarget_MeansDirectIncoming()
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeRelative("   ", '/');

        Assert.IsTrue(result.Success);
        Assert.AreEqual(string.Empty, result.Value);
    }

    [TestMethod]
    public void MixedSeparators_AreNormalizedToCoreSeparator()
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeRelative("Serien\\Staffel 01", '/');

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Serien/Staffel 01", result.Value);
        Assert.IsTrue(result.Changed);
    }

    [TestMethod]
    [DataRow("/tmp/foo")]
    [DataRow("\\server\\share")]
    [DataRow("C:\\Incoming\\foo")]
    public void AbsolutePaths_AreRejected(string value)
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeRelative(value, '/');

        Assert.IsFalse(result.Success);
        Assert.AreEqual(string.Empty, result.Value);
    }

    [TestMethod]
    public void ParentTraversal_IsRejected()
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeRelative("Serie/../Andere", '/');

        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void CoreProblematicCharacters_AreSanitized()
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeRelative("Movies: Sci-Fi", '/');

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Movies - Sci-Fi", result.Value);
        Assert.IsTrue(result.Changed);
    }

    [TestMethod]
    [DataRow("C:\\AJMULTI\\Core1\\incoming", '\\')]
    [DataRow("/home/user/applejuice/incoming", '/')]
    public void Separator_IsDerivedFromCorePath(string path, char expected)
    {
        Assert.AreEqual(expected, CoreTargetDirectory.DetermineSeparator(path));
    }

    [TestMethod]
    public void ExistingCoreDirectory_PreservesCoreValidCharacters()
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeExistingRelative("Movies: Sci-Fi/Staffel?1", '/');

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Movies: Sci-Fi/Staffel?1", result.Value);
        Assert.IsFalse(result.Changed);
    }

    [TestMethod]
    public void ExistingCoreDirectory_NormalizesOnlySeparators()
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeExistingRelative(@"Serien\Staffel 01", '/');

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Serien/Staffel 01", result.Value);
        Assert.IsTrue(result.Changed);
    }

    [TestMethod]
    [DataRow("/home/user/applejuice/incoming/Serie")]
    [DataRow(@"C:\AJMULTI\Core1\incoming\Serie")]
    public void ExistingCoreDirectory_RejectsAbsolutePath(string value)
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeExistingRelative(value, '/');

        Assert.IsFalse(result.Success);
        Assert.AreEqual(string.Empty, result.Value);
    }

    [TestMethod]
    public void ExistingCoreDirectory_RejectsParentTraversal()
    {
        CoreTargetDirectoryNormalizationResult result = CoreTargetDirectory.NormalizeExistingRelative("Serien/../Andere", '/');

        Assert.IsFalse(result.Success);
    }}
