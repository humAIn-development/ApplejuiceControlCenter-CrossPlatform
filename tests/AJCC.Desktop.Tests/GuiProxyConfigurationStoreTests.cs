using AJCC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Desktop.Tests;

[TestClass]
public sealed class GuiProxyConfigurationStoreTests
{
    [TestMethod]
    public void PrivateProxy_RoundTripsPersistentFieldsWithoutPasswordField()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "proxy-settings.json");
            GuiProxyConfigurationStore store = new(path);
            GuiProxyConfiguration configuration = new(
                true,
                GuiProxyConfiguration.PrivateMode,
                "  proxy.example.test  ",
                8080,
                "  proxy-user  ");

            Assert.IsTrue(store.TrySave(configuration, out string error), error);
            Assert.IsFalse(File.Exists(path + ".tmp"));

            GuiProxyConfiguration loaded = store.Load();
            Assert.IsTrue(loaded.Enabled);
            Assert.AreEqual(GuiProxyConfiguration.PrivateMode, loaded.Mode);
            Assert.AreEqual("proxy.example.test", loaded.Host);
            Assert.AreEqual(8080, loaded.Port);
            Assert.AreEqual("proxy-user", loaded.UserName);

            string json = File.ReadAllText(path);
            Assert.IsFalse(json.Contains("password", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void PublicProxy_DropsUsernameAndValidationMatchesPersistentProductiveFields()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "proxy-settings.json");
            GuiProxyConfigurationStore store = new(path);

            Assert.IsTrue(
                store.TrySave(
                    new GuiProxyConfiguration(
                        true,
                        GuiProxyConfiguration.PublicMode,
                        "proxy.example.test",
                        3128,
                        "must-not-persist"),
                    out string saveError),
                saveError);
            Assert.AreEqual(string.Empty, store.Load().UserName);

            Assert.IsFalse(
                store.TrySave(
                    new GuiProxyConfiguration(true, GuiProxyConfiguration.PublicMode, string.Empty, 3128, string.Empty),
                    out _));
            Assert.IsFalse(
                store.TrySave(
                    new GuiProxyConfiguration(true, GuiProxyConfiguration.PublicMode, "proxy.example.test", 70000, string.Empty),
                    out _));
            Assert.IsFalse(
                store.TrySave(
                    new GuiProxyConfiguration(true, GuiProxyConfiguration.PrivateMode, "proxy.example.test", 3128, string.Empty),
                    out _));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

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
