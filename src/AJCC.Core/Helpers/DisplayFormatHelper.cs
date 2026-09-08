using System.Globalization;

namespace AJCC.Core.Helpers;

public static class DisplayFormatHelper
{
    public static string Bytes(long bytes)
    {
        const double factor = 1024.0;
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };

        double value = Math.Abs((double)bytes);
        int unitIndex = 0;
        while (value >= factor && unitIndex < units.Length - 1)
        {
            value /= factor;
            unitIndex++;
        }

        double signedValue = bytes < 0 ? -value : value;
        string format = unitIndex == 0 ? "0" : "0.##";
        return signedValue.ToString(format, CultureInfo.CurrentCulture) + " " + units[unitIndex];
    }

    public static string BytesPerSecond(long bytesPerSecond)
        => Bytes(bytesPerSecond) + "/s";

    public static string Count(long count, string singular, string plural)
        => count == 1 ? "1 " + singular : count.ToString("N0", CultureInfo.CurrentCulture) + " " + plural;
}
