using System.Xml;
using System.Xml.Linq;

namespace AJCC.Core.Parsers;

public static class AjServerListParser
{
    public static IReadOnlyList<string> ParseLinks(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Array.Empty<string>();

        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using StringReader textReader = new(xml);
        using XmlReader reader = XmlReader.Create(textReader, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.None);

        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "server", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("link")?.Value?.Trim() ?? string.Empty)
            .Where(link => link.StartsWith("ajfsp://server|", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
