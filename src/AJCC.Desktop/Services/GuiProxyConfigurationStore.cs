using System.Text.Json;

namespace AJCC.Desktop.Services;

public sealed record GuiProxyConfiguration(
    bool Enabled,
    string Mode,
    string Host,
    int Port,
    string UserName)
{
    public const string PublicMode = "public";
    public const string PrivateMode = "private";

    public static GuiProxyConfiguration CreateDefault()
        => new(false, PublicMode, string.Empty, 0, string.Empty);

    public GuiProxyConfiguration Normalize()
    {
        bool isPrivate = string.Equals(Mode, PrivateMode, StringComparison.OrdinalIgnoreCase);
        return this with
        {
            Mode = isPrivate ? PrivateMode : PublicMode,
            Host = (Host ?? string.Empty).Trim(),
            UserName = isPrivate ? (UserName ?? string.Empty).Trim() : string.Empty
        };
    }
}

public sealed class GuiProxyConfigurationStore
{
    private readonly string _settingsPath;

    public GuiProxyConfigurationStore(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? BuildDefaultSettingsPath()
            : Path.GetFullPath(settingsPath);
    }

    public GuiProxyConfiguration Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return GuiProxyConfiguration.CreateDefault();

            string json = File.ReadAllText(_settingsPath);
            GuiProxyConfiguration? configuration =
                JsonSerializer.Deserialize<GuiProxyConfiguration>(json);
            return (configuration ?? GuiProxyConfiguration.CreateDefault()).Normalize();
        }
        catch
        {
            return GuiProxyConfiguration.CreateDefault();
        }
    }

    public bool TrySave(GuiProxyConfiguration configuration, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        errorMessage = string.Empty;

        GuiProxyConfiguration normalized = configuration.Normalize();
        if (normalized.Port < 0 || normalized.Port > 65535)
        {
            errorMessage = "Proxy-Port muss leer/0 oder ein Wert zwischen 1 und 65535 sein.";
            return false;
        }

        if (normalized.Enabled && string.IsNullOrWhiteSpace(normalized.Host))
        {
            errorMessage = "Wenn Proxy-Nutzung aktiviert ist, muss eine Proxy-Adresse eingetragen sein.";
            return false;
        }

        if (normalized.Enabled
            && string.Equals(normalized.Mode, GuiProxyConfiguration.PrivateMode, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(normalized.UserName))
        {
            errorMessage = "Private Proxy benötigt einen Benutzernamen.";
            return false;
        }

        try
        {
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
        return Path.Combine(root, "AJCC-X", "proxy-settings.json");
    }
}
