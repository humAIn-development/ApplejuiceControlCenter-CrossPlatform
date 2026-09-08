namespace AJCC.Core.Services;

public sealed class ServerReconnectRestrictionState
{
    private DateTimeOffset _untilUtc = DateTimeOffset.MinValue;

    public DateTimeOffset UntilUtc => _untilUtc;
    public bool HasExactCountdown { get; private set; }
    public long TargetServerId { get; private set; }
    public bool IsMarked => _untilUtc != DateTimeOffset.MinValue;

    public bool IsActive(DateTimeOffset nowUtc)
        => _untilUtc > nowUtc;

    public TimeSpan GetRemaining(DateTimeOffset nowUtc)
    {
        TimeSpan remaining = _untilUtc - nowUtc;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void Mark(
        TimeSpan remaining,
        bool hasExactCountdown,
        long targetServerId,
        DateTimeOffset nowUtc)
    {
        if (remaining <= TimeSpan.Zero)
            return;

        DateTimeOffset until = nowUtc.Add(remaining);
        if (until > _untilUtc)
            _untilUtc = until;

        HasExactCountdown = hasExactCountdown;
        TargetServerId = targetServerId;
    }

    public bool ClearIfConnected(long connectedServerId)
    {
        if (!IsMarked
            || connectedServerId <= 0
            || TargetServerId <= 0
            || connectedServerId != TargetServerId)
        {
            return false;
        }

        Clear();
        return true;
    }

    public bool ClearIfExpired(DateTimeOffset nowUtc)
    {
        if (!IsMarked || _untilUtc > nowUtc)
            return false;

        Clear();
        return true;
    }

    public void Clear()
    {
        _untilUtc = DateTimeOffset.MinValue;
        HasExactCountdown = false;
        TargetServerId = 0;
    }
}
