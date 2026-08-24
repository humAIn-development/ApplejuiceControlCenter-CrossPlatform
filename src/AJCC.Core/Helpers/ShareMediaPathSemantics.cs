namespace AJCC.Core.Helpers;

public static class ShareMediaPathSemantics
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".webm",
        ".mpg", ".mpeg", ".ts", ".m2ts", ".ogv", ".flv", ".3gp",
        ".mp3", ".flac", ".ogg", ".opus", ".wav", ".m4a", ".aac", ".wma"
    };

    public static bool IsPlausibleMediaFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string value = fileName.Trim();
        int slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        string name = slash >= 0 ? value[(slash + 1)..] : value;
        int dot = name.LastIndexOf('.');
        return dot >= 0 && MediaExtensions.Contains(name[dot..]);
    }

    public static bool TryGetRelativePathBelowIncoming(
        string? coreIncomingDirectory,
        string? coreFilePath,
        out string relativePath)
    {
        relativePath = string.Empty;
        string incoming = Normalize(coreIncomingDirectory);
        string file = Normalize(coreFilePath);
        if (incoming.Length == 0 || file.Length == 0)
            return false;

        incoming = incoming.TrimEnd('/');
        string prefix = incoming + "/";
        if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string remainder = file[prefix.Length..];
        string[] parts = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
            return false;

        relativePath = string.Join('/', parts);
        return true;
    }

    private static string Normalize(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized;
    }
}
