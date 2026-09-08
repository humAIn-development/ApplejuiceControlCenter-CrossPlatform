using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Protocol;

namespace AJCC.Core.Services;

public sealed class CoreRuntimeBootstrapper
{
    private const string InitialModifiedFilter = "ids;down;server;uploads;search;user;informations";
    private readonly AppleJuiceCoreClient _client;

    public CoreRuntimeBootstrapper(AppleJuiceCoreClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<CoreBootstrapResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        AjState state = new();

        string settingsXml = await _client.GetSettingsXmlAsync(cancellationToken).ConfigureAwait(false);
        state.Settings = AjXmlParser.ParseSettings(settingsXml);

        string informationXml = await _client.GetInformationXmlAsync(cancellationToken).ConfigureAwait(false);
        state.NetworkInfo = AjXmlParser.ParseInformationXml(informationXml);
        string coreVersion = AjXmlParser.ParseCoreVersion(informationXml);

        string sessionXml = await _client.GetSessionXmlAsync(cancellationToken).ConfigureAwait(false);
        state.SessionId = AjXmlParser.ParseSessionId(sessionXml);

        string modifiedXml = await _client.GetModifiedXmlAsync(
            timestamp: 0,
            sessionId: state.SessionId,
            filter: InitialModifiedFilter,
            cancellationToken).ConfigureAwait(false);

        ModifiedParseResult modified = AjXmlParser.ParseModified(modifiedXml);
        AjStateUpdater.Apply(state, modified);
        if (modified.CoreTimestamp > 0)
            state.LastTimestamp = modified.CoreTimestamp;

        return new CoreBootstrapResult(state, coreVersion);
    }
}

public sealed record CoreBootstrapResult(AjState State, string CoreVersion);
