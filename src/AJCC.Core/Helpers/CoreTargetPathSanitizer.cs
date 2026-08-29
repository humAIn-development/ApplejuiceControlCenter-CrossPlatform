using System.Globalization;
using System.Text;

namespace AJCC.Core.Helpers;

internal static class CoreTargetPathSanitizer
{
    public static string NormalizeManualSubfolderInput(string value, out bool changed)
    {
        changed = false;
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Trim().Trim('"');
        string sanitized = SanitizePathText(normalized, preserveDirectorySeparators: true, out bool pathChanged);
        changed = pathChanged || !string.Equals(normalized, sanitized, StringComparison.Ordinal);
        return sanitized.Trim().Trim('"');
    }

    public static string NormalizeRelativeTargetDirectory(string value, char separator, out bool changed)
    {
        changed = false;
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        char safeSeparator = separator is '/' or '\\' ? separator : '\\';
        string normalized = value.Trim().Trim('"')
            .Replace('/', safeSeparator)
            .Replace('\\', safeSeparator)
            .Trim(safeSeparator);

        string[] parts = normalized
            .Split(safeSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0 && part != "." && part != "..")
            .ToArray();

        StringBuilder builder = new();
        foreach (string part in parts)
        {
            string sanitizedPart = SanitizeDirectorySegment(part, out bool partChanged);
            changed |= partChanged || !string.Equals(part, sanitizedPart, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(sanitizedPart))
                continue;

            if (builder.Length > 0)
                builder.Append(safeSeparator);
            builder.Append(sanitizedPart);
        }

        string result = builder.ToString();
        changed |= !string.Equals(normalized, result, StringComparison.Ordinal);
        return result;
    }

    public static string SanitizeDirectorySegment(string value, out bool changed)
    {
        changed = false;
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = SanitizePathText(value, preserveDirectorySeparators: false, out bool textChanged);
        sanitized = CollapseSpaces(sanitized).Trim();
        changed = textChanged || !string.Equals(value, sanitized, StringComparison.Ordinal);
        return sanitized;
    }

    private static string SanitizePathText(string value, bool preserveDirectorySeparators, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string normalized = value.Normalize(NormalizationForm.FormC);
        StringBuilder builder = new(normalized.Length);

        foreach (char c in normalized)
        {
            string replacement = GetReplacement(c, preserveDirectorySeparators);
            if (replacement.Length == 0)
            {
                changed = true;
                continue;
            }

            if (replacement.Length != 1 || replacement[0] != c)
                changed = true;

            builder.Append(replacement);
        }

        return CollapseSpaces(builder.ToString());
    }

    private static string GetReplacement(char c, bool preserveDirectorySeparators)
    {
        if (c == '\\' || c == '/')
            return preserveDirectorySeparators ? c.ToString() : " ";

        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
        if (category == UnicodeCategory.Format)
            return string.Empty;

        if (char.IsControl(c))
            return char.IsWhiteSpace(c) ? " " : string.Empty;

        return c switch
        {
            '\u2026' => string.Empty,
            '.' => string.Empty,
            '\u00A0' => " ",
            '\u2007' => " ",
            '\u202F' => " ",
            '\u200B' => string.Empty,
            '\u200C' => string.Empty,
            '\u200D' => string.Empty,
            '\uFEFF' => string.Empty,
            ':' => " - ",
            '"' => "'",
            '<' => " ",
            '>' => " ",
            '|' => " ",
            '?' => " ",
            '*' => " ",
            _ => c.ToString()
        };
    }

    private static string CollapseSpaces(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder builder = new(value.Length);
        bool previousWasSpace = false;
        foreach (char c in value)
        {
            if (c == ' ')
            {
                if (previousWasSpace)
                    continue;
                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
