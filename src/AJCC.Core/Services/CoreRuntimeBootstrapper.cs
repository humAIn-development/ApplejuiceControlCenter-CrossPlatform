using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Protocol;

namespace AJCC.Core.Services;

public sealed class CoreRuntimeBootstrapper
{
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

        // Keep the session for subsequent live polling, but deliberately do not attach it
        // to the initial timestamp=0 runtime snapshot. The productive AJCC uses the same
        // sessionless full-sync semantics; a fresh session may otherwise describe only
        // changes relative to that session instead of the complete pre-existing state.
        string sessionXml = await _client.GetSessionXmlAsync(cancellationToken).ConfigureAwait(false);
        state.SessionId = AjXmlParser.ParseSessionId(sessionXml);

        CoreRuntimeSnapshot snapshot = await new CoreRuntimeSnapshotService(_client)
            .LoadAndApplyAsync(state, cancellationToken)
            .ConfigureAwait(false);

        return new CoreBootstrapResult(state, coreVersion);
    }
}

public sealed record CoreBootstrapResult(AjState State, string CoreVersion);
