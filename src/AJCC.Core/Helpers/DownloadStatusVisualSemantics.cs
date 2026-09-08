namespace AJCC.Core.Helpers;

public enum DownloadStatusVisualRole
{
    Neutral,
    Completed,
    Aborted,
    Paused
}

public static class DownloadStatusVisualSemantics
{
    public static DownloadStatusVisualRole GetRole(int status)
        => status switch
        {
            14 => DownloadStatusVisualRole.Completed,
            15 or 17 => DownloadStatusVisualRole.Aborted,
            18 => DownloadStatusVisualRole.Paused,
            _ => DownloadStatusVisualRole.Neutral
        };
}
