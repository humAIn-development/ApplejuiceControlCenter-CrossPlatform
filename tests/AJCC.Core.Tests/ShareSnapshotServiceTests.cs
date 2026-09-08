using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ShareSnapshotServiceTests
{
    [TestMethod]
    public void CreateSnapshot_NormalizesAndDeduplicatesWindowsPaths()
    {
        ShareSnapshotDocument snapshot = ShareSnapshotService.CreateSnapshot(
            "CoreHost",
            9851,
            new[]
            {
                new ShareSnapshotSourceFile(@"C:\Share\Film 10.mkv", 100),
                new ShareSnapshotSourceFile(@"c:\share\film 10.mkv", 200),
                new ShareSnapshotSourceFile(@"C:\Share\Film 2.mkv", -5)
            },
            new[]
            {
                new ShareSnapshotSourceRoot(@"C:\Share", "subdirectory"),
                new ShareSnapshotSourceRoot(@"c:\share", "directory")
            });

        Assert.AreEqual(2, snapshot.Files.Count);
        Assert.AreEqual(@"C:\Share\Film 2.mkv", snapshot.Files[0].Path);
        Assert.AreEqual(0L, snapshot.Files[0].Size);
        Assert.AreEqual(1, snapshot.Roots.Count);
        Assert.AreEqual("subdirectory", snapshot.Roots[0].ShareMode);
    }

    [TestMethod]
    public void Compare_FindsAddedRemovedAndChangedFiles()
    {
        ShareSnapshotDocument baseline = ShareSnapshotService.CreateSnapshot(
            "core",
            9851,
            new[]
            {
                new ShareSnapshotSourceFile("/share/keep.bin", 10),
                new ShareSnapshotSourceFile("/share/remove.bin", 20),
                new ShareSnapshotSourceFile("/share/change.bin", 30)
            },
            Array.Empty<ShareSnapshotSourceRoot>());

        ShareSnapshotDocument current = ShareSnapshotService.CreateSnapshot(
            "core",
            9851,
            new[]
            {
                new ShareSnapshotSourceFile("/share/keep.bin", 10),
                new ShareSnapshotSourceFile("/share/add.bin", 40),
                new ShareSnapshotSourceFile("/share/change.bin", 35)
            },
            Array.Empty<ShareSnapshotSourceRoot>());

        ShareSnapshotComparisonReport report = ShareSnapshotService.Compare(current, baseline);

        Assert.AreEqual(1, report.AddedFiles.Count);
        Assert.AreEqual("/share/add.bin", report.AddedFiles[0].Path);
        Assert.AreEqual(1, report.RemovedFiles.Count);
        Assert.AreEqual("/share/remove.bin", report.RemovedFiles[0].Path);
        Assert.AreEqual(1, report.ChangedFiles.Count);
        Assert.AreEqual("/share/change.bin", report.ChangedFiles[0].Path);
        Assert.AreEqual(3, report.TotalChangeCount);
    }

    [TestMethod]
    public void Compare_FlagsExpandedShareRoot()
    {
        ShareSnapshotDocument baseline = ShareSnapshotService.CreateSnapshot(
            "core",
            9851,
            Array.Empty<ShareSnapshotSourceFile>(),
            new[]
            {
                new ShareSnapshotSourceRoot("/share/series", "directory")
            });

        ShareSnapshotDocument current = ShareSnapshotService.CreateSnapshot(
            "core",
            9851,
            Array.Empty<ShareSnapshotSourceFile>(),
            new[]
            {
                new ShareSnapshotSourceRoot("/share/series", "subdirectory")
            });

        ShareSnapshotComparisonReport report = ShareSnapshotService.Compare(current, baseline);

        Assert.AreEqual(1, report.RootChanges.Count);
        Assert.AreEqual(ShareSnapshotRootChangeKind.ModeChanged, report.RootChanges[0].Kind);
        Assert.IsTrue(report.Notices.Any(notice =>
            notice.Severity == ShareSnapshotNoticeSeverity.Review
            && notice.Category == "Freigabemodus"));
    }

    [TestMethod]
    public void Compare_KeepsUnixPathComparisonCaseSensitive()
    {
        ShareSnapshotDocument baseline = ShareSnapshotService.CreateSnapshot(
            "core",
            9851,
            new[]
            {
                new ShareSnapshotSourceFile("/share/File.bin", 10)
            },
            Array.Empty<ShareSnapshotSourceRoot>());

        ShareSnapshotDocument current = ShareSnapshotService.CreateSnapshot(
            "core",
            9851,
            new[]
            {
                new ShareSnapshotSourceFile("/share/file.bin", 10)
            },
            Array.Empty<ShareSnapshotSourceRoot>());

        ShareSnapshotComparisonReport report = ShareSnapshotService.Compare(current, baseline);

        Assert.AreEqual(1, report.AddedFiles.Count);
        Assert.AreEqual(1, report.RemovedFiles.Count);
    }

    [TestMethod]
    public void Compare_WithoutBaselineReturnsInformationalNotice()
    {
        ShareSnapshotDocument current = ShareSnapshotService.CreateSnapshot(
            "core",
            9851,
            Array.Empty<ShareSnapshotSourceFile>(),
            Array.Empty<ShareSnapshotSourceRoot>());

        ShareSnapshotComparisonReport report = ShareSnapshotService.Compare(current, null, "snapshot.bin");

        Assert.IsFalse(report.HasBaseline);
        Assert.AreEqual("snapshot.bin", report.StoragePath);
        Assert.AreEqual(1, report.Notices.Count);
        Assert.AreEqual(ShareSnapshotNoticeSeverity.Information, report.Notices[0].Severity);
    }
}
