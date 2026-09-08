using System.Net;
using System.Net.Http;
using AJCC.Core.Models;
using AJCC.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AppleJuiceCoreClientTests
{
    [TestMethod]
    public async Task GetSettings_UsesHttpsBasePathAndHashedPassword()
    {
        RecordingHandler handler = new("<settings />");
        using HttpClient httpClient = new(handler);
        CoreEndpoint endpoint = new("https", "example.org", basePath: "/applejuice/");
        AppleJuiceCoreClient client = new(endpoint, "secret", httpClient);

        await client.GetSettingsXmlAsync();

        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.AreEqual("https", handler.LastRequestUri.Scheme);
        Assert.AreEqual("example.org", handler.LastRequestUri.Host);
        Assert.AreEqual("/applejuice/xml/settings.xml", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "password=5ebe2294ecd0e0f08eab7690d2a6ee69");
        Assert.IsFalse(query.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetShare_UsesShareXmlEndpoint()
    {
        RecordingHandler handler = new("<shares />");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.GetShareXmlAsync();

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/xml/share.xml", handler.LastRequestUri.AbsolutePath);
    }

    [TestMethod]
    public async Task GetModified_SendsTimestampSessionAndFilter()
    {
        RecordingHandler handler = new("<modified><time>43</time></modified>");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.GetModifiedXmlAsync(42, "session-1", "ids;down;server");

        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.AreEqual("/xml/modified.xml", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "timestamp=42");
        StringAssert.Contains(query, "session=session-1");
        StringAssert.Contains(query, "filter=ids;down;server");
    }

    [TestMethod]
    public async Task GetDownloadPartList_UsesLowercaseIdParameter()
    {
        RecordingHandler handler = new("<root />");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.GetDownloadPartListXmlAsync(1234);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/xml/downloadpartlist.xml", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=1234");
        Assert.IsFalse(query.Contains("ID=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetUserPartList_UsesLowercaseIdParameter()
    {
        RecordingHandler handler = new("<root />");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.GetUserPartListXmlAsync(5678);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/xml/userpartlist.xml", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=5678");
        Assert.IsFalse(query.Contains("ID=", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Search_UsesPostAndJavaGuiStyleEncoding()
    {
        RecordingHandler handler = new(string.Empty);
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("https", "example.org", basePath: "/applejuice/"), "secret", httpClient);

        string result = await client.SearchAsync("  two words  ");

        Assert.AreEqual("OK", result);
        Assert.AreEqual(HttpMethod.Post, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/applejuice/function/search", handler.LastRequestUri.AbsolutePath);
        string rawUrl = handler.LastRequestUri.OriginalString;
        StringAssert.Contains(rawUrl, "search=two%20words");
        Assert.IsFalse(rawUrl.Contains("search=two+words", StringComparison.Ordinal));
        Assert.IsFalse(rawUrl.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CancelSearch_UsesGetWithSearchId()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.CancelSearchAsync(17);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/cancelsearch", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=17");
    }

    [TestMethod]
    public async Task PauseDownload_UsesHistoricalUppercaseIdParameter()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.PauseDownloadAsync(1234);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/pausedownload", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "ID=1234");
    }

    [TestMethod]
    public async Task ResumeDownload_UsesLowercaseIdParameter()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.ResumeDownloadAsync(1234);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/resumedownload", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=1234");
    }

    [TestMethod]
    public async Task SetPowerDownload_UsesHistoricalParameterNames()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.SetPowerDownloadAsync(1234, 37);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/setpowerdownload", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=1234");
        StringAssert.Contains(query, "Powerdownload=37");
    }

    [TestMethod]
    public async Task SetPriority_UsesProductiveMultiIdParameters()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.SetPriorityAsync(new long[] { 41, 42, 41 }, 17);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/setpriority", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=41");
        StringAssert.Contains(query, "priority=17");
        StringAssert.Contains(query, "id1=42");
        Assert.IsFalse(query.Contains("id2=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SetShareDirectories_UsesProductiveSetSettingsParameters()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.SetShareDirectoriesAsync(
            new[]
            {
                new AjShareDirectory { Name = @"D:\Music", ShareMode = "subdirectory" },
                new AjShareDirectory { Name = @"D:\Movies", ShareMode = "singledirectory" },
                new AjShareDirectory { Name = " ", ShareMode = "subdirectory" }
            });

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/setsettings", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, @"sharedirectory1=D:\Music");
        StringAssert.Contains(query, "sharesub1=true");
        StringAssert.Contains(query, @"sharedirectory2=D:\Movies");
        StringAssert.Contains(query, "sharesub2=false");
        StringAssert.Contains(query, "countshares=2");
        Assert.IsFalse(query.Contains("sharedirectory3=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(query.Contains("sharesub3=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SetShareDirectories_ShrinkClearsTrailingLegacySlotsBeforeFinalCount()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.SetShareDirectoriesAsync(
            new[]
            {
                new AjShareDirectory { Name = "/mnt/music", ShareMode = "singledirectory" }
            },
            previousShareCount: 3);

        Assert.AreEqual(2, handler.RequestUris.Count);

        string clearQuery = WebUtility.UrlDecode(handler.RequestUris[0].Query);
        StringAssert.Contains(clearQuery, "sharedirectory1=/mnt/music");
        StringAssert.Contains(clearQuery, "sharesub1=false");
        StringAssert.Contains(clearQuery, "sharedirectory2=");
        StringAssert.Contains(clearQuery, "sharesub2=false");
        StringAssert.Contains(clearQuery, "sharedirectory3=");
        StringAssert.Contains(clearQuery, "sharesub3=false");
        StringAssert.Contains(clearQuery, "countshares=3");

        string finalQuery = WebUtility.UrlDecode(handler.RequestUris[1].Query);
        StringAssert.Contains(finalQuery, "sharedirectory1=/mnt/music");
        StringAssert.Contains(finalQuery, "sharesub1=false");
        StringAssert.Contains(finalQuery, "countshares=1");
        Assert.IsFalse(finalQuery.Contains("sharedirectory2=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SetShareDirectories_ZeroDesiredSharesClearsAllPreviousSlots()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.SetShareDirectoriesAsync(
            Array.Empty<AjShareDirectory>(),
            previousShareCount: 2);

        Assert.AreEqual(2, handler.RequestUris.Count);

        string clearQuery = WebUtility.UrlDecode(handler.RequestUris[0].Query);
        StringAssert.Contains(clearQuery, "sharedirectory1=");
        StringAssert.Contains(clearQuery, "sharesub1=false");
        StringAssert.Contains(clearQuery, "sharedirectory2=");
        StringAssert.Contains(clearQuery, "sharesub2=false");
        StringAssert.Contains(clearQuery, "countshares=2");

        string finalQuery = WebUtility.UrlDecode(handler.RequestUris[1].Query);
        StringAssert.Contains(finalQuery, "countshares=0");
        Assert.IsFalse(finalQuery.Contains("sharedirectory1=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task CleanDownloadList_UsesGetWithoutDownloadId()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.CleanDownloadListAsync();

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/cleandownloadlist", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        Assert.IsFalse(query.Contains("id=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ServerLogin_UsesGetWithServerId()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.ServerLoginAsync(77);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/serverlogin", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=77");
    }

    [TestMethod]
    public async Task RemoveServer_UsesGetWithServerId()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);

        await client.RemoveServerAsync(88);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/removeserver", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=88");
    }

    [TestMethod]
    public async Task ProcessLink_UsesEndpointEncodingAndParsesAcceptedResponse()
    {
        const string link = "ajfsp://file|demo file.bin|0123456789abcdef0123456789abcdef|12345/";
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("https", "example.org", basePath: "/applejuice/"), "secret", httpClient);
        AjCoreCompatibilityProfile profile = AjCoreCompatibilityProfile.FromCoreVersion("0.31.149.113");

        var result = await client.ProcessLinkDetailedAsync(link, profile);

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/applejuice/function/processlink", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "link=" + link);
        StringAssert.Contains(query, "password=5ebe2294ecd0e0f08eab7690d2a6ee69");
        Assert.IsFalse(query.Contains("subdir=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(handler.LastRequestUri.OriginalString.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TestConnection_AcceptsAppleJuiceSettingsShape()
    {
        const string settingsXml = "<settings><xmlport>9851</xmlport><incomingdirectory>/in</incomingdirectory><temporarydirectory>/tmp</temporarydirectory></settings>";
        RecordingHandler handler = new(settingsXml);
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "localhost", 9851), httpClient: httpClient);

        ConnectionTestResult result = await client.TestConnectionAsync();

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.Message, "Verbindung erfolgreich");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public System.Collections.Generic.List<Uri> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            if (request.RequestUri is not null)
                RequestUris.Add(request.RequestUri);
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody)
            };
            return Task.FromResult(response);
        }
    }
}
