namespace AJCC.Desktop.Platform;

public sealed record HostIncomingDirectoryPreparationResult(
    bool Success,
    string LocalTargetPath,
    string ErrorMessage);

public sealed class HostIncomingDirectoryPreparer
{
    public HostIncomingDirectoryPreparationResult Prepare(string? localIncomingMapping, string coreRelativeTarget)
    {
        string mapping = (localIncomingMapping ?? string.Empty).Trim().Trim('"');
        if (mapping.Length == 0)
        {
            return new HostIncomingDirectoryPreparationResult(
                false,
                string.Empty,
                "Für einen neuen tiefen Zielpfad ist ein lokales/gemountetes Incoming-Mapping erforderlich.");
        }

        if (!Directory.Exists(mapping))
        {
            return new HostIncomingDirectoryPreparationResult(
                false,
                string.Empty,
                $"Das lokale Incoming-Mapping ist nicht erreichbar: {mapping}");
        }

        string[] parts = (coreRelativeTarget ?? string.Empty)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Any(part => part is "." or ".."))
        {
            return new HostIncomingDirectoryPreparationResult(
                false,
                string.Empty,
                "Der Zielpfad enthält eine unzulässige Navigationsebene.");
        }

        try
        {
            string root = Path.GetFullPath(mapping);
            string target = root;
            foreach (string part in parts)
                target = Path.Combine(target, part);

            target = Path.GetFullPath(target);
            if (!IsAtOrBelow(root, target))
            {
                return new HostIncomingDirectoryPreparationResult(
                    false,
                    string.Empty,
                    "Der lokale Zielpfad würde das Incoming-Mapping verlassen.");
            }

            Directory.CreateDirectory(target);
            return new HostIncomingDirectoryPreparationResult(true, target, string.Empty);
        }
        catch (Exception ex)
        {
            return new HostIncomingDirectoryPreparationResult(
                false,
                string.Empty,
                "Die lokale Zielordnerstruktur konnte nicht angelegt werden: " + ex.Message);
        }
    }

    private static bool IsAtOrBelow(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(normalizedRoot, normalizedCandidate, comparison)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison);
    }
}
