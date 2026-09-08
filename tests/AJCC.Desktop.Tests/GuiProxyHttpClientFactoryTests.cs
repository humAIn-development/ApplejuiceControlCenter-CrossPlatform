using System.Net;
using AJCC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Desktop.Tests;

[TestClass]
public sealed class GuiProxyHttpClientFactoryTests
{
    [TestMethod]
    public void DisabledProxy_BypassesProxyHandling()
    {
        GuiProxyConfiguration configuration = new(
            false,
            GuiProxyConfiguration.PublicMode,
            "proxy.example.test",
            8080,
            string.Empty);

        using HttpClientHandler handler =
            GuiProxyHttpClientFactory.CreateHandler(configuration);

        Assert.IsFalse(handler.UseProxy);
    }

    [TestMethod]
    public void PublicProxy_UsesConfiguredEndpointWithoutCredentials()
    {
        GuiProxyConfiguration configuration = new(
            true,
            GuiProxyConfiguration.PublicMode,
            "proxy.example.test",
            8080,
            "ignored-user");

        using HttpClientHandler handler =
            GuiProxyHttpClientFactory.CreateHandler(configuration);

        Assert.IsTrue(handler.UseProxy);
        Assert.IsNotNull(handler.Proxy);

        Uri destination = new("http://destination.example/");
        Uri proxyUri = handler.Proxy.GetProxy(destination);
        Assert.AreEqual("http://proxy.example.test:8080/", proxyUri.AbsoluteUri);
        Assert.IsNull(handler.Proxy.Credentials);
    }

    [TestMethod]
    public void PrivateProxy_UsesSessionOnlyCredentials()
    {
        GuiProxyConfiguration configuration = new(
            true,
            GuiProxyConfiguration.PrivateMode,
            "proxy.example.test",
            3128,
            "proxy-user");

        using HttpClientHandler handler =
            GuiProxyHttpClientFactory.CreateHandler(configuration, "session-secret");

        Assert.IsTrue(handler.UseProxy);
        Assert.IsNotNull(handler.Proxy);

        Uri destination = new("https://destination.example/");
        Uri proxyUri = handler.Proxy.GetProxy(destination);
        NetworkCredential? credential =
            handler.Proxy.Credentials?.GetCredential(proxyUri, "Basic");

        Assert.IsNotNull(credential);
        Assert.AreEqual("proxy-user", credential.UserName);
        Assert.AreEqual("session-secret", credential.Password);
    }

    [TestMethod]
    public void SessionCredentials_RoundTripAndClearWithoutPersistence()
    {
        GuiProxySessionCredentials.ClearPassword();
        Assert.AreEqual(string.Empty, GuiProxySessionCredentials.Password);

        GuiProxySessionCredentials.SetPassword("temporary-secret");
        Assert.AreEqual("temporary-secret", GuiProxySessionCredentials.Password);

        GuiProxySessionCredentials.ClearPassword();
        Assert.AreEqual(string.Empty, GuiProxySessionCredentials.Password);
    }
}
