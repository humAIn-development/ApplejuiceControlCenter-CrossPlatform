namespace AJCC.Core.Helpers;

public sealed record CoreTargetDirectoryNormalizationResult(
    bool Success,
    string Value,
    bool Changed,
    string ErrorMessage);

public static class CoreTargetDirectory
{
    public static char DetermineSeparator(params string?[] paths)
    {
        foreach (string? path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            bool hasSlash = path.Contains('/');
            bool hasBackslash = path.Contains('\\');
            if (hasSlash && !hasBackslash)
                return '/';
            if (hasBackslash && !hasSlash)
                return '\\';
        }

        return '\\';
    }

    public static CoreTargetDirectoryNormalizationResult NormalizeRelative(string? value, char separator)
    {
        char safeSeparator = separator is '/' or '\\' ? separator : '\\';
        string original = value ?? string.Empty;
        string raw = original.Trim().Trim('"');

        if (raw.Length == 0)
            return new CoreTargetDirectoryNormalizationResult(true, string.Empty, original.Length != 0, string.Empty);

        if (LooksAbsolute(raw))
        {
            return new CoreTargetDirectoryNormalizationResult(
                false,
                string.Empty,
                false,
                "Bitte nur einen relativen Unterpfad unterhalb des Core-Incoming-Verzeichnisses eingeben.");
        }

        string[] rawParts = raw
            .Replace('\\', safeSeparator)
            .Replace('/', safeSeparator)
            .Split(safeSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (rawParts.Any(part => part is "." or ".."))
        {
            return new CoreTargetDirectoryNormalizationResult(
                false,
                string.Empty,
                false,
                "'.' und '..' sind im Core-Zielpfad nicht zulässig.");
        }

        string manual = CoreTargetPathSanitizer.NormalizeManualSubfolderInput(raw, out bool manualChanged);
        string normalized = CoreTargetPathSanitizer.NormalizeRelativeTargetDirectory(manual, safeSeparator, out bool relativeChanged);

        if (normalized.Length == 0)
        {
            return new CoreTargetDirectoryNormalizationResult(
                false,
                string.Empty,
                true,
                "Der eingegebene Zielpfad enthält nach der Core-kompatiblen Bereinigung keinen gültigen Ordnernamen mehr.");
        }

        bool changed = manualChanged
            || relativeChanged
            || !string.Equals(raw, normalized, StringComparison.Ordinal);

        return new CoreTargetDirectoryNormalizationResult(true, normalized, changed, string.Empty);
    }

    private static bool LooksAbsolute(string value)
    {
        if (value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("\\", StringComparison.Ordinal))
            return true;

        return value.Length >= 2
            && char.IsLetter(value[0])
            && value[1] == ':';
    }
}
