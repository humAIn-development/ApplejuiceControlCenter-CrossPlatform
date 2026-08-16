using AJCC.Core.Models;

namespace AJCC.Desktop.ViewModels;

internal static class DownloadActionSemantics
{
    public static bool IsTerminal(AjDownload download)
    {
        if (download.Status is 14 or 15 or 17)
            return true;

        string statusText = (download.StatusText ?? string.Empty).Trim();
        return statusText.Contains("Fertig", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Abbruch", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Abgebrochen", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Cancelled", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Canceled", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Complete", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Done", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPaused(AjDownload download)
    {
        if (download.Status == 18)
            return true;

        string statusText = (download.StatusText ?? string.Empty).Trim();
        return statusText.Contains("Pausiert", StringComparison.OrdinalIgnoreCase)
            || statusText.Contains("Paused", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanPause(AjDownload? download)
        => download is not null && !IsTerminal(download) && !IsPaused(download);

    public static bool CanResume(AjDownload? download)
        => download is not null && !IsTerminal(download) && IsPaused(download);

    public static bool CanCancel(AjDownload? download)
        => download is not null && !IsTerminal(download);
}
