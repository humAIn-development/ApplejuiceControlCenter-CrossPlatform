using System.Globalization;
using AJCC.Core.Models;

namespace AJCC.Core.Links;

public static class AjLinkParser
{
    public static AjLinkInfo Parse(string rawLink)
    {
        AjLinkInfo info = new()
        {
            RawLink = NormalizeRawLink(rawLink)
        };

        if (string.IsNullOrWhiteSpace(info.RawLink))
            return Invalid(info, "Link ist leer.");

        if (!info.RawLink.StartsWith("ajfsp://", StringComparison.OrdinalIgnoreCase))
            return Invalid(info, "Link muss mit ajfsp:// beginnen.");

        string body = info.RawLink[8..].Trim();
        string[] parts = body.Split('|');
        if (parts.Length < 4)
            return Invalid(info, "Erwartet: ajfsp://file|Dateiname|Checksum|Größe[/] oder mit zusätzlicher Quelle.");

        info.LinkType = parts[0].Trim();
        if (!info.LinkType.Equals("file", StringComparison.OrdinalIgnoreCase))
            return Invalid(info, "Derzeit wird nur der Linktyp file unterstützt.");

        info.FileName = parts[1].Trim();
        info.Checksum = parts[2].Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(info.FileName))
            return Invalid(info, "Dateiname fehlt.");

        if (!IsHexMd5(info.Checksum))
            return Invalid(info, "Checksum muss eine 32-stellige MD5-Hex-Zeichenfolge sein.");

        string sizeText = parts[3].Trim().TrimEnd('/');
        if (!long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long size) || size < 0)
            return Invalid(info, "Dateigröße ist ungültig.");

        info.Size = size;

        if (parts.Length >= 5)
            ParseSource(info, string.Join("|", parts.Skip(4)).Trim());

        info.IsValid = true;
        return info;
    }

    private static string NormalizeRawLink(string rawLink)
    {
        string value = (rawLink ?? string.Empty).Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Trim();

        value = value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

        if (value.Contains('%'))
        {
            try
            {
                value = Uri.UnescapeDataString(value);
            }
            catch
            {
                value = value
                    .Replace("%7C", "|", StringComparison.OrdinalIgnoreCase)
                    .Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
            }
        }

        return value;
    }

    private static void ParseSource(AjLinkInfo info, string source)
    {
        source = source.Trim();
        info.SourceRaw = source;

        string technicalSource = source.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(technicalSource))
            return;

        string[] sourceParts = technicalSource.Split(':');
        if (sourceParts.Length == 4)
        {
            info.SourceIp = sourceParts[0].Trim();
            info.SourcePort = sourceParts[1].Trim();
            info.SourceHost = sourceParts[2].Trim();
            info.SourceXmlPort = sourceParts[3].Trim();
        }
    }

    private static bool IsHexMd5(string value)
        => value.Length == 32 && value.All(Uri.IsHexDigit);

    private static AjLinkInfo Invalid(AjLinkInfo info, string error)
    {
        info.IsValid = false;
        info.Error = error;
        return info;
    }
}
