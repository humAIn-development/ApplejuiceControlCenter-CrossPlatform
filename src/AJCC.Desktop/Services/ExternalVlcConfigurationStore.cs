using System.Text.Json;

namespace AJCC.Desktop.Services;

public sealed record ExternalVlcConfiguration(bool Enabled, string ExecutablePath);

public sealed class ExternalVlcConfigurationStore
{
    private readonly string _settingsPath;

    public ExternalVlcConfigurationStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public ExternalVlcConfiguration Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new ExternalVlcConfiguration(false, string.Empty);

            string json = File.ReadAllText(_settingsPath);
            ExternalVlcConfiguration? configuration =
                JsonSerializer.Deserialize<ExternalVlcConfiguration>(json);
            return configuration ?? new ExternalVlcConfiguration(false, string.Empty);
        }
        catch
        {
            return new ExternalVlcConfiguration(false, string.Empty);
        }
    }

    public bool TrySave(ExternalVlcConfiguration configuration, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        errorMessage = string.Empty;

        try
        {
            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            ExternalVlcConfiguration normalized = configuration with
            {
                ExecutablePath = (configuration.ExecutablePath ?? string.Empty).Trim().Trim('"')
            };
            string json = JsonSerializer.Serialize(
                normalized,
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

    private static string BuildDefaultSettingsPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, "AJCC-X", "external-vlc.json");
    }
}
