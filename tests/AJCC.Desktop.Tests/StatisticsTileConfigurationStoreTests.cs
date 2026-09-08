using System.Text.Json;
using AJCC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Desktop.Tests;

[TestClass]
public sealed class StatisticsTileConfigurationStoreTests
{
    [TestMethod]
    public void CatalogMatchesProductiveFixedOrderAndDefaultEight()
    {
        string[] expectedKeys =
        {
            "connection",
            "transfer",
            "activity",
            "session",
            "network",
            "core",
            "gui",
            "health",
            "downloads",
            "uploads",
            "sources",
            "shares",
            "networksize",
            "guiruntime",
            "guidisplay",
            "guicpu",
            "guimemory",
            "guiprocess",
            "history"
        };

        CollectionAssert.AreEqual(
            expectedKeys,
            StatisticsTileCatalog.Definitions.Select(static definition => definition.Key).ToArray());
        CollectionAssert.AreEqual(
            expectedKeys.Take(StatisticsTileCatalog.MaximumVisibleTiles).ToArray(),
            StatisticsTileCatalog.DefaultSelectedKeys);
        CollectionAssert.AreEqual(
            new[] { "connection" },
            StatisticsTileCatalog.NormalizeSelection(new[] { "connection" }));
    }

    [TestMethod]
    public void RoundtripNormalizesToFixedProductiveOrderAndMaximumEight()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "statistics-tiles.json");
        try
        {
            StatisticsTileConfigurationStore store = new(path);
            StatisticsTileConfiguration input = new(
                new[]
                {
                    "history",
                    "shares",
                    "connection",
                    "transfer",
                    "activity",
                    "session",
                    "network",
                    "core",
                    "gui",
                    "health",
                    "connection",
                    "unknown"
                });

            Assert.IsTrue(store.TrySave(input, out string errorMessage), errorMessage);

            StatisticsTileConfiguration loaded = store.Load();
            CollectionAssert.AreEqual(
                new[]
                {
                    "connection",
                    "transfer",
                    "activity",
                    "session",
                    "network",
                    "core",
                    "gui",
                    "health"
                },
                loaded.SelectedKeys);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement selectedKeys = document.RootElement.GetProperty("SelectedKeys");
            Assert.AreEqual(JsonValueKind.Array, selectedKeys.ValueKind);
            Assert.AreEqual(8, selectedKeys.GetArrayLength());
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public void EmptySelectionFallsBackToProductiveDefaultEight()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "statistics-tiles.json");
        try
        {
            StatisticsTileConfigurationStore store = new(path);
            Assert.IsTrue(
                store.TrySave(new StatisticsTileConfiguration(Array.Empty<string>()), out string errorMessage),
                errorMessage);

            StatisticsTileConfiguration loaded = store.Load();
            CollectionAssert.AreEqual(
                StatisticsTileCatalog.DefaultSelectedKeys,
                loaded.SelectedKeys);
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "AJCC-X-statistics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the assertion result.
        }
    }
}
