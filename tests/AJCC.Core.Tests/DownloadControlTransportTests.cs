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
