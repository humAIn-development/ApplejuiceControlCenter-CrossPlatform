using System.Text.Json;
using AJCC.Core.Protocol;
using AJCC.Core.Services;

namespace AJCC.Desktop.Services;

public sealed class ServerReconnectRestrictionStore
{
    private readonly string _settingsPath;

    public ServerReconnectRestrictionStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public bool TryLoad(
        string endpointText,
        out ServerReconnectRestrictionSnapshot snapshot,
        out string errorMessage)
    {
        snapshot = default;
        errorMessage = string.Empty;
        string key = NormalizeEndpointKey(endpointText);
        if (key.Length == 0)
            return true;

        try
        {
            Dictionary<string, StoredSnapshot> snapshots = Load();
            if (snapshots.TryGetValue(key, out StoredSnapshot? stored) && stored is not null)
            {
                snapshot = new ServerReconnectRestrictionSnapshot(
                    stored.UntilUtc,
                    stored.HasExactCountdown,
                    stored.TargetServerId);
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TrySave(
        string endpointText,
        ServerReconnectRestrictionSnapshot snapshot,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        string key = NormalizeEndpointKey(endpointText);
        if (key.Length == 0)
            return true;

        try
        {
            Dictionary<string, StoredSnapshot> snapshots = Load();
            if (snapshot.IsMarked)
            {
                snapshots[key] = new StoredSnapshot
                {
                    UntilUtc = snapshot.UntilUtc,
                    HasExactCountdown = snapshot.HasExactCountdown,
                    TargetServerId = snapshot.TargetServerId
                };
            }
            else
            {
                snapshots.Remove(key);
            }

            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                snapshots,
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

    private Dictionary<string, StoredSnapshot> Load()
    {
        if (!File.Exists(_settingsPath))
            return new Dictionary<string, StoredSnapshot>(StringComparer.Ordinal);

        string json = File.ReadAllText(_settingsPath);
        Dictionary<string, StoredSnapshot>? raw =
            JsonSerializer.Deserialize<Dictionary<string, StoredSnapshot>>(json);
        return raw is null
            ? new Dictionary<string, StoredSnapshot>(StringComparer.Ordinal)
            : new Dictionary<string, StoredSnapshot>(raw, StringComparer.Ordinal);
    }

    private static string NormalizeEndpointKey(string? endpointText)
    {
        string text = (endpointText ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;

        try
        {
            return CoreEndpoint.Parse(text).BaseUri.AbsoluteUri;
        }
        catch
        {
            return text;
        }
    }

    private static string BuildDefaultSettingsPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, "AJCC-X", "server-reconnect-restrictions.json");
    }

    private sealed class StoredSnapshot
    {
        public DateTimeOffset UntilUtc { get; set; }
        public bool HasExactCountdown { get; set; }
        public long TargetServerId { get; set; }
    }
}
