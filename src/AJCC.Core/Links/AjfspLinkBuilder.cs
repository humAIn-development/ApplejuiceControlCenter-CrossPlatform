namespace AJCC.Core.Links;

public static class AjfspLinkBuilder
{
    private const string PlainSeparator = "|";
    private const string EncodedSeparator = "%7C";

    public static string BuildFileLink(string filename, string checksum, long size)
        => BuildFileLink(filename, checksum, size, source: null);

    public static string BuildFileLink(string filename, string checksum, long size, string? source)
        => BuildFileLinkCore(filename, checksum, size, source, PlainSeparator);

    public static string BuildFileUri(string filename, string checksum, long size)
        => BuildFileUri(filename, checksum, size, source: null);

    public static string BuildFileUri(string filename, string checksum, long size, string? source)
        => BuildFileLinkCore(filename, checksum, size, source, EncodedSeparator);

    private static string BuildFileLinkCore(string filename, string checksum, long size, string? source, string separator)
    {
        string normalizedFilename = (filename ?? string.Empty).Trim();
        string normalizedChecksum = (checksum ?? string.Empty).Trim();
        string normalizedSource = (source ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedSource))
            return $"ajfsp://file{separator}{normalizedFilename}{separator}{normalizedChecksum}{separator}{size}/";

        return $"ajfsp://file{separator}{normalizedFilename}{separator}{normalizedChecksum}{separator}{size}{separator}{normalizedSource}/";
    }
}
