using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using AJCC.Core.Protocol;

namespace AJCC.Desktop.Services;

public enum CoreProfileReachabilityStatus
{
    Unknown,
    Checking,
    Reachable,
    Unreachable
}

public sealed class CoreProfileEntry : INotifyPropertyChanged
{
    private CoreProfileReachabilityStatus _reachabilityStatus;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Core";
    public string Endpoint { get; set; } = "http://127.0.0.1:9851/";

    [JsonIgnore]
    public string ReachabilityLabel => _reachabilityStatus switch
    {
        CoreProfileReachabilityStatus.Checking => "…",
        CoreProfileReachabilityStatus.Reachable => "✓",
        CoreProfileReachabilityStatus.Unreachable => "×",
        _ => "?"
    };

    [JsonIgnore]
    public string ReachabilityText => _reachabilityStatus switch
    {
        CoreProfileReachabilityStatus.Checking => "TCP-Erreichbarkeit wird geprüft",
        CoreProfileReachabilityStatus.Reachable => "TCP-Endpunkt erreichbar",
        CoreProfileReachabilityStatus.Unreachable => "TCP-Endpunkt nicht erreichbar",
        _ => "TCP-Erreichbarkeit noch nicht geprüft"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetReachabilityStatus(CoreProfileReachabilityStatus status)
    {
        if (_reachabilityStatus == status)
            return;

        _reachabilityStatus = status;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReachabilityLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReachabilityText)));
    }

    public override string ToString() => Name;
}

public sealed class CoreProfileStoreSnapshot
{
    public List<CoreProfileEntry> Profiles { get; set; } = new();
    public string DefaultProfileId { get; set; } = string.Empty;
}

public sealed class CoreProfileStore
{
    private readonly string _settingsPath;

    public CoreProfileStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public CoreProfileStoreSnapshot Load()
    {
        if (!File.Exists(_settingsPath))
            return new CoreProfileStoreSnapshot();

        try
        {
            string json = File.ReadAllText(_settingsPath);
            CoreProfileStoreSnapshot? raw = JsonSerializer.Deserialize<CoreProfileStoreSnapshot>(json);
            return Normalize(raw ?? new CoreProfileStoreSnapshot());
        }
        catch
        {
            return new CoreProfileStoreSnapshot();
        }
    }

    public bool TrySave(
        IEnumerable<CoreProfileEntry> profiles,
        string? defaultProfileId,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            CoreProfileStoreSnapshot snapshot = Normalize(new CoreProfileStoreSnapshot
            {
                Profiles = profiles?.Select(Clone).ToList() ?? new List<CoreProfileEntry>(),
                DefaultProfileId = defaultProfileId?.Trim() ?? string.Empty
            });

            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                snapshot,
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

    public static string NormalizeEndpoint(string endpointText)
        => CoreEndpoint.Parse(endpointText).BaseUri.AbsoluteUri;

    public static string TryNormalizeEndpoint(string? endpointText)
    {
        try
        {
            return NormalizeEndpoint(endpointText ?? string.Empty);
        }
        catch
        {
            return (endpointText ?? string.Empty).Trim();
        }
    }

    private static CoreProfileStoreSnapshot Normalize(CoreProfileStoreSnapshot snapshot)
    {
        List<CoreProfileEntry> normalized = new();
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedEndpoints = new(StringComparer.OrdinalIgnoreCase);

        foreach (CoreProfileEntry? profile in snapshot.Profiles ?? new List<CoreProfileEntry>())
        {
            if (profile is null)
                continue;

            string endpoint;
            try
            {
                endpoint = NormalizeEndpoint(profile.Endpoint);
            }
            catch
            {
                continue;
            }

            if (!usedEndpoints.Add(endpoint))
                continue;

            string id = (profile.Id ?? string.Empty).Trim();
            if (id.Length == 0 || !usedIds.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                usedIds.Add(id);
            }

            string name = (profile.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                name = CoreEndpoint.Parse(endpoint).Host;

            normalized.Add(new CoreProfileEntry
            {
                Id = id,
                Name = name,
                Endpoint = endpoint
            });
        }

        string defaultProfileId = (snapshot.DefaultProfileId ?? string.Empty).Trim();
        if (normalized.Count > 0
            && normalized.All(profile =>
                !string.Equals(profile.Id, defaultProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            defaultProfileId = normalized[0].Id;
        }

        return new CoreProfileStoreSnapshot
        {
            Profiles = normalized,
            DefaultProfileId = defaultProfileId
        };
    }

    private static CoreProfileEntry Clone(CoreProfileEntry profile)
        => new()
        {
            Id = profile.Id,
            Name = profile.Name,
            Endpoint = profile.Endpoint
        };

    private static string BuildDefaultSettingsPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(root, "AJCC-X", "core-profiles.json");
    }
}
