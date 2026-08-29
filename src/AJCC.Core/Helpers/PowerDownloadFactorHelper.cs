using System.Globalization;

namespace AJCC.Core.Helpers;

internal static class PowerDownloadFactorHelper
{
    public const double MinimumFactor = 2.2;
    public const double MaximumFactor = 50.0;

    public static IReadOnlyList<double> Values { get; } = Enumerable.Range(22, 479)
        .Select(value => value / 10.0)
        .ToArray();

    public static double Normalize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            value = MaximumFactor;

        value = Math.Clamp(value, MinimumFactor, MaximumFactor);
        return Math.Round(value * 10.0, 0, MidpointRounding.AwayFromZero) / 10.0;
    }

    public static int ToRaw(double factor)
    {
        factor = Normalize(factor);
        return Math.Max(0, (int)Math.Round(factor * 10.0, MidpointRounding.AwayFromZero) - 10);
    }

    public static string Format(double factor)
        => Normalize(factor).ToString("0.0", CultureInfo.InvariantCulture);

    public static bool TryNormalizeInput(string? input, out double factor)
    {
        factor = MinimumFactor;
        string text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        int separatorCount = 0;
        int separatorIndex = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (char.IsDigit(ch))
                continue;

            if (ch == '.' || ch == ',')
            {
                separatorCount++;
                separatorIndex = i;
                if (separatorCount > 1)
                    return false;
                continue;
            }

            return false;
        }

        if (separatorCount == 1 && (separatorIndex <= 0 || separatorIndex >= text.Length - 1))
            return false;

        string normalized = text.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double parsed))
            return false;

        if (parsed < 0)
            return false;

        factor = Normalize(parsed);
        return true;
    }
}
