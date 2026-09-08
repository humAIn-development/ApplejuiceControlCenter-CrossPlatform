using AJCC.Core.Helpers;
using AJCC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class DownloadStatusVisualSemanticsTests
{
    [TestMethod]
    public void GetRole_UsesProductiveDefaultStatusGroups()
    {
        Assert.AreEqual(DownloadStatusVisualRole.Completed, DownloadStatusVisualSemantics.GetRole(14));
        Assert.AreEqual(DownloadStatusVisualRole.Aborted, DownloadStatusVisualSemantics.GetRole(15));
        Assert.AreEqual(DownloadStatusVisualRole.Aborted, DownloadStatusVisualSemantics.GetRole(17));
        Assert.AreEqual(DownloadStatusVisualRole.Paused, DownloadStatusVisualSemantics.GetRole(18));
    }

    [TestMethod]
    public void GetRole_FinalizingAndOtherStatusesRemainNeutral()
    {
        int[] neutralStatuses = { 0, 1, 12, 13, 16, -1, 999 };

        foreach (int status in neutralStatuses)
            Assert.AreEqual(DownloadStatusVisualRole.Neutral, DownloadStatusVisualSemantics.GetRole(status));
    }

    [TestMethod]
    public void AjDownload_ExposesVisualFlagsForCurrentStatus()
    {
        AjDownload download = new() { Status = 14 };
        Assert.IsTrue(download.HasStatusVisualColor);
        Assert.IsTrue(download.IsCompletedStatusVisual);
        Assert.IsFalse(download.IsAbortedStatusVisual);
        Assert.IsFalse(download.IsPausedStatusVisual);

        download.Status = 18;
        Assert.IsTrue(download.HasStatusVisualColor);
        Assert.IsFalse(download.IsCompletedStatusVisual);
        Assert.IsFalse(download.IsAbortedStatusVisual);
        Assert.IsTrue(download.IsPausedStatusVisual);

        download.Status = 12;
        Assert.IsFalse(download.HasStatusVisualColor);
        Assert.AreEqual(DownloadStatusVisualRole.Neutral, download.StatusVisualRole);
    }
}
