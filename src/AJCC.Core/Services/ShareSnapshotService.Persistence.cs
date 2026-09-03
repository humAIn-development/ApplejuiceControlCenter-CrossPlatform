using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AJCC.Core.Models;

namespace AJCC.Core.Services;

public static partial class ShareSnapshotService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false,
        IgnoreReadOnlyProperties = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<ShareSnapshotLoadResult> LoadAsync(
        string coreHost,
        int corePort,
        string? storageRoot = null)
    {
        string storagePath = GetStoragePath(coreHost, corePort, storageRoot);
        if (!File.Exists(storagePath))
        {
            return new ShareSnapshotLoadResult
            {
                StoragePath = storagePath
            };
        }

        try
        {
            await using FileStream fileStream =
                new(storagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using GZipStream gzipStream =
                new(fileStream, CompressionMode.Decompress, leaveOpen: false);

            ShareSnapshotDocument? snapshot =
                await JsonSerializer.DeserializeAsync<ShareSnapshotDocument>(
                    gzipStream,
                    SnapshotJsonOptions);

            if (snapshot is null)
                throw new InvalidDataException("Die gespeicherte Vergleichsbasis ist leer.");
            if (snapshot.FormatVersion != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"Nicht unterstütztes Snapshot-Format {snapshot.FormatVersion}.");
            }

            snapshot.Files ??= new List<ShareSnapshotFileEntry>();
            snapshot.Roots ??= new List<ShareSnapshotRootEntry>();

            return new ShareSnapshotLoadResult
            {
                Snapshot = snapshot,
                StoragePath = storagePath
            };
        }
        catch (Exception ex)
        {
            return new ShareSnapshotLoadResult
            {
                ErrorMessage = ex.Message,
                StoragePath = storagePath
            };
        }
    }

    public static async Task SaveAsync(
        ShareSnapshotDocument snapshot,
        string? storageRoot = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string storagePath =
            GetStoragePath(snapshot.CoreHost, snapshot.CorePort, storageRoot);
        string? folder = Path.GetDirectoryName(storagePath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException(
                "Der lokale Snapshot-Ordner konnte nicht bestimmt werden.");
        }

        Directory.CreateDirectory(folder);
        string temporaryPath = storagePath + ".tmp";

        try
        {
            await using (FileStream fileStream =
                new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (GZipStream gzipStream =
                new(fileStream, CompressionLevel.Optimal, leaveOpen: false))
            {
                RestrictSnapshotFileBestEffort(temporaryPath);
                await JsonSerializer.SerializeAsync(
                    gzipStream,
                    snapshot,
                    SnapshotJsonOptions);
            }

            File.Move(temporaryPath, storagePath, overwrite: true);
            RestrictSnapshotFileBestEffort(storagePath);
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
                // Eine liegengebliebene temporäre Datei kann beim nächsten Speichern
                // gefahrlos überschrieben werden.
            }

            throw;
        }
    }

    public static string GetStoragePath(
        string coreHost,
        int corePort,
        string? storageRoot = null)
    {
        string folder = string.IsNullOrWhiteSpace(storageRoot)
            ? BuildDefaultSnapshotFolder()
            : Path.GetFullPath(storageRoot);

        string identity = $"{NormalizeCoreHost(coreHost)}:{corePort}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string token = Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant();
        return Path.Combine(folder, $"share-snapshot-{token}.json.gz");
    }

    private static string BuildDefaultSnapshotFolder()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(root, "AJCC-X", "share-snapshots");
    }

    private static void RestrictSnapshotFileBestEffort(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Privacy hardening must not make snapshot persistence unusable
            // on unusual filesystems.
        }
    }

    private static string NormalizeCoreHost(string? coreHost)
        => string.IsNullOrWhiteSpace(coreHost)
            ? "127.0.0.1"
            : coreHost.Trim().ToLowerInvariant();
}
