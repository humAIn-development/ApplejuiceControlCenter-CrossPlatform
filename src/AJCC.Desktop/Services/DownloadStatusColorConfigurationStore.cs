using System.Text.Json;

namespace AJCC.Desktop.Services;

public sealed record DownloadStatusColorRule(
    int Status,
    string Label,
    bool Enabled,
    string Background,
    string Foreground);

public sealed record DownloadStatusColorConfiguration
{
    public bool CompletedEnabled { get; init; } = true;
    public string CompletedBackground { get; init; } = "#39FF14";
    public string CompletedForeground { get; init; } = "#071407";

    public bool AbortedEnabled { get; init; } = true;
    public string AbortedBackground { get; init; } = "#FF2020";
    public string AbortedForeground { get; init; } = "#FFFFFF";

    public bool PausedEnabled { get; init; } = true;
    public string PausedBackground { get; init; } = "#FF77C8";
    public string PausedForeground { get; init; } = "#1A0010";

    public bool OtherEnabled { get; init; } = false;
    public string OtherBackground { get; init; } = "#00000000";
    public string OtherForeground { get; init; } = "#FFFFFF";

    public List<DownloadStatusColorRule> Rules { get; init; } = new();

    public DownloadStatusColorRule GetRule(int status)
    {
        DownloadStatusColorRule? exact = Rules.FirstOrDefault(rule => rule.Status == status);
        if (exact is not null)
            return exact;

        return Rules.FirstOrDefault(rule => rule.Status == -1)
            ?? CreateDefaultRules().First(rule => rule.Status == -1);
    }

    public static List<DownloadStatusColorRule> CreateDefaultRules()
        => new()
        {
            new DownloadStatusColorRule(0, "Suchen/Laden", false, "#00000000", "#FFFFFF"),
            new DownloadStatusColorRule(1, "Plattenfehler", false, "#00000000", "#FFFFFF"),
            new DownloadStatusColorRule(12, "Fertigstellen", false, "#00000000", "#FFFFFF"),
            new DownloadStatusColorRule(13, "Fehler beim Fertigstellen", false, "#00000000", "#FFFFFF"),
            new DownloadStatusColorRule(14, "Fertig", true, "#39FF14", "#071407"),
            new DownloadStatusColorRule(15, "Abbrechen", true, "#FF2020", "#FFFFFF"),
            new DownloadStatusColorRule(16, ".data wird erstellt", false, "#00000000", "#FFFFFF"),
            new DownloadStatusColorRule(17, "Abgebrochen", true, "#FF2020", "#FFFFFF"),
            new DownloadStatusColorRule(18, "Pausiert", true, "#FF77C8", "#1A0010"),
            new DownloadStatusColorRule(-1, "Unbekannt / sonstige", false, "#00000000", "#FFFFFF")
        };
}

public sealed class DownloadStatusColorConfigurationStore
{
    private readonly string _settingsPath;

    public DownloadStatusColorConfigurationStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public DownloadStatusColorConfiguration Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return Normalize(null);

            string json = File.ReadAllText(_settingsPath);
            DownloadStatusColorConfiguration? configuration =
                JsonSerializer.Deserialize<DownloadStatusColorConfiguration>(json);
            return Normalize(configuration);
        }
        catch
        {
            return Normalize(null);
        }
    }

    public bool TrySave(DownloadStatusColorConfiguration configuration, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        errorMessage = string.Empty;

        try
        {
            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                Normalize(configuration),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static DownloadStatusColorConfiguration Normalize(DownloadStatusColorConfiguration? configuration)
    {
        DownloadStatusColorConfiguration defaults = new();
        bool hasStoredRules = configuration?.Rules is { Count: > 0 };

        bool completedEnabled = configuration?.CompletedEnabled ?? defaults.CompletedEnabled;
        string completedBackground = NormalizeValue(configuration?.CompletedBackground, defaults.CompletedBackground);
        string completedForeground = NormalizeValue(configuration?.CompletedForeground, defaults.CompletedForeground);

        bool abortedEnabled = configuration?.AbortedEnabled ?? defaults.AbortedEnabled;
        string abortedBackground = NormalizeValue(configuration?.AbortedBackground, defaults.AbortedBackground);
        string abortedForeground = NormalizeValue(configuration?.AbortedForeground, defaults.AbortedForeground);

        bool pausedEnabled = configuration?.PausedEnabled ?? defaults.PausedEnabled;
        string pausedBackground = NormalizeValue(configuration?.PausedBackground, defaults.PausedBackground);
        string pausedForeground = NormalizeValue(configuration?.PausedForeground, defaults.PausedForeground);

        bool otherEnabled = configuration?.OtherEnabled ?? defaults.OtherEnabled;
        string otherBackground = NormalizeValue(configuration?.OtherBackground, defaults.OtherBackground);
        string otherForeground = NormalizeValue(configuration?.OtherForeground, defaults.OtherForeground);

        List<DownloadStatusColorRule> normalizedRules = new();
        foreach (DownloadStatusColorRule defaultRule in DownloadStatusColorConfiguration.CreateDefaultRules())
        {
            DownloadStatusColorRule? stored = hasStoredRules
                ? configuration!.Rules.FirstOrDefault(rule => rule.Status == defaultRule.Status)
                : null;

            DownloadStatusColorRule normalized = stored is null
                ? defaultRule
                : new DownloadStatusColorRule(
                    defaultRule.Status,
                    string.IsNullOrWhiteSpace(stored.Label) ? defaultRule.Label : stored.Label.Trim(),
                    stored.Enabled,
                    NormalizeValue(stored.Background, defaultRule.Background),
                    NormalizeValue(stored.Foreground, defaultRule.Foreground));

            if (!hasStoredRules)
            {
                normalized = defaultRule.Status switch
                {
                    14 => normalized with
                    {
                        Enabled = completedEnabled,
                        Background = completedBackground,
                        Foreground = completedForeground
                    },
                    15 or 17 => normalized with
                    {
                        Enabled = abortedEnabled,
                        Background = abortedBackground,
                        Foreground = abortedForeground
                    },
                    18 => normalized with
                    {
                        Enabled = pausedEnabled,
                        Background = pausedBackground,
                        Foreground = pausedForeground
                    },
                    -1 => normalized with
                    {
                        Enabled = otherEnabled,
                        Background = otherBackground,
                        Foreground = otherForeground
                    },
                    _ => normalized
                };
            }

            if (normalized.Status == 18
                && !normalized.Enabled
                && string.Equals(normalized.Background, "#00000000", StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalized.Foreground, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
            {
                normalized = defaultRule;
            }

            normalizedRules.Add(normalized);
        }

        DownloadStatusColorRule completed = normalizedRules.First(rule => rule.Status == 14);
        DownloadStatusColorRule aborted = normalizedRules.First(rule => rule.Status == 17);
        DownloadStatusColorRule paused = normalizedRules.First(rule => rule.Status == 18);
        DownloadStatusColorRule other = normalizedRules.First(rule => rule.Status == -1);

        return new DownloadStatusColorConfiguration
        {
            CompletedEnabled = completed.Enabled,
            CompletedBackground = completed.Background,
            CompletedForeground = completed.Foreground,
            AbortedEnabled = aborted.Enabled,
            AbortedBackground = aborted.Background,
            AbortedForeground = aborted.Foreground,
            PausedEnabled = paused.Enabled,
            PausedBackground = paused.Background,
            PausedForeground = paused.Foreground,
            OtherEnabled = other.Enabled,
            OtherBackground = other.Background,
            OtherForeground = other.Foreground,
            Rules = normalizedRules
        };
    }

    private static string NormalizeValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string BuildDefaultSettingsPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, "AJCC-X", "download-status-colors.json");
    }
}
