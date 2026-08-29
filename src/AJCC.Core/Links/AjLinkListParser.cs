using System.Globalization;
using AJCC.Core.Models;

namespace AJCC.Core.Links;

public sealed class AjLinkListImportResult
{
    public string FilePath { get; init; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public List<AjLinkInfo> Links { get; } = new();
    public List<string> Errors { get; } = new();
    public int CandidateCount { get; set; }
    public int SkippedCount => Errors.Count;
}

public static class AjLinkListParser
{
    private const string Separator = "-----";

    public static AjLinkListImportResult ParseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Kein AJL-Dateipfad angegeben.", nameof(filePath));

        string[] lines = File.ReadAllLines(filePath);
        return ParseLines(lines, filePath);
    }

    public static AjLinkListImportResult ParseLines(IEnumerable<string> lines, string filePath = "")
    {
        AjLinkListImportResult result = new() { FilePath = filePath };
        List<string> normalizedLines = lines.Select(line => (line ?? string.Empty).Trim()).ToList();

        int separatorIndex = normalizedLines.FindIndex(line => line.Equals(Separator, StringComparison.Ordinal));
        int dataStartIndex = separatorIndex >= 0 ? separatorIndex + 1 : 0;

        foreach (string headerLine in normalizedLines.Take(dataStartIndex))
        {
            if (headerLine.StartsWith("Quelle:", StringComparison.OrdinalIgnoreCase))
            {
                result.Source = headerLine["Quelle:".Length..].Trim();
                break;
            }
            if (headerLine.StartsWith("Source:", StringComparison.OrdinalIgnoreCase))
            {
                result.Source = headerLine["Source:".Length..].Trim();
                break;
            }
        }

        List<string> dataLines = normalizedLines.Skip(dataStartIndex).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (dataLines.Count > 0 && IsNumericMarker(dataLines[0]))
            dataLines.RemoveAt(0);

        if (dataLines.Any(line => line.StartsWith("ajfsp://", StringComparison.OrdinalIgnoreCase)))
        {
            ParseAjfspLineList(dataLines, result);
            return result;
        }

        ParseLegacyThreeLineBlocks(dataLines, result);
        return result;
    }

    private static void ParseAjfspLineList(IReadOnlyList<string> dataLines, AjLinkListImportResult result)
    {
        int entryNumber = 0;
        foreach (string rawLine in dataLines)
        {
            string rawLink = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(rawLink))
                continue;

            entryNumber++;
            result.CandidateCount++;
            AjLinkInfo linkInfo = AjLinkParser.Parse(rawLink);
            if (!linkInfo.IsValid)
            {
                result.Errors.Add($"Eintrag {entryNumber}: {linkInfo.Error}");
                continue;
            }
            result.Links.Add(linkInfo);
        }
    }

    private static void ParseLegacyThreeLineBlocks(IReadOnlyList<string> dataLines, AjLinkListImportResult result)
    {
        for (int index = 0; index < dataLines.Count; index += 3)
        {
            int entryNumber = (index / 3) + 1;
            if (index + 2 >= dataLines.Count)
            {
                result.Errors.Add($"Eintrag {entryNumber}: unvollständiger 3-Zeilen-Block.");
                break;
            }

            string filename = dataLines[index].Trim();
            string checksum = dataLines[index + 1].Trim().ToLowerInvariant();
            string sizeText = dataLines[index + 2].Trim();
            result.CandidateCount++;

            if (string.IsNullOrWhiteSpace(filename))
            {
                result.Errors.Add($"Eintrag {entryNumber}: Dateiname fehlt.");
                continue;
            }
            if (filename.Contains('|'))
            {
                result.Errors.Add($"Eintrag {entryNumber}: Dateiname enthält das AJFSP-Trennzeichen '|'.");
                continue;
            }
            if (!long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long size) || size <= 0)
            {
                result.Errors.Add($"Eintrag {entryNumber}: Dateigröße ist ungültig.");
                continue;
            }

            AjLinkInfo linkInfo = AjLinkParser.Parse($"ajfsp://file|{filename}|{checksum}|{size}/");
            if (!linkInfo.IsValid)
            {
                result.Errors.Add($"Eintrag {entryNumber}: {linkInfo.Error}");
                continue;
            }
            result.Links.Add(linkInfo);
        }
    }

    private static bool IsNumericMarker(string value) => value.All(char.IsDigit);
}
