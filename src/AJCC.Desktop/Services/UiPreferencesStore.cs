using System.Text.Json;

namespace AJCC.Desktop.Services;

public sealed record UiPreferences(bool SuppressCoreProfileSwitchConfirmation);

public sealed class UiPreferencesStore
{
    private readonly string _settingsPath;

    public UiPreferencesStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public UiPreferences Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new UiPreferences(false);

            string json = File.ReadAllText(_settingsPath);
            UiPreferences? preferences = JsonSerializer.Deserialize<UiPreferences>(json);
            return preferences ?? new UiPreferences(false);
        }
        catch
        {
            return new UiPreferences(false);
        }
    }

    public bool TrySave(UiPreferences preferences, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        errorMessage = string.Empty;

        try
        {
            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                preferences,
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
        return Path.Combine(root, "AJCC-X", "ui-preferences.json");
    }
}
