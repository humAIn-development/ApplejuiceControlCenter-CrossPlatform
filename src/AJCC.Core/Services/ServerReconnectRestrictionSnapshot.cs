namespace AJCC.Core.Services;

public readonly record struct ServerReconnectRestrictionSnapshot(
    DateTimeOffset UntilUtc,
    bool HasExactCountdown,
    long TargetServerId)
{
    public bool IsMarked => UntilUtc != DateTimeOffset.MinValue;
}

public static class ServerReconnectRestrictionSnapshots
{
    public static ServerReconnectRestrictionSnapshot Capture(ServerReconnectRestrictionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.IsMarked
            ? new ServerReconnectRestrictionSnapshot(
                state.UntilUtc,
                state.HasExactCountdown,
                state.TargetServerId)
            : default;
    }

    public static bool Restore(
        ServerReconnectRestrictionState state,
        ServerReconnectRestrictionSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Clear();
        if (!snapshot.IsMarked || snapshot.UntilUtc <= nowUtc)
            return false;

        state.Mark(
            snapshot.UntilUtc - nowUtc,
            snapshot.HasExactCountdown,
            snapshot.TargetServerId,
            nowUtc);
        return true;
    }
}
