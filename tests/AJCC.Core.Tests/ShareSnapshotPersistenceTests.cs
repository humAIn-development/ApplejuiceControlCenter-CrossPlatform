using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ShareSnapshotPersistenceTests
{
    [TestMethod]
    public async Task SaveAndLoad_RoundTripsGzipPerCoreEndpoint()
    {
        string root = CreateTemporaryRoot();
        try
        {
            ShareSnapshotDocument snapshot = ShareSnapshotService.CreateSnapshot(
                "CoreHost",
                9851,
                new[]
                {
                    new ShareSnapshotSourceFile("/share/Film 2.mkv", 20),
                    new ShareSnapshotSourceFile("/share/Film 10.mkv", 100)
                },
                new[]
                {
                    new ShareSnapshotSourceRoot("/share", "subdirectory")
                });

            await ShareSnapshotService.SaveAsync(snapshot, root);

            string storagePath =
                ShareSnapshotService.GetStoragePath("corehost", 9851, root);
            Assert.IsTrue(File.Exists(storagePath));
            if (!OperatingSystem.IsWindows())
            {
                Assert.AreEqual(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(storagePath));
            }

            byte[] bytes = await File.ReadAllBytesAsync(storagePath);
            Assert.IsTrue(bytes.Length >= 2);
            Assert.AreEqual((byte)0x1f, bytes[0]);
            Assert.AreEqual((byte)0x8b, bytes[1]);

            ShareSnapshotLoadResult loaded =
                await ShareSnapshotService.LoadAsync("COREHOST", 9851, root);

            Assert.IsFalse(loaded.HasError);
            Assert.IsNotNull(loaded.Snapshot);
            Assert.AreEqual(storagePath, loaded.StoragePath);
            Assert.AreEqual(2, loaded.Snapshot.Files.Count);
            Assert.AreEqual("/share/Film 2.mkv", loaded.Snapshot.Files[0].Path);
            Assert.AreEqual(1, loaded.Snapshot.Roots.Count);
            Assert.AreEqual("subdirectory", loaded.Snapshot.Roots[0].ShareMode);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [TestMethod]
    public void GetStoragePath_NormalizesHostAndSeparatesPorts()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string first = ShareSnapshotService.GetStoragePath(" CoreHost ", 9851, root);
            string same = ShareSnapshotService.GetStoragePath("corehost", 9851, root);
            string otherPort = ShareSnapshotService.GetStoragePath("corehost", 9852, root);

            Assert.AreEqual(first, same);
            Assert.AreNotEqual(first, otherPort);
            Assert.AreEqual(Path.GetFullPath(root), Path.GetDirectoryName(first));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [TestMethod]
    public async Task LoadAsync_MissingBaselineReturnsEmptyResultWithoutError()
    {
        string root = CreateTemporaryRoot();
        try
        {
            ShareSnapshotLoadResult result =
                await ShareSnapshotService.LoadAsync("core", 9851, root);

            Assert.IsFalse(result.HasError);
            Assert.IsNull(result.Snapshot);
            Assert.AreEqual(
                ShareSnapshotService.GetStoragePath("core", 9851, root),
                result.StoragePath);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [TestMethod]
    public async Task LoadAsync_CorruptBaselineReturnsErrorWithoutThrowing()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string storagePath =
                ShareSnapshotService.GetStoragePath("core", 9851, root);
            Directory.CreateDirectory(Path.GetDirectoryName(storagePath)!);
            await File.WriteAllTextAsync(storagePath, "not-a-gzip-snapshot");

            ShareSnapshotLoadResult result =
                await ShareSnapshotService.LoadAsync("core", 9851, root);

            Assert.IsTrue(result.HasError);
            Assert.IsNull(result.Snapshot);
            Assert.AreEqual(storagePath, result.StoragePath);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "AJCC-X-ShareSnapshotTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup must not mask the assertion result.
        }
    }
}
