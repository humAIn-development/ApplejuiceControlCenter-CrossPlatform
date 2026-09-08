using System.Globalization;
using AJCC.Core.Models;

namespace AJCC.Core.Protocol;

public sealed class AjSettingsOverrides
{
    public string? Nick { get; init; }
    public int? Port { get; init; }
    public int? XmlPort { get; init; }
    public long? MaxUpload { get; init; }
    public long? MaxDownload { get; init; }
    public int? MaxConnections { get; init; }
    public bool? AutoConnect { get; init; }
    public int? MaxSourcesPerFile { get; init; }
    public int? SpeedPerSlot { get; init; }
    public string? IncomingDirectory { get; init; }
    public string? TemporaryDirectory { get; init; }
    public int? MaxNewConnectionsPerTurn { get; init; }
}

public static class AjSettingsParameters
{
    public static IReadOnlyDictionary<string, string> BuildComplete(
        AjSettings settings,
        AjSettingsOverrides? overrides = null,
        IEnumerable<AjShareDirectory>? shareDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nick"] = overrides?.Nick ?? settings.Nick ?? string.Empty,
            ["port"] = Math.Max(0, overrides?.Port ?? settings.Port).ToString(CultureInfo.InvariantCulture),
            ["XMLPort"] = Math.Max(0, overrides?.XmlPort ?? settings.XmlPort).ToString(CultureInfo.InvariantCulture),
            ["maxconnections"] = Math.Max(0, overrides?.MaxConnections ?? settings.MaxConnections).ToString(CultureInfo.InvariantCulture),
            ["maxupload"] = Math.Max(0L, overrides?.MaxUpload ?? settings.MaxUpload).ToString(CultureInfo.InvariantCulture),
            ["speedperslot"] = Math.Max(0, overrides?.SpeedPerSlot ?? settings.SpeedPerSlot).ToString(CultureInfo.InvariantCulture),
            ["maxdownload"] = Math.Max(0L, overrides?.MaxDownload ?? settings.MaxDownload).ToString(CultureInfo.InvariantCulture),
            ["maxnewconnectionsperturn"] = Math.Max(0, overrides?.MaxNewConnectionsPerTurn ?? settings.MaxNewConnectionsPerTurn).ToString(CultureInfo.InvariantCulture),
            ["maxsourcesperfile"] = Math.Max(0, overrides?.MaxSourcesPerFile ?? settings.MaxSourcesPerFile).ToString(CultureInfo.InvariantCulture),
            ["autoconnect"] = (overrides?.AutoConnect ?? settings.AutoConnect) ? "true" : "false",
            ["incomingdirectory"] = overrides?.IncomingDirectory ?? settings.IncomingDirectory ?? string.Empty,
            ["temporarydirectory"] = overrides?.TemporaryDirectory ?? settings.TemporaryDirectory ?? string.Empty
        };

        List<AjShareDirectory> directories = (shareDirectories ?? settings.SharedDirectories)
            .Where(directory => !string.IsNullOrWhiteSpace(directory.Name))
            .ToList();

        for (int index = 0; index < directories.Count; index++)
        {
            AjShareDirectory directory = directories[index];
            int slot = index + 1;
            parameters[$"sharedirectory{slot}"] = directory.Name;
            parameters[$"sharesub{slot}"] = directory.ShareMode.Equals("subdirectory", StringComparison.OrdinalIgnoreCase)
                ? "true"
                : "false";
        }

        parameters["countshares"] = directories.Count.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }
}
