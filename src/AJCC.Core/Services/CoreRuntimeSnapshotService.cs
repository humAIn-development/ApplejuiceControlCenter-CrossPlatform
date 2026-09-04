using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Protocol;

namespace AJCC.Core.Services;

public static class CoreRuntimeFilters
{
    public const string FullRuntime = "ids;down;server;uploads;search;user;informations";
}

public sealed class CoreRuntimeSnapshotService
{
    private readonly AppleJuiceCoreClient _client;

    public CoreRuntimeSnapshotService(AppleJuiceCoreClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<CoreRuntimeSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        string rawXml = await _client.GetModifiedXmlAsync(
            timestamp: 0,
            sessionId: null,
            filter: CoreRuntimeFilters.FullRuntime,
            cancellationToken).ConfigureAwait(false);

        return new CoreRuntimeSnapshot(AjXmlParser.ParseModified(rawXml), rawXml);
    }

    public async Task<CoreRuntimeSnapshot> LoadAndApplyAsync(AjState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        CoreRuntimeSnapshot snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        AjStateUpdater.Apply(state, snapshot.Result);
        if (snapshot.Result.CoreTimestamp > state.LastTimestamp)
            state.LastTimestamp = snapshot.Result.CoreTimestamp;

        return snapshot;
    }
}

public sealed record CoreRuntimeSnapshot(ModifiedParseResult Result, string RawXml);
