using AJCC.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class NaturalStringComparerTests
{
    [TestMethod]
    public void Compare_OrdersDigitRunsNumerically()
    {
        NaturalStringComparer comparer = NaturalStringComparer.Instance;

        Assert.IsTrue(comparer.Compare("Film 2", "Film 10") < 0);
        Assert.IsTrue(comparer.Compare("Episode 9x20", "Episode 10x01") < 0);
    }

    [TestMethod]
    public void Compare_IsCaseAndAccentInsensitiveForTextRuns()
    {
        NaturalStringComparer comparer = NaturalStringComparer.Instance;

        Assert.AreEqual(0, comparer.Compare("film 2", "Film 2"));
    }

    [TestMethod]
    public void Compare_UsesOriginalDigitRunLengthAsStableTieBreak()
    {
        NaturalStringComparer comparer = NaturalStringComparer.Instance;

        Assert.IsTrue(comparer.Compare("File 2", "File 02") < 0);
    }
}
