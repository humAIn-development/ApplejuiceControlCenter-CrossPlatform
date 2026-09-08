using System.Globalization;
using Avalonia.Data.Converters;

namespace AJCC.Desktop.Services;

public sealed class DownloadStatusRuleMatchConverter : IValueConverter
{
    private static readonly HashSet<int> KnownStatuses = new()
    {
        0,
        1,
        12,
        13,
        14,
        15,
        16,
        17,
        18
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int status
            || !int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int expected))
        {
            return false;
        }

        return expected == -1
            ? !KnownStatuses.Contains(status)
            : status == expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
