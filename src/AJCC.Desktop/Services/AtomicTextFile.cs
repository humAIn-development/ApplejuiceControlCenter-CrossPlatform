using System.Text;

namespace AJCC.Desktop.Services;

internal static class AtomicTextFile
{
    public static void WriteAllText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            LocalDataPermissions.RestrictDirectoryBestEffort(directory);
        }

        string temporaryPath = fullPath + ".tmp";
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                LocalDataPermissions.RestrictFileBestEffort(temporaryPath);
                using StreamWriter writer =
                    new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            LocalDataPermissions.RestrictFileBestEffort(fullPath);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // A stale temp file is harmless and can be overwritten by a later save.
            }

            throw;
        }
    }
}
