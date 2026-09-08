using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class DownloadTransferSpeedSemanticsTests
{
    [TestMethod]
    public void CalculateDisplayedTotal_SumsPerDownloadRuntimeSpeeds()
    {
        AjDownload[] downloads =
        {
            new() { DownloadSpeed = 1_024 },
            new() { DownloadSpeed = 2_048 },
            new() { DownloadSpeed = 0 }
        };

        Assert.AreEqual(3_072L, DownloadTransferSpeedSemantics.CalculateDisplayedTotal(downloads));
    }

    [TestMethod]
    public void CalculateDisplayedTotal_EmptyListIsZero()
    {
        Assert.AreEqual(
            0L,
            DownloadTransferSpeedSemantics.CalculateDisplayedTotal(Array.Empty<AjDownload>()));
    }
}
