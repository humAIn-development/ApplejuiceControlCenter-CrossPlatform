using AJCC.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class CoreEndpointTests
{
    [TestMethod]
    public void Resolve_BuildsClassicLocalCoreUrl()
    {
        CoreEndpoint endpoint = new("http", "127.0.0.1", 9851);

        Assert.AreEqual("http://127.0.0.1:9851/xml/settings.xml", endpoint.Resolve("/xml/settings.xml").ToString());
    }

    [TestMethod]
    public void Resolve_SupportsHttpsWithoutExplicitPort()
    {
        CoreEndpoint endpoint = new("https", "core.example.org");

        Assert.AreEqual("https://core.example.org/xml/information.xml", endpoint.Resolve("xml/information.xml").ToString());
    }

    [TestMethod]
    public void Resolve_PreservesReverseProxyBasePath()
    {
        CoreEndpoint endpoint = new("https", "example.org", basePath: "/applejuice/");

        Assert.AreEqual("https://example.org/applejuice/xml/modified.xml", endpoint.Resolve("/xml/modified.xml").ToString());
    }

    [TestMethod]
    public void Constructor_RejectsUnsupportedScheme()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CoreEndpoint("ftp", "example.org"));
    }
}
