using System.Globalization;

namespace AJCC.Core.Protocol;

public static class AppleJuiceCoreClientDownloadExtensions
{
    public static Task<string> CancelDownloadAsync(
        this AppleJuiceCoreClient client,
        long id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.GetXmlAsync(
            AjEndpoints.CancelDownload,
            new Dictionary<string, string>
            {
                ["id"] = id.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);
    }
}
