using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AJCC.Core.Services;

namespace AJCC.Desktop.Services;

public sealed record DownloadQueueConfiguration(
    int Limit,
    int PreparedLimit,
    Dictionary<string, string>? Priorities = null)
{
    public static DownloadQueueConfiguration Default { get; } =
        new(
            0,
            DownloadQueuePlanningSemantics.DefaultLimit,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public sealed class DownloadQueueConfigurationStore
{
    private readonly string _settingsPath;

    public DownloadQueueConfigurationStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public DownloadQueueConfiguration Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return DownloadQueueConfiguration.Default;

            string json = File.ReadAllText(_settingsPath);
            DownloadQueueConfiguration? configuration =
                JsonSerializer.Deserialize<DownloadQueueConfiguration>(json);
            return configuration is null
                ? DownloadQueueConfiguration.Default
                : Normalize(configuration);
        }
        catch
        {
            return DownloadQueueConfiguration.Default;
        }
    }

    public bool TrySave(DownloadQueueConfiguration configuration, out string errorMessage)
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

    private static DownloadQueueConfiguration Normalize(DownloadQueueConfiguration configuration)
    {
        int limit = configuration.Limit <= 0
            ? 0
            : Math.Clamp(
                configuration.Limit,
                DownloadQueuePlanningSemantics.MinimumLimit,
                DownloadQueuePlanningSemantics.MaximumLimit);

        int preparedLimit = configuration.PreparedLimit <= 0
            ? DownloadQueuePlanningSemantics.DefaultLimit
            : Math.Clamp(
                configuration.PreparedLimit,
                DownloadQueuePlanningSemantics.MinimumLimit,
                DownloadQueuePlanningSemantics.MaximumLimit);

        Dictionary<string, string> priorities = (configuration.Priorities
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new KeyValuePair<string, string>(
                pair.Key.Trim(),
                DownloadQueuePlanningSemantics.NormalizePriority(pair.Value)))
            .Where(pair => pair.Value.Length > 0)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

        return new DownloadQueueConfiguration(limit, preparedLimit, priorities);
    }

    private static string BuildDefaultSettingsPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, "AJCC-X", "download-queue.json");
    }
}
