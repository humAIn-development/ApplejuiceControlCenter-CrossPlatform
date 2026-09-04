using System.Net;
using AJCC.Core.Models;
using AJCC.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class CoreSettingsTransportTests
{
    [TestMethod]
    public void BuildComplete_PreservesCoreValuesAndShareList()
    {
        AjSettings settings = CreateSettings();
        settings.SharedDirectories.Add(new AjShareDirectory { Name = "/mnt/music", ShareMode = "subdirectory" });
        settings.SharedDirectories.Add(new AjShareDirectory { Name = "/mnt/movies", ShareMode = "singledirectory" });

        IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(settings);

        Assert.AreEqual("test-user", parameters["nick"]);
        Assert.AreEqual("8000", parameters["port"]);
        Assert.AreEqual("9851", parameters["XMLPort"]);
        Assert.AreEqual("500", parameters["maxconnections"]);
        Assert.AreEqual("5000", parameters["maxupload"]);
        Assert.AreEqual("165", parameters["speedperslot"]);
        Assert.AreEqual("0", parameters["maxdownload"]);
        Assert.AreEqual("50", parameters["maxnewconnectionsperturn"]);
        Assert.AreEqual("250", parameters["maxsourcesperfile"]);
        Assert.AreEqual("true", parameters["autoconnect"]);
        Assert.AreEqual("/srv/incoming/", parameters["incomingdirectory"]);
        Assert.AreEqual("/srv/temp/", parameters["temporarydirectory"]);
        Assert.AreEqual("/mnt/music", parameters["sharedirectory1"]);
        Assert.AreEqual("true", parameters["sharesub1"]);
        Assert.AreEqual("/mnt/movies", parameters["sharedirectory2"]);
        Assert.AreEqual("false", parameters["sharesub2"]);
        Assert.AreEqual("2", parameters["countshares"]);
        Assert.AreEqual(17, parameters.Count);
    }

    [TestMethod]
    public void BuildComplete_AppliesOverridesWithoutDroppingOtherSettings()
    {
        AjSettings settings = CreateSettings();
        settings.SharedDirectories.Add(new AjShareDirectory { Name = "/mnt/music", ShareMode = "subdirectory" });

        AjSettingsOverrides overrides = new()
        {
            Nick = string.Empty,
            MaxConnections = 900,
            MaxUpload = 7777,
            SpeedPerSlot = 42,
            AutoConnect = false
        };

        IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(settings, overrides);

        Assert.AreEqual(string.Empty, parameters["nick"]);
        Assert.AreEqual("900", parameters["maxconnections"]);
        Assert.AreEqual("7777", parameters["maxupload"]);
        Assert.AreEqual("42", parameters["speedperslot"]);
        Assert.AreEqual("false", parameters["autoconnect"]);
        Assert.AreEqual("0", parameters["maxdownload"]);
        Assert.AreEqual("50", parameters["maxnewconnectionsperturn"]);
        Assert.AreEqual("/srv/incoming/", parameters["incomingdirectory"]);
        Assert.AreEqual("/mnt/music", parameters["sharedirectory1"]);
        Assert.AreEqual("true", parameters["sharesub1"]);
        Assert.AreEqual("1", parameters["countshares"]);
    }

    [TestMethod]
    public void BuildComplete_ClampsNegativeNumbersAndSkipsBlankShareEntries()
    {
        AjSettings settings = CreateSettings();
        settings.SharedDirectories.Add(new AjShareDirectory { Name = " ", ShareMode = "subdirectory" });

        AjSettingsOverrides overrides = new()
        {
            Port = -1,
            XmlPort = -2,
            MaxConnections = -3,
            MaxUpload = -4,
            MaxDownload = -5,
            MaxNewConnectionsPerTurn = -6,
            MaxSourcesPerFile = -7,
            SpeedPerSlot = -8
        };

        IReadOnlyDictionary<string, string> parameters = AjSettingsParameters.BuildComplete(settings, overrides);

        Assert.AreEqual("0", parameters["port"]);
        Assert.AreEqual("0", parameters["XMLPort"]);
        Assert.AreEqual("0", parameters["maxconnections"]);
        Assert.AreEqual("0", parameters["maxupload"]);
        Assert.AreEqual("0", parameters["maxdownload"]);
        Assert.AreEqual("0", parameters["maxnewconnectionsperturn"]);
        Assert.AreEqual("0", parameters["maxsourcesperfile"]);
        Assert.AreEqual("0", parameters["speedperslot"]);
        Assert.AreEqual("0", parameters["countshares"]);
        Assert.IsFalse(parameters.ContainsKey("sharedirectory1"));
    }

    [TestMethod]
    public async Task SetSettings_UsesSetSettingsEndpointAndHashedAuthentication()
    {
        RecordingHandler handler = new("OK");
        using HttpClient httpClient = new(handler);
        AppleJuiceCoreClient client = new(
            new CoreEndpoint("http", "127.0.0.1", 9851),
            "secret",
            httpClient);

        await client.SetSettingsAsync(new Dictionary<string, string>
        {
            ["maxconnections"] = "500",
            ["countshares"] = "0"
        });

        Assert.AreEqual(HttpMethod.Get, handler.LastMethod);
        Assert.IsNotNull(handler.LastRequestUri);
        Assert.AreEqual("/function/setsettings", handler.LastRequestUri.AbsolutePath);

        string query = WebUtility.UrlDecode(handler.LastRequestUri.Query);
        StringAssert.Contains(query, "maxconnections=500");
        StringAssert.Contains(query, "countshares=0");
        StringAssert.Contains(query, "password=5ebe2294ecd0e0f08eab7690d2a6ee69");
        Assert.IsFalse(query.Contains("secret", StringComparison.Ordinal));
    }

    private static AjSettings CreateSettings()
        => new()
        {
            Nick = "test-user",
            Port = 8000,
            XmlPort = 9851,
            MaxConnections = 500,
            MaxUpload = 5000,
            SpeedPerSlot = 165,
            MaxDownload = 0,
            MaxNewConnectionsPerTurn = 50,
            MaxSourcesPerFile = 250,
            AutoConnect = true,
            IncomingDirectory = "/srv/incoming/",
            TemporaryDirectory = "/srv/temp/"
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;

        public RecordingHandler(string body)
        {
            _body = body;
        }

        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body)
            });
        }
    }
}
