namespace AJCC.Desktop.Services;

internal static class LocalDataPermissions
{
    private const UnixFileMode PrivateFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static void RestrictFileBestEffort(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, PrivateFileMode);
        }
        catch
        {
            // Privacy hardening must not make local persistence unusable on unusual filesystems.
        }
    }

    public static void RestrictDirectoryBestEffort(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, PrivateDirectoryMode);
        }
        catch
        {
            // Privacy hardening must not make local persistence unusable on unusual filesystems.
        }
    }
}
