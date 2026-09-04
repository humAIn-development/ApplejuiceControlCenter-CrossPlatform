using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static class DownloadTransferSpeedSemantics
{
    public static long CalculateDisplayedTotal(IEnumerable<AjDownload> downloads)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        return downloads.Sum(download => Math.Max(0L, download.DownloadSpeed));
    }
}
