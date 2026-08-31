using System.Text.Json;

namespace AJCC.Desktop.Services;

public sealed record DownloadStatusColorConfiguration
{
    public string CompletedBackground { get; init; } = "#39FF14";
    public string CompletedForeground { get; init; } = "#071407";
    public string AbortedBackground { get; init; } = "#FF2020";
    public string AbortedForeground { get; init; } = "#FFFFFF";
    public string PausedBackground { get; init; } = "#FF77C8";
    public string PausedForeground { get; init; } = "#1A0010";
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
                return new DownloadStatusColorConfiguration();

            string json = File.ReadAllText(_settingsPath);
            DownloadStatusColorConfiguration? configuration =
                JsonSerializer.Deserialize<DownloadStatusColorConfiguration>(json);
            return Normalize(configuration);
        }
        catch
        {
            return new DownloadStatusColorConfiguration();
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
        return new DownloadStatusColorConfiguration
        {
            CompletedBackground = NormalizeValue(configuration?.CompletedBackground, defaults.CompletedBackground),
            CompletedForeground = NormalizeValue(configuration?.CompletedForeground, defaults.CompletedForeground),
            AbortedBackground = NormalizeValue(configuration?.AbortedBackground, defaults.AbortedBackground),
            AbortedForeground = NormalizeValue(configuration?.AbortedForeground, defaults.AbortedForeground),
            PausedBackground = NormalizeValue(configuration?.PausedBackground, defaults.PausedBackground),
            PausedForeground = NormalizeValue(configuration?.PausedForeground, defaults.PausedForeground)
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
