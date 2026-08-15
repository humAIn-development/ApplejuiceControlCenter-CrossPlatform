namespace AJCC.Core.Links;

public enum AjProcessLinkStatus
{
    Accepted,
    AlreadyDownloaded,
    IncorrectLink,
    Failure,
    Unknown
}

public sealed class AjProcessLinkResult
{
    public AjProcessLinkResult(AjProcessLinkStatus status, string rawResponse)
    {
        Status = status;
        RawResponse = rawResponse ?? string.Empty;
    }

    public AjProcessLinkStatus Status { get; }
    public string RawResponse { get; }
    public bool IsAccepted => Status == AjProcessLinkStatus.Accepted;
    public bool IsAlreadyDownloaded => Status == AjProcessLinkStatus.AlreadyDownloaded;
    public bool IsRejected => Status == AjProcessLinkStatus.IncorrectLink || Status == AjProcessLinkStatus.Failure || Status == AjProcessLinkStatus.Unknown;

    public string StatusText => Status switch
    {
        AjProcessLinkStatus.Accepted => "ok",
        AjProcessLinkStatus.AlreadyDownloaded => "already downloaded",
        AjProcessLinkStatus.IncorrectLink => "incorrect link",
        AjProcessLinkStatus.Failure => "failure",
        _ => "unknown response"
    };

    public static AjProcessLinkResult FromResponse(string? response)
    {
        string raw = response ?? string.Empty;
        string normalized = raw.Trim().ToLowerInvariant();

        if (normalized.Length == 0)
            return new AjProcessLinkResult(AjProcessLinkStatus.Unknown, raw);

        if (normalized.Contains("already downloaded", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bereits", StringComparison.OrdinalIgnoreCase) && normalized.Contains("download", StringComparison.OrdinalIgnoreCase))
            return new AjProcessLinkResult(AjProcessLinkStatus.AlreadyDownloaded, raw);

        if (normalized.Contains("incorrect link", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("invalid link", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ungült", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("unguelt", StringComparison.OrdinalIgnoreCase))
            return new AjProcessLinkResult(AjProcessLinkStatus.IncorrectLink, raw);

        if (normalized.Contains("failure", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("error", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fehler", StringComparison.OrdinalIgnoreCase))
            return new AjProcessLinkResult(AjProcessLinkStatus.Failure, raw);

        if (normalized.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ok", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(">ok<", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(">ok ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ok -", StringComparison.OrdinalIgnoreCase))
            return new AjProcessLinkResult(AjProcessLinkStatus.Accepted, raw);

        return new AjProcessLinkResult(AjProcessLinkStatus.Unknown, raw);
    }
}
