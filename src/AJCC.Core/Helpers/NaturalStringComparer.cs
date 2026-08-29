using System.Globalization;

namespace AJCC.Core.Helpers;

public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private static readonly CompareInfo CompareInfo = CultureInfo.CurrentCulture.CompareInfo;

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;

        int ix = 0;
        int iy = 0;

        while (ix < x.Length && iy < y.Length)
        {
            char cx = x[ix];
            char cy = y[iy];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int numericResult = CompareNumberRun(x, ref ix, y, ref iy);
                if (numericResult != 0)
                    return numericResult;

                continue;
            }

            int startX = ix;
            int startY = iy;

            while (ix < x.Length && !char.IsDigit(x[ix]))
                ix++;
            while (iy < y.Length && !char.IsDigit(y[iy]))
                iy++;

            string textX = x[startX..ix];
            string textY = y[startY..iy];
            int textResult = CompareInfo.Compare(textX, textY, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (textResult != 0)
                return textResult;
        }

        return x.Length.CompareTo(y.Length);
    }

    private static int CompareNumberRun(string x, ref int ix, string y, ref int iy)
    {
        int startX = ix;
        int startY = iy;

        while (ix < x.Length && char.IsDigit(x[ix]))
            ix++;
        while (iy < y.Length && char.IsDigit(y[iy]))
            iy++;

        string numberX = x[startX..ix].TrimStart('0');
        string numberY = y[startY..iy].TrimStart('0');

        if (numberX.Length == 0)
            numberX = "0";
        if (numberY.Length == 0)
            numberY = "0";

        int lengthResult = numberX.Length.CompareTo(numberY.Length);
        if (lengthResult != 0)
            return lengthResult;

        int ordinalResult = string.CompareOrdinal(numberX, numberY);
        if (ordinalResult != 0)
            return ordinalResult;

        int originalLengthX = ix - startX;
        int originalLengthY = iy - startY;
        return originalLengthX.CompareTo(originalLengthY);
    }
}
