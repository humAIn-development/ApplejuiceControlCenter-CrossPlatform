using System.Globalization;
using System.Xml.Linq;

namespace AJCC.Core.Helpers;

public static class XmlHelper
{
    public static string Attr(this XElement element, string name, string fallback = "")
        => FindAttributeValue(element, name) ?? fallback;

    public static int IntAttr(this XElement element, string name, int fallback = 0)
        => int.TryParse(FindAttributeValue(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    public static long LongAttr(this XElement element, string name, long fallback = 0)
        => long.TryParse(FindAttributeValue(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : fallback;

    public static bool BoolAttr(this XElement element, string name, bool fallback = false)
    {
        string? value = FindAttributeValue(element, name);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindAttributeValue(XElement element, string name)
        => element.Attribute(name)?.Value
            ?? element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    public static string ElementText(this XElement parent, string name, string fallback = "")
        => parent.Element(name)?.Value ?? fallback;

    public static int ElementInt(this XElement parent, string name, int fallback = 0)
        => int.TryParse(parent.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    public static long ElementLong(this XElement parent, string name, long fallback = 0)
        => long.TryParse(parent.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : fallback;
}
