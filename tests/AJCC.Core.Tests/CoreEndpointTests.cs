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
    public void Parse_PreservesSchemePortAndBasePath()
    {
        CoreEndpoint endpoint = CoreEndpoint.Parse("https://core.example.org:9443/applejuice/");

        Assert.AreEqual("https", endpoint.Scheme);
        Assert.AreEqual("core.example.org", endpoint.Host);
        Assert.AreEqual(9443, endpoint.Port);
        Assert.AreEqual("/applejuice/", endpoint.BasePath);
        Assert.AreEqual("https://core.example.org:9443/applejuice/", endpoint.BaseUri.ToString());
    }

    [TestMethod]
    public void Parse_RejectsEmbeddedCredentials()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CoreEndpoint.Parse("http://user:secret@127.0.0.1:9851/"));
    }

    [TestMethod]
    public void Parse_RejectsQueryAndFragment()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CoreEndpoint.Parse("http://127.0.0.1:9851/?password=secret"));
        Assert.ThrowsExactly<ArgumentException>(() => CoreEndpoint.Parse("http://127.0.0.1:9851/#fragment"));
    }

    [TestMethod]
    public void Constructor_RejectsUnsupportedScheme()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CoreEndpoint("ftp", "example.org"));
    }
}
