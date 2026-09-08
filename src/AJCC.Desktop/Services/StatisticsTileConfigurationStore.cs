using System.Text.Json;

namespace AJCC.Desktop.Services;

public sealed record StatisticsTileDefinition(
    string Key,
    string Title,
    string Description);

public static class StatisticsTileCatalog
{
    public const int MinimumVisibleTiles = 1;
    public const int MaximumVisibleTiles = 8;

    public static IReadOnlyList<StatisticsTileDefinition> Definitions { get; } =
        new StatisticsTileDefinition[]
        {
            new("connection", "Verbindung", "Core-Verbindung, Server und Verbindungszustand."),
            new("transfer", "Transfer", "Download-/Uploadrate und aktive Downloads."),
            new("activity", "Aktivität", "Statuszusammenfassung für Downloads, Uploads, Quellen und Shares."),
            new("session", "Session", "Sessionvolumen, Credits und Verhältnis."),
            new("network", "Netzwerk", "Offene Verbindungen und Netzwerkuser aus Core-Sicht."),
            new("core", "Core", "Coreversion, Aktualisierung und Systemverteilung."),
            new("gui", "GUI-System", "Lokales Betriebssystem und Arbeitsspeicher des GUI-Prozesses."),
            new("health", "Zustand", "Kuratierter Gesamtzustand der aktuellen GUI-/Core-Sicht."),
            new("downloads", "Downloads", "Downloadzahlen, Statusgruppen und Volumen."),
            new("uploads", "Uploads", "Uploadzahlen, aktive Uploads und Geschwindigkeit."),
            new("sources", "Quellen", "Sichtbare Quellen und Warteschlangen-/Aktivstatus."),
            new("shares", "Shares", "Anzahl und Gesamtgröße der eigenen Shares."),
            new("networksize", "Netzwerkgröße", "Netzwerkweite Dateianzahl und Größe, soweit der Core sie liefert."),
            new("guiruntime", "GUI-Laufzeit", "Laufzeit dieser GUI-Sitzung und Runtime-Version."),
            new("guidisplay", "Anzeige / DPI", "Lokale Bildschirmauflösung und Skalierung für Layoutdiagnose."),
            new("guicpu", "GUI-CPU", "Geschätzte CPU-Last des GUI-Prozesses."),
            new("guimemory", "GUI-Speicherlast", "Arbeitsspeicher, GC-Speicher und System-Memory-Pressure."),
            new("guiprocess", "GUI-Prozess", "Threads, Handles und privater Prozessspeicher."),
            new("history", "Live-Historie", "Lokale Kurzzeithistorie für Graphen/Samples.")
        };

    public static string[] DefaultSelectedKeys { get; } =
        Definitions
            .Take(MaximumVisibleTiles)
            .Select(static definition => definition.Key)
            .ToArray();

    public static string[] NormalizeSelection(IEnumerable<string>? keys)
    {
        HashSet<string> requested = new(
            (keys ?? Array.Empty<string>())
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Select(static key => key.Trim()),
            StringComparer.OrdinalIgnoreCase);

        string[] normalized = Definitions
            .Where(definition => requested.Contains(definition.Key))
            .Select(static definition => definition.Key)
            .Take(MaximumVisibleTiles)
            .ToArray();

        return normalized.Length >= MinimumVisibleTiles
            ? normalized
            : DefaultSelectedKeys.ToArray();
    }

    public static StatisticsTileDefinition? Find(string? key)
        => Definitions.FirstOrDefault(
            definition => string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed record StatisticsTileConfiguration(string[] SelectedKeys)
{
    public static StatisticsTileConfiguration Default =>
        new(StatisticsTileCatalog.DefaultSelectedKeys.ToArray());
}

public sealed class StatisticsTileConfigurationStore
{
    private readonly string _settingsPath;

    public StatisticsTileConfigurationStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public StatisticsTileConfiguration Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return StatisticsTileConfiguration.Default;

            string json = File.ReadAllText(_settingsPath);
            StatisticsTileConfiguration? configuration =
                JsonSerializer.Deserialize<StatisticsTileConfiguration>(json);
            return new StatisticsTileConfiguration(
                StatisticsTileCatalog.NormalizeSelection(configuration?.SelectedKeys));
        }
        catch
        {
            return StatisticsTileConfiguration.Default;
        }
    }

    public bool TrySave(StatisticsTileConfiguration configuration, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        errorMessage = string.Empty;

        try
        {
            StatisticsTileConfiguration normalized = new(
                StatisticsTileCatalog.NormalizeSelection(configuration.SelectedKeys));
            string json = JsonSerializer.Serialize(
                normalized,
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

    private static string BuildDefaultSettingsPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, "AJCC-X", "statistics-tiles.json");
    }
}
