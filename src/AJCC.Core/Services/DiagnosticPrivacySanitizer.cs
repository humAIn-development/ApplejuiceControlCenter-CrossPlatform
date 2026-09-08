using System.Text.RegularExpressions;

namespace AJCC.Core.Services;

public static class DiagnosticPrivacySanitizer
{
    public static string Sanitize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input))
            return input;

        string output = MaskPasswordParameters(input);

        output = Regex.Replace(
            output,
            @"(\|\s*Antwort:\s*)[^\r\n]*",
            "$1[RAW_CORE_RESPONSE_MASKED_FOR_PRIVACY]",
            RegexOptions.IgnoreCase);

        output = Regex.Replace(output, @"ajfsp://\S+", "[MASKED_AJFSP_LINK]", RegexOptions.IgnoreCase);
        output = Regex.Replace(output, @"file://\S+", "[MASKED_FILE_LINK]", RegexOptions.IgnoreCase);
        output = Regex.Replace(output, @"https?://\S+", "[MASKED_URL]", RegexOptions.IgnoreCase);

        output = Regex.Replace(output, @"(?<![A-Za-z0-9])([A-Za-z]:\\[^\r\n\t<>""']+)", "[MASKED_PATH]");
        output = Regex.Replace(output, @"\\\\[^\r\n\t<>""']+", "[MASKED_UNC_PATH]");

        output = Regex.Replace(
            output,
            @"(?<![A-Za-z0-9])/(home|mnt|media|Users|var|tmp|opt|root|etc|usr|bin|sbin|dev|sys|proc|run|app|lib)/[^\r\n\t<>""']+",
            "[MASKED_PATH]",
            RegexOptions.IgnoreCase);
        output = Regex.Replace(
            output,
            @"(?<![A-Za-z0-9])/(home|mnt|media|Users|var|tmp|opt|root|etc|usr|bin|sbin|dev|sys|proc|run|app|lib)(?=($|[\s\r\n\t<>""']))",
            "[MASKED_PATH]",
            RegexOptions.IgnoreCase);

        output = Regex.Replace(output, @"(Verzeichnis geladen:\s*)[^\r\n]+", "$1[MASKED_PATH]", RegexOptions.IgnoreCase);
        output = Regex.Replace(output, @"(Verzeichnisbaum bis Initialpfad geöffnet:\s*)[^\r\n]+", "$1[MASKED_PATH]", RegexOptions.IgnoreCase);

        output = Regex.Replace(output, @"<nick>.*?</nick>", "<nick>[MASKED_NICK]</nick>", RegexOptions.IgnoreCase);
        output = Regex.Replace(
            output,
            @"\b(filename|name|host|nick|source|checksum|hash|link)\s*=\s*""[^""]*""",
            "$1=\"[MASKED]\"",
            RegexOptions.IgnoreCase);
        output = Regex.Replace(
            output,
            @"\b(filename|name|host|nick|source|checksum|hash|link)\s*=\s*'[^']*'",
            "$1='[MASKED]'",
            RegexOptions.IgnoreCase);
        output = Regex.Replace(
            output,
            @"\b(filename|name|host|nick|source|checksum|hash|link)\s*=\s*[^\s>/]+",
            "$1=[MASKED]",
            RegexOptions.IgnoreCase);

        output = Regex.Replace(
            output,
            @"\b[A-Za-z0-9][A-Za-z0-9.-]*\.(?:de|com|net|org|cc|io|info|eu)\b",
            "[MASKED_DOMAIN]",
            RegexOptions.IgnoreCase);

        output = Regex.Replace(
            output,
            @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?::\d{1,5})?\b",
            "[MASKED_IP]");

        output = Regex.Replace(output, @"\b[a-fA-F0-9]{32,128}\b", "[MASKED_HASH]");

        string[] fields =
        {
            "Filename", "DisplayFilename", "FileName", "Target", "TargetDirectory", "IncomingDirectory",
            "TemporaryDirectory", "Source", "SourceText", "Nick", "Nickname", "Host", "Machine",
            "User", "CurrentDirectory", "SearchText", "Text", "Request", "AJFSP"
        };

        foreach (string field in fields)
        {
            output = Regex.Replace(
                output,
                $@"({Regex.Escape(field)}\s*=\s*)'[^'\r\n]*'",
                "$1'[MASKED]'",
                RegexOptions.IgnoreCase);
            output = Regex.Replace(
                output,
                $@"({Regex.Escape(field)}\s*=\s*)[^\t\r\n]+",
                "$1[MASKED]",
                RegexOptions.IgnoreCase);
            output = Regex.Replace(
                output,
                $@"({Regex.Escape(field)}\s*:\s*)[^\r\n]+",
                "$1[MASKED]",
                RegexOptions.IgnoreCase);
        }

        output = Regex.Replace(
            output,
            @"\b[^\s\t\r\n<>""']+\.(avi|mkv|mp4|mp3|flac|wav|zip|rar|7z|iso|exe|msi|pdf|txt|nfo|jpg|jpeg|png|gif|webp|ajl)\b",
            "[MASKED_FILENAME]",
            RegexOptions.IgnoreCase);

        return output;
    }

    private static string MaskPasswordParameters(string input)
    {
        string output = input;
        string[] keywords =
        {
            "password",
            "passwort",
            "cpass",
            "corepass",
            "corepassword",
            "newpassword"
        };

        foreach (string keyword in keywords)
        {
            output = Regex.Replace(
                output,
                keyword + "=([^&\\s]+)",
                keyword + "=******",
                RegexOptions.IgnoreCase);
        }

        return output;
    }
}
