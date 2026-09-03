using System.Net;
using System.Net.Http;
using AJCC.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class DownloadControlTransportTests
{
    [TestMethod]
    public async Task CancelDownload_UsesLowercaseIdAndEndpointBasePath()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(
            new CoreEndpoint("https", "example.org", basePath: "/applejuice/"),
            "secret",
            httpClient);

        await client.CancelDownloadAsync(4711);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/applejuice/function/canceldownload", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=4711");
        Assert.IsFalse(query.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RenameDownload_UsesProductiveEndpointAndLowercaseId()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(
            new CoreEndpoint("https", "example.org", basePath: "/applejuice/"),
            "secret",
            httpClient);

        await client.RenameDownloadAsync(72, "Film A.mkv");

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/applejuice/function/renamedownload", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "id=72");
        StringAssert.Contains(query, "name=Film A.mkv");
        Assert.IsFalse(query.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("Serien/Staffel 01", "Serien/Staffel+01")]
    [DataRow("Serien\\Staffel 01", "Serien\\Staffel+01")]
    public async Task SetTargetDir_PreservesDirectorySeparatorsForOldCore(string target, string expectedEncodedTarget)
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(
            new CoreEndpoint("https", "example.org", basePath: "/applejuice/"),
            "secret",
            httpClient);

        await client.SetTargetDirAsync(73, target);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/applejuice/function/settargetdir", handler.LastRequestUri.AbsolutePath);
        string original = handler.LastRequestUri.OriginalString;
        StringAssert.Contains(original, "id=73");
        StringAssert.Contains(original, "dir=" + expectedEncodedTarget);
        Assert.IsFalse(original.Contains("%2F", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(original.Contains("%5C", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DirectoryXml_UsesConnectedCoreEndpoint()
    {
        RecordingHandler handler = new("<directory />");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(
            new CoreEndpoint("https", "example.org", basePath: "/applejuice/"),
            "secret",
            httpClient);

        await client.GetDirectoryXmlAsync("Incoming/Serien");

        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/applejuice/xml/directory.xml", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "directory=Incoming/Serien");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
            => _responseBody = responseBody;

        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody)
            });
        }
    }
}
