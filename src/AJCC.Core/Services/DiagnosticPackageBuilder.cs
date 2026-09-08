using System.IO.Compression;
using System.Text;

namespace AJCC.Core.Services;

public sealed record DiagnosticPackageContent(
    string Summary,
    string DeveloperLog,
    string StateSnapshot);

public static class DiagnosticPackageBuilder
{
    public const string SummaryFileName = "diagnostic-summary.txt";
    public const string DeveloperLogFileName = "developer-log-anonymized.txt";
    public const string StateSnapshotFileName = "state-snapshot-anonymized.txt";
    public const string PrivacyReadmeFileName = "privacy-readme.txt";

    public static byte[] CreateZip(DiagnosticPackageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteSanitizedEntry(archive, SummaryFileName, content.Summary);
            WriteSanitizedEntry(archive, DeveloperLogFileName, content.DeveloperLog);
            WriteSanitizedEntry(archive, StateSnapshotFileName, content.StateSnapshot);
            WriteSanitizedEntry(archive, PrivacyReadmeFileName, BuildPrivacyReadme());
        }

        return output.ToArray();
    }

    public static string BuildPrivacyReadme()
    {
        StringBuilder builder = new();
        builder.AppendLine("Applejuice-Control-Center - anonymisiertes Diagnosepaket");
        builder.AppendLine("======================================================");
        builder.AppendLine();
        builder.AppendLine("Dieser Export ist absichtlich konservativ anonymisiert.");
        builder.AppendLine("Maskiert werden insbesondere: Passwörter und Passwortparameter, AJFSP-/file-Links, lokale und UNC-Pfade, Host-/Serverangaben, IP-Adressen, Hashes/Checksums, Dateinamen, Suchtexte, Nicks/Quellen, Roh-URLs mit Query-Parametern sowie rohe Core-/XML-Antwortinhalte.");
        builder.AppendLine("Der Export enthält keine Share-Dateien, keine heruntergeladenen Dateien, keine absichtlich vollständigen Dateilisten und keine Raw-Core-Payloads.");
        builder.AppendLine("Wenn durch die starke Maskierung Diagnosewert verloren geht, ist das beabsichtigt: Datenschutz hat hier Vorrang vor maximaler Detailtiefe.");
        return builder.ToString();
    }

    private static void WriteSanitizedEntry(
        ZipArchive archive,
        string fileName,
        string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using StreamWriter writer = new(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false);
        writer.Write(DiagnosticPrivacySanitizer.Sanitize(content ?? string.Empty));
    }
}
