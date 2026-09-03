using AJCC.Core.Services;
using AJCC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Desktop.Tests;

[TestClass]
public sealed class DesktopPersistenceStoreTests
{
    [TestMethod]
    public void CoreProfileStore_SaveOverExistingFile_RoundTripsWithoutTempFile()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "core-profiles.json");
            File.WriteAllText(path, "{\"Profiles\":[],\"DefaultProfileId\":\"\"}");

            CoreProfileStore store = new(path);
            CoreProfileEntry profile = new()
            {
                Id = "core-one",
                Name = "Local Core",
                Endpoint = "http://127.0.0.1:9851/"
            };

            Assert.IsTrue(store.TrySave(new[] { profile }, profile.Id, out string error), error);
            Assert.IsFalse(File.Exists(path + ".tmp"));

            if (!OperatingSystem.IsWindows())
            {
                Assert.AreEqual(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(path));
                Assert.AreEqual(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(root));
            }

            CoreProfileStoreSnapshot loaded = store.Load();
            Assert.AreEqual(1, loaded.Profiles.Count);
            Assert.AreEqual("core-one", loaded.DefaultProfileId);
            Assert.AreEqual("Local Core", loaded.Profiles[0].Name);
            Assert.AreEqual("http://127.0.0.1:9851/", loaded.Profiles[0].Endpoint);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void CoreProfileStore_RebuildEndpointHostAndPort_PreservesSchemeAndBasePath()
    {
        const string existing = "https://Example.test:9851/CoreA/";

        string unchanged = CoreProfileStore.RebuildEndpointHostAndPort(
            existing,
            "example.test",
            9851);
        string moved = CoreProfileStore.RebuildEndpointHostAndPort(
            existing,
            "proxy.example",
            9951);

        Assert.AreEqual("https://example.test:9851/CoreA/", unchanged);
        Assert.AreEqual("https://proxy.example:9951/CoreA/", moved);
    }

    [TestMethod]
    public void CoreProfileStore_EndpointIdentity_PreservesCaseSensitiveBasePaths()
    {
        Assert.IsTrue(CoreProfileStore.EndpointEquals(
            "https://Example.test/CoreA/",
            "https://example.test/CoreA/"));
        Assert.IsFalse(CoreProfileStore.EndpointEquals(
            "https://example.test/CoreA/",
            "https://example.test/corea/"));

        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "core-profiles.json");
            CoreProfileStore store = new(path);
            CoreProfileEntry upper = new()
            {
                Id = "upper",
                Name = "Upper",
                Endpoint = "https://example.test/CoreA/"
            };
            CoreProfileEntry lower = new()
            {
                Id = "lower",
                Name = "Lower",
                Endpoint = "https://example.test/corea/"
            };

            Assert.IsTrue(store.TrySave(new[] { upper, lower }, upper.Id, out string error), error);

            CoreProfileStoreSnapshot loaded = store.Load();
            Assert.AreEqual(2, loaded.Profiles.Count);
            CollectionAssert.AreEquivalent(
                new[] { "https://example.test/CoreA/", "https://example.test/corea/" },
                loaded.Profiles.Select(profile => profile.Endpoint).ToArray());
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void EndpointSpecificStores_SeparateCaseSensitiveBasePaths()
    {
        const string upperEndpoint = "https://example.test/CoreA/";
        const string lowerEndpoint = "https://example.test/corea/";

        string root = CreateTempDirectory();
        try
        {
            string incomingPath = Path.Combine(root, "core-incoming-mappings.json");
            LocalIncomingMappingStore incomingStore = new(incomingPath);
            Assert.IsTrue(incomingStore.TrySave(upperEndpoint, "/mnt/upper", out string upperError), upperError);
            Assert.IsTrue(incomingStore.TrySave(lowerEndpoint, "/mnt/lower", out string lowerError), lowerError);
            Assert.AreEqual("/mnt/upper", incomingStore.Get(upperEndpoint));
            Assert.AreEqual("/mnt/lower", incomingStore.Get(lowerEndpoint));

            string reconnectPath = Path.Combine(root, "server-reconnect-restrictions.json");
            ServerReconnectRestrictionStore reconnectStore = new(reconnectPath);
            Assert.IsTrue(
                reconnectStore.TrySave(
                    upperEndpoint,
                    new ServerReconnectRestrictionSnapshot(DateTimeOffset.UtcNow.AddMinutes(10), true, 111),
                    out string upperReconnectError),
                upperReconnectError);
            Assert.IsTrue(
                reconnectStore.TrySave(
                    lowerEndpoint,
                    new ServerReconnectRestrictionSnapshot(DateTimeOffset.UtcNow.AddMinutes(20), true, 222),
                    out string lowerReconnectError),
                lowerReconnectError);

            Assert.IsTrue(
                reconnectStore.TryLoad(
                    upperEndpoint,
                    out ServerReconnectRestrictionSnapshot upperSnapshot,
                    out string upperLoadError),
                upperLoadError);
            Assert.IsTrue(
                reconnectStore.TryLoad(
                    lowerEndpoint,
                    out ServerReconnectRestrictionSnapshot lowerSnapshot,
                    out string lowerLoadError),
                lowerLoadError);
            Assert.AreEqual(111L, upperSnapshot.TargetServerId);
            Assert.AreEqual(222L, lowerSnapshot.TargetServerId);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void UiPreferencesStore_SavePreservesDownloadSortState()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "ui-preferences.json");
            UiPreferencesStore store = new(path);

            UiPreferences sorted = new(false)
            {
                GuiSoundsEnabled = true,
                DownloadSortColumn = "Filename",
                DownloadSortDescending = true
            };
            Assert.IsTrue(store.TrySave(sorted, out string firstError), firstError);

            UiPreferences changedElsewhere = new(true)
            {
                GuiSoundsEnabled = false
            };
            Assert.IsTrue(store.TrySave(changedElsewhere, out string secondError), secondError);
            Assert.IsFalse(File.Exists(path + ".tmp"));

            UiPreferences loaded = store.Load();
            Assert.IsTrue(loaded.SuppressCoreProfileSwitchConfirmation);
            Assert.IsFalse(loaded.GuiSoundsEnabled);
            Assert.AreEqual("Filename", loaded.DownloadSortColumn);
            Assert.IsTrue(loaded.DownloadSortDescending);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void RemainingPersistentStores_RoundTripThroughAtomicWriter()
    {
        string root = CreateTempDirectory();
        try
        {
            string queuePath = Path.Combine(root, "download-queue.json");
            DownloadQueueConfigurationStore queueStore = new(queuePath);
            DownloadQueueConfiguration queue = new(
                3,
                5,
                new Dictionary<string, string>(),
                false,
                20,
                "ListOrder");
            Assert.IsTrue(queueStore.TrySave(queue, out string queueError), queueError);
            DownloadQueueConfiguration queueLoaded = queueStore.Load();
            Assert.AreEqual(3, queueLoaded.Limit);
            Assert.AreEqual(5, queueLoaded.PreparedLimit);
            Assert.AreEqual("ListOrder", queueLoaded.OrderMode);
            Assert.IsFalse(queueLoaded.RotateSourceLess ?? true);
            Assert.AreEqual(20, queueLoaded.SourceLessTimeoutMinutes);
            AssertNoTempFile(queuePath);

            string colorsPath = Path.Combine(root, "download-status-colors.json");
            DownloadStatusColorConfigurationStore colorsStore = new(colorsPath);
            DownloadStatusColorConfiguration colors = new()
            {
                Rules = DownloadStatusColorConfiguration.CreateDefaultRules()
            };
            Assert.IsTrue(colorsStore.TrySave(colors, out string colorsError), colorsError);
            Assert.AreEqual(10, colorsStore.Load().Rules.Count);
            AssertNoTempFile(colorsPath);

            string vlcPath = Path.Combine(root, "external-vlc.json");
            ExternalVlcConfigurationStore vlcStore = new(vlcPath);
            Assert.IsTrue(
                vlcStore.TrySave(
                    new ExternalVlcConfiguration(true, "  \"/opt/vlc\"  "),
                    out string vlcError),
                vlcError);
            ExternalVlcConfiguration vlcLoaded = vlcStore.Load();
            Assert.IsTrue(vlcLoaded.Enabled);
            Assert.AreEqual("/opt/vlc", vlcLoaded.ExecutablePath);
            AssertNoTempFile(vlcPath);

            const string endpoint = "http://127.0.0.1:9851/";

            string incomingPath = Path.Combine(root, "core-incoming-mappings.json");
            LocalIncomingMappingStore incomingStore = new(incomingPath);
            Assert.IsTrue(
                incomingStore.TrySave(endpoint, "/mnt/incoming", out string incomingError),
                incomingError);
            Assert.AreEqual("/mnt/incoming", incomingStore.Get(endpoint));
            AssertNoTempFile(incomingPath);

            string reconnectPath = Path.Combine(root, "server-reconnect-restrictions.json");
            ServerReconnectRestrictionStore reconnectStore = new(reconnectPath);
            ServerReconnectRestrictionSnapshot expected =
                new(DateTimeOffset.UtcNow.AddMinutes(10), true, 123);
            Assert.IsTrue(
                reconnectStore.TrySave(endpoint, expected, out string reconnectSaveError),
                reconnectSaveError);
            Assert.IsTrue(
                reconnectStore.TryLoad(
                    endpoint,
                    out ServerReconnectRestrictionSnapshot actual,
                    out string reconnectLoadError),
                reconnectLoadError);
            Assert.IsTrue(actual.IsMarked);
            Assert.IsTrue(actual.HasExactCountdown);
            Assert.AreEqual(123L, actual.TargetServerId);
            AssertNoTempFile(reconnectPath);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static void AssertNoTempFile(string path)
        => Assert.IsFalse(File.Exists(path + ".tmp"), $"Temporary file remained for {path}.");

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "AJCC.Desktop.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the actual assertion result.
        }
    }
}
