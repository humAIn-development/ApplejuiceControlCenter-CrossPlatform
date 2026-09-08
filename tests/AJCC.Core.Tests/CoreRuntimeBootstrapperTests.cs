using System.Net;
using System.Net.Http;
using AJCC.Core.Protocol;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class CoreRuntimeBootstrapperTests
{
    [TestMethod]
    public async Task LoadAsync_BuildsStateFromFoundationCoreSequence()
    {
        RoutingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("https", "core.example.org", basePath: "/aj/"), httpClient: httpClient);
        CoreRuntimeBootstrapper bootstrapper = new(client);

        CoreBootstrapResult result = await bootstrapper.LoadAsync();

        Assert.AreEqual("bootstrap", result.State.Settings.Nick);
        Assert.AreEqual(9851, result.State.Settings.XmlPort);
        Assert.AreEqual(321L, result.State.NetworkInfo.Users);
        Assert.AreEqual("0.31.149.0", result.CoreVersion);
        Assert.AreEqual("session-xyz", result.State.SessionId);
        Assert.AreEqual(444L, result.State.LastTimestamp);
        Assert.AreEqual(1, result.State.Downloads.Count);
        Assert.AreEqual("boot.bin", result.State.Downloads[0].Filename);
        Assert.AreEqual(777L, result.State.Information.Credits);
        CollectionAssert.AreEqual(
            new[] { "/aj/xml/settings.xml", "/aj/xml/information.xml", "/aj/xml/getsession.xml", "/aj/xml/modified.xml" },
            handler.RequestPaths);

        Assert.IsNotNull(handler.ModifiedRequestUri);
        string rawQuery = handler.ModifiedRequestUri.Query;
        StringAssert.Contains(WebUtility.UrlDecode(rawQuery), "timestamp=0");
        StringAssert.Contains(WebUtility.UrlDecode(rawQuery), "filter=" + CoreRuntimeFilters.FullRuntime);
        Assert.IsFalse(rawQuery.Contains("session=", StringComparison.OrdinalIgnoreCase),
            "The initial full runtime snapshot must be sessionless, matching productive AJCC semantics.");
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = new();
        public Uri? ModifiedRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            RequestPaths.Add(path);
            if (path.EndsWith("/xml/modified.xml", StringComparison.OrdinalIgnoreCase))
                ModifiedRequestUri = request.RequestUri;

            string body = path switch
            {
                "/aj/xml/settings.xml" => "<settings><nick>bootstrap</nick><xmlport>9851</xmlport><incomingdirectory>/in</incomingdirectory><temporarydirectory>/tmp</temporarydirectory></settings>",
                "/aj/xml/information.xml" => "<root><version>0.31.149.0</version><networkinfo users='321' files='654' filesize='987' /></root>",
                "/aj/xml/getsession.xml" => "<applejuice><session id='session-xyz' /></applejuice>",
                "/aj/xml/modified.xml" => "<modified><time>444</time><download id='1' size='1000' status='0' filename='boot.bin' ready='250' /><information id='2' credits='777' /></modified>",
                _ => "<applejuice />"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
