using System.Text.Json;

namespace AJCC.Desktop.Services;

public sealed record UiPreferences(
    bool SuppressCoreProfileSwitchConfirmation,
    bool AutoLoadShareFilesAtStartup = false)
{
    public bool GuiSoundsEnabled { get; init; } = true;
    public string? DownloadSortColumn { get; init; }
    public bool DownloadSortDescending { get; init; }
}

public sealed class UiPreferencesStore
{
    private readonly string _settingsPath;
    private readonly string _startupShareLoadMarkerPath;

    public UiPreferencesStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
        string? directory = Path.GetDirectoryName(_settingsPath);
        _startupShareLoadMarkerPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory,
            "startup-share-load.in-progress");
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
            UiPreferences preferencesToSave = preferences;
            if (preferences.DownloadSortColumn is null)
            {
                UiPreferences existingPreferences = Load();
                if (!string.IsNullOrWhiteSpace(existingPreferences.DownloadSortColumn))
                {
                    preferencesToSave = preferences with
                    {
                        DownloadSortColumn = existingPreferences.DownloadSortColumn,
                        DownloadSortDescending = existingPreferences.DownloadSortDescending
                    };
                }
            }

            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                preferencesToSave,
                new JsonSerializerOptions { WriteIndented = true });
            AtomicTextFile.WriteAllText(_settingsPath, json);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool HasStartupShareLoadMarker()
    {
        try
        {
            return File.Exists(_startupShareLoadMarkerPath);
        }
        catch
        {
            return false;
        }
    }

    public void MarkStartupShareLoadInProgress()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_startupShareLoadMarkerPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_startupShareLoadMarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            // Der Schutzmarker darf den normalen Programmstart niemals verhindern.
        }
    }

    public void ClearStartupShareLoadMarker()
    {
        try
        {
            if (File.Exists(_startupShareLoadMarkerPath))
                File.Delete(_startupShareLoadMarkerPath);
        }
        catch
        {
            // Ein alter Marker bleibt absichtlich harmlos und aktiviert nur erneut den Safe-Start.
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
