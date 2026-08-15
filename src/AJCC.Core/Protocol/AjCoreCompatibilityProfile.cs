using System.Text.RegularExpressions;

namespace AJCC.Core.Protocol;

public enum AjProcessLinkStrategy
{
    MinimalLinkOnly,
    LinkWithOptionalSubdir
}

public sealed class AjCoreCompatibilityProfile
{
    private static readonly Version OptionalSubdirIntroducedVersion = new(0, 31, 149, 0);

    public string CoreVersionText { get; }
    public Version? ParsedVersion { get; }
    public AjProcessLinkStrategy ProcessLinkStrategy { get; }
    public string ProcessLinkStrategyText => ProcessLinkStrategy == AjProcessLinkStrategy.LinkWithOptionalSubdir
        ? "processlink: link + optional subdir"
        : "processlink: link-only/minimal";

    public bool SupportsProcessLinkSubdir => ProcessLinkStrategy == AjProcessLinkStrategy.LinkWithOptionalSubdir;

    private AjCoreCompatibilityProfile(string coreVersionText, Version? parsedVersion, AjProcessLinkStrategy processLinkStrategy)
    {
        CoreVersionText = coreVersionText?.Trim() ?? string.Empty;
        ParsedVersion = parsedVersion;
        ProcessLinkStrategy = processLinkStrategy;
    }

    public static AjCoreCompatibilityProfile FromCoreVersion(string? coreVersionText)
    {
        string text = coreVersionText?.Trim() ?? string.Empty;
        Version? version = TryParseVersion(text);

        if (version is null || version < OptionalSubdirIntroducedVersion)
            return new AjCoreCompatibilityProfile(text, version, AjProcessLinkStrategy.MinimalLinkOnly);

        return new AjCoreCompatibilityProfile(text, version, AjProcessLinkStrategy.LinkWithOptionalSubdir);
    }

    private static Version? TryParseVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match match = Regex.Match(text, @"(?<version>\d+(?:\.\d+){1,3})");
        if (!match.Success)
            return null;

        string[] parts = match.Groups["version"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        int[] values = new int[4];
        for (int index = 0; index < Math.Min(parts.Length, values.Length); index++)
        {
            if (!int.TryParse(parts[index], out values[index]))
                return null;
        }

        return new Version(values[0], values[1], values[2], values[3]);
    }
}
