using System.Net;
using System.Net.Http;
using AJCC.Core.Models;
using AJCC.Core.Protocol;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class UploadObjectFilenameSemanticsTests
{
    [TestMethod]
    public async Task GetObject_UsesHistoricalUppercaseIdParameter()
    {
        RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(
            new CoreEndpoint("http", "127.0.0.1", 9851),
            httpClient: httpClient);

        await client.GetObjectXmlAsync(42);

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/xml/getobject.xml", handler.LastRequestUri.AbsolutePath);
        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "ID=42");
        Assert.IsFalse(query.Contains("id=42", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryExtractUsableFilename_ReadsFilenameThenNameAttributes()
    {
        string? fromFilename = UploadObjectFilenameSemantics.TryExtractUsableFilename(
            "<object filename=\"Movie.mkv\" />");
        string? fromName = UploadObjectFilenameSemantics.TryExtractUsableFilename(
            "<root><object name=\"Music.flac\" /></root>");

        Assert.AreEqual("Movie.mkv", fromFilename);
        Assert.AreEqual("Music.flac", fromName);
    }

    [TestMethod]
    public void TryExtractUsableFilename_RejectsTechnicalAndMalformedPayloads()
    {
        Assert.IsNull(UploadObjectFilenameSemantics.TryExtractUsableFilename(
            "<object filename=\"12345.data\" />"));
        Assert.IsNull(UploadObjectFilenameSemantics.TryExtractUsableFilename(
            "<object filename=\"ShareID 42\" />"));
        Assert.IsNull(UploadObjectFilenameSemantics.TryExtractUsableFilename("<broken"));
    }

    [TestMethod]
    public void GetCandidateShareIds_IsBoundedAndRespectsCacheAndRetryDelay()
    {
        DateTime nowUtc = new(2026, 8, 30, 8, 0, 0, DateTimeKind.Utc);
        List<AjUpload> uploads = Enumerable.Range(1, 15)
            .Select(id => new AjUpload
            {
                Id = id,
                ShareId = id,
                Filename = $"ShareID {id}"
            })
            .ToList();
        Dictionary<long, string> cached = new()
        {
            [2] = "Cached.mkv"
        };
        Dictionary<long, DateTime> failed = new()
        {
            [3] = nowUtc - TimeSpan.FromMinutes(1),
            [4] = nowUtc - TimeSpan.FromMinutes(6)
        };

        IReadOnlyList<long> candidates = UploadObjectFilenameSemantics.GetCandidateShareIds(
            uploads,
            cached,
            failed,
            nowUtc,
            TimeSpan.FromMinutes(5),
            maxPerSweep: 12);

        CollectionAssert.AreEqual(
            new long[] { 1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 },
            candidates.ToArray());
    }

    [TestMethod]
    public void ApplyCachedFilenames_ReplacesOnlyTechnicalNamesForMatchingShareId()
    {
        List<AjUpload> uploads =
        [
            new AjUpload { Id = 1, ShareId = 42, Filename = "ShareID 42" },
            new AjUpload { Id = 2, ShareId = 42, Filename = "AlreadyGood.mkv" },
            new AjUpload { Id = 3, ShareId = 43, Filename = "12345.data" }
        ];
        Dictionary<long, string> cached = new()
        {
            [42] = @"C:\Shared\Resolved.mkv"
        };

        bool changed = UploadObjectFilenameSemantics.ApplyCachedFilenames(uploads, cached);

        Assert.IsTrue(changed);
        Assert.AreEqual(@"C:\Shared\Resolved.mkv", uploads[0].Filename);
        Assert.AreEqual("AlreadyGood.mkv", uploads[1].Filename);
        Assert.AreEqual("12345.data", uploads[2].Filename);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<root />")
            });
        }
    }
}
