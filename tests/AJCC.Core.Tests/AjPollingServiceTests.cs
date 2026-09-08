using System.Net;
using System.Net.Http;
using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Protocol;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjPollingServiceTests
{
    [TestMethod]
    public async Task Polling_CreatesSessionAndPublishesModifiedState()
    {
        PollingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);
        AjPollingService polling = new(client);
        AjState state = new();
        TaskCompletionSource<ModifiedParseResult> received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        polling.ModifiedReceived += (result, _) => received.TrySetResult(result);

        await polling.StartAsync(state, intervalMs: 10);
        ModifiedParseResult result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await polling.StopAsync();

        Assert.AreEqual("poll-session", state.SessionId);
        Assert.AreEqual(123L, state.LastTimestamp);
        Assert.AreEqual(1, result.Downloads.Count);
        Assert.AreEqual("poll.bin", result.Downloads[0].Filename);
        Assert.IsTrue(handler.ModifiedRequestCount >= 1);
        StringAssert.Contains(WebUtility.UrlDecode(handler.FirstModifiedQuery ?? string.Empty), "filter=down;uploads;server;informations;search");
    }

    [TestMethod]
    public async Task Polling_UsesExistingTimestampAndSession()
    {
        PollingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("https", "core.example.org", basePath: "/aj/"), httpClient: httpClient);
        AjPollingService polling = new(client);
        AjState state = new() { SessionId = "existing-session", LastTimestamp = 77 };
        TaskCompletionSource<bool> received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        polling.ModifiedReceived += (_, _) => received.TrySetResult(true);

        await polling.StartAsync(state, intervalMs: 10);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await polling.StopAsync();

        Assert.AreEqual(0, handler.SessionRequestCount);
        string query = WebUtility.UrlDecode(handler.FirstModifiedQuery ?? string.Empty);
        StringAssert.Contains(query, "timestamp=77");
        StringAssert.Contains(query, "session=existing-session");
    }

    [TestMethod]
    public async Task Polling_RequestsFullResyncAfterThreeMissingCoreTimestamps()
    {
        MissingTimestampPollingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(new CoreEndpoint("http", "127.0.0.1", 9851), httpClient: httpClient);
        AjPollingService polling = new(client);
        AjState state = new() { SessionId = "existing-session", LastTimestamp = 77 };
        TaskCompletionSource<(int Count, string Reason)> requested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        polling.FullResyncRequested += (count, reason) => requested.TrySetResult((count, reason));

        await polling.StartAsync(state, intervalMs: 10);
        (int count, string reason) = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await polling.StopAsync();

        Assert.AreEqual(3, count);
        Assert.AreEqual(77L, state.LastTimestamp);
        Assert.IsTrue(handler.ModifiedRequestCount >= 3);
        StringAssert.Contains(reason, "keinen Core-Zeitstempel");
    }

    private sealed class PollingHandler : HttpMessageHandler
    {
        public int SessionRequestCount { get; private set; }
        public int ModifiedRequestCount { get; private set; }
        public string? FirstModifiedQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string body;

            if (path.EndsWith("/xml/getsession.xml", StringComparison.Ordinal))
            {
                SessionRequestCount++;
                body = "<applejuice><session id='poll-session' /></applejuice>";
            }
            else if (path.EndsWith("/xml/modified.xml", StringComparison.Ordinal))
            {
                ModifiedRequestCount++;
                FirstModifiedQuery ??= request.RequestUri?.Query;
                body = "<modified><time>123</time><download id='4' size='100' status='0' filename='poll.bin' ready='25' /></modified>";
            }
            else
            {
                body = "<applejuice />";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
    private sealed class MissingTimestampPollingHandler : HttpMessageHandler
    {
        public int ModifiedRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string body;

            if (path.EndsWith("/xml/modified.xml", StringComparison.Ordinal))
            {
                ModifiedRequestCount++;
                body = "<modified />";
            }
            else
            {
                body = "<applejuice />";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }

}
