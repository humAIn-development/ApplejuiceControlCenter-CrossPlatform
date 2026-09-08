using System.Text.RegularExpressions;

namespace AJCC.Core.Services;

public readonly record struct ServerReconnectEvaluation(
    bool RequiresConfirmation,
    TimeSpan ConnectedFor,
    TimeSpan RecommendedWait)
{
    public bool MayProceedWithoutConfirmation => !RequiresConfirmation;
}

public static class ServerReconnectPolicy
{
    public const int RestrictionMinutes = 30;

    public static TimeSpan RestrictionWindow => TimeSpan.FromMinutes(RestrictionMinutes);

    public static ServerReconnectEvaluation EvaluateLogin(
        long connectedServerId,
        long targetServerId,
        long connectedSinceUnixMilliseconds,
        long nowUnixMilliseconds)
    {
        if (connectedServerId <= 0
            || targetServerId == connectedServerId
            || connectedSinceUnixMilliseconds <= 0)
        {
            return new ServerReconnectEvaluation(
                RequiresConfirmation: false,
                ConnectedFor: TimeSpan.Zero,
                RecommendedWait: TimeSpan.Zero);
        }

        long elapsedMilliseconds = Math.Max(0, nowUnixMilliseconds - connectedSinceUnixMilliseconds);
        TimeSpan connectedFor = TimeSpan.FromMilliseconds(elapsedMilliseconds);
        TimeSpan safeWindow = RestrictionWindow;

        if (connectedFor >= safeWindow)
        {
            return new ServerReconnectEvaluation(
                RequiresConfirmation: false,
                ConnectedFor: connectedFor,
                RecommendedWait: TimeSpan.Zero);
        }

        return new ServerReconnectEvaluation(
            RequiresConfirmation: true,
            ConnectedFor: connectedFor,
            RecommendedWait: safeWindow - connectedFor);
    }

    public static bool LooksLikeRestrictionResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        string text = response.ToLowerInvariant();
        return text.Contains("wait") && text.Contains("reconnect")
               || text.Contains("30 minutes")
               || text.Contains("30 minuten")
               || text.Contains("until reconnect")
               || text.Contains("reconnect") && text.Contains("minute")
               || text.Contains("reconnect") && text.Contains("minuten");
    }

    public static TimeSpan ExtractRestrictionRemaining(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return RestrictionWindow;

        Match match = Regex.Match(
            response.ToLowerInvariant(),
            @"(\d+)\s*(minutes?|minuten?|mins?|min\.)");

        if (match.Success
            && int.TryParse(match.Groups[1].Value, out int minutes)
            && minutes > 0)
        {
            return TimeSpan.FromMinutes(Math.Min(RestrictionMinutes, minutes));
        }

        return RestrictionWindow;
    }
}
