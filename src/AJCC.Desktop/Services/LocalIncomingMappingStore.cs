using System.Text.Json;
using AJCC.Core.Protocol;

namespace AJCC.Desktop.Services;

public sealed class LocalIncomingMappingStore
{
    private readonly string _settingsPath;

    public LocalIncomingMappingStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public string Get(string endpointText)
    {
        string key = NormalizeEndpointKey(endpointText);
        if (key.Length == 0)
            return string.Empty;

        try
        {
            Dictionary<string, string> mappings = Load();
            return mappings.TryGetValue(key, out string? value) ? value : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public bool TrySave(string endpointText, string? localIncomingMapping, out string errorMessage)
    {
        errorMessage = string.Empty;
        string key = NormalizeEndpointKey(endpointText);
        if (key.Length == 0)
            return true;

        try
        {
            Dictionary<string, string> mappings = Load();
            string mapping = (localIncomingMapping ?? string.Empty).Trim().Trim('"');
            if (mapping.Length == 0)
                mappings.Remove(key);
            else
                mappings[key] = mapping;

            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(mappings, new JsonSerializerOptions { WriteIndented = true });
            AtomicTextFile.WriteAllText(_settingsPath, json);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_settingsPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string json = File.ReadAllText(_settingsPath);
        Dictionary<string, string>? raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return raw is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
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
        return Path.Combine(root, "AJCC-X", "core-incoming-mappings.json");
    }
}
