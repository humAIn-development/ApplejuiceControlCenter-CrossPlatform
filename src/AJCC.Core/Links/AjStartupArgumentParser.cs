using AJCC.Core.Models;

namespace AJCC.Core.Links;

public static class AjStartupArgumentParser
{
    public static AjStartupImportRequest Parse(string[]? args)
    {
        AjStartupImportRequest request = new();
        if (args == null || args.Length == 0)
            return request;

        foreach (string arg in args)
        {
            string value = NormalizeArgument(arg);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (LooksLikeAjfspLink(value))
            {
                request.Links.Add(value);
                continue;
            }

            if (LooksLikeAjlFile(value))
                request.LinkListFiles.Add(value);
        }

        return request;
    }

    private static string NormalizeArgument(string? arg)
    {
        string value = (arg ?? string.Empty).Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Trim();
        return value;
    }

    private static bool LooksLikeAjfspLink(string value)
        => value.StartsWith("ajfsp://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("ajfsp:%2f%2f", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("ajfsp:%2F%2F", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAjlFile(string value)
    {
        try
        {
            return value.EndsWith(".ajl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(value), ".ajl", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return value.EndsWith(".ajl", StringComparison.OrdinalIgnoreCase);
        }
    }
}
