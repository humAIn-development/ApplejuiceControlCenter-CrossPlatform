using System.Reflection;

namespace AJCC.Desktop.Services;

internal static class AppBuildInfo
{
    public static string SemanticVersion
        => GetAssemblyMetadata(
            "SemanticVersion",
            typeof(AppBuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    public static string ReleaseChannel
        => GetAssemblyMetadata("ReleaseChannel", "testbuild");

    public static string BuildCodename
        => GetAssemblyMetadata("BuildCodename", "FirstLight");

    public static string GitBranch
        => GetAssemblyMetadata("GitBranch", "local");

    public static string GitBranchSafe
        => SanitizeBuildToken(GitBranch);

    public static string GitCommit
        => GetAssemblyMetadata("GitCommit", "local");

    public static string GitCommitShort
        => ShortCommit(GitCommit);

    public static string CiRunId
        => GetAssemblyMetadata("CiRunId", "local");

    public static string CiRunNumber
        => GetAssemblyMetadata("CiRunNumber", "local");

    public static string DisplayVersion
        => $"v{SemanticVersion} · {ReleaseChannel} · {BuildCodename} · {GitBranchSafe} · {GitCommitShort}";

    public static string DiagnosticVersion
        => string.Equals(CiRunNumber, "local", StringComparison.OrdinalIgnoreCase)
            ? DisplayVersion
            : $"{DisplayVersion} · CI #{CiRunNumber}";

    private static string GetAssemblyMetadata(string key, string fallback)
    {
        try
        {
            string? value = typeof(AppBuildInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
        catch
        {
            return fallback;
        }
    }

    private static string ShortCommit(string commit)
    {
        if (string.IsNullOrWhiteSpace(commit)
            || string.Equals(commit, "local", StringComparison.OrdinalIgnoreCase))
        {
            return "local";
        }

        string value = commit.Trim();
        return value.Length <= 8 ? value : value[..8];
    }

    private static string SanitizeBuildToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        char[] chars = value.Trim()
            .Select(character =>
                char.IsLetterOrDigit(character)
                || character is '.' or '_' or '-'
                    ? character
                    : '-')
            .ToArray();

        string sanitized = new(chars);
        while (sanitized.Contains("--", StringComparison.Ordinal))
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);

        sanitized = sanitized.Trim('-');
        return sanitized.Length == 0 ? "unknown" : sanitized;
    }
}
