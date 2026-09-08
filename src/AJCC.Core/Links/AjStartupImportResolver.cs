using AJCC.Core.Models;

namespace AJCC.Core.Links;

public sealed class AjStartupImportResolution
{
    public List<AjLinkInfo> Links { get; } = new();
    public List<string> Errors { get; } = new();
}

public static class AjStartupImportResolver
{
    public static AjStartupImportResolution Resolve(AjStartupImportRequest? request)
    {
        AjStartupImportResolution resolution = new();
        if (request is null)
            return resolution;

        foreach (string rawLink in request.Links)
        {
            string candidate = (rawLink ?? string.Empty).Trim();
            if (candidate.Length == 0)
                continue;

            AjLinkInfo parsed = AjLinkParser.Parse(candidate);
            if (parsed.IsValid)
            {
                resolution.Links.Add(parsed);
            }
            else
            {
                resolution.Errors.Add(
                    "AJFSP-Link: " +
                    (string.IsNullOrWhiteSpace(parsed.Error)
                        ? "ungültiger Link"
                        : parsed.Error));
            }
        }

        foreach (string rawPath in request.LinkListFiles)
        {
            string filePath = (rawPath ?? string.Empty).Trim();
            if (filePath.Length == 0)
                continue;

            try
            {
                AjLinkListImportResult parsedFile = AjLinkListParser.ParseFile(filePath);
                resolution.Links.AddRange(parsedFile.Links);
                foreach (string error in parsedFile.Errors)
                    resolution.Errors.Add($"{Path.GetFileName(filePath)}: {error}");
            }
            catch (Exception ex)
            {
                resolution.Errors.Add(
                    $"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        return resolution;
    }
}
