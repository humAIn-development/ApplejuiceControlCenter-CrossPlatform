using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class DiagnosticPrivacySanitizerTests
{
    [TestMethod]
    public void Sanitize_MasksPasswordsLinksPathsAndIdentifiers()
    {
        const string input =
            "password=secret\n" +
            "AJFSP ajfsp://server/file/hash\n" +
            "file://C:/Users/Martin/private.iso\n" +
            "https://core.example.org/function/test?password=secret\n" +
            "Path C:\\Users\\Martin\\private.iso\n" +
            "UNC \\\\server\\share\\private.iso\n" +
            "Unix /home/martin/private.iso\n" +
            "<nick>Martin</nick>\n" +
            "host=\"private.example.org\"\n" +
            "IP 192.168.178.25:9851\n" +
            "Hash 0123456789abcdef0123456789abcdef\n" +
            "SearchText=private search\n" +
            "Movie private-film.mkv";

        string output = DiagnosticPrivacySanitizer.Sanitize(input);

        StringAssert.Contains(output, "password=******");
        StringAssert.Contains(output, "[MASKED_AJFSP_LINK]");
        StringAssert.Contains(output, "[MASKED_FILE_LINK]");
        StringAssert.Contains(output, "[MASKED_URL]");
        StringAssert.Contains(output, "[MASKED_PATH]");
        StringAssert.Contains(output, "[MASKED_UNC_PATH]");
        StringAssert.Contains(output, "<nick>[MASKED_NICK]</nick>");
        StringAssert.Contains(output, "host=[MASKED]");
        StringAssert.Contains(output, "[MASKED_IP]");
        StringAssert.Contains(output, "[MASKED_HASH]");
        StringAssert.Contains(output, "SearchText=[MASKED]");
        StringAssert.Contains(output, "[MASKED_FILENAME]");

        Assert.IsFalse(output.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(output.Contains("Martin", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(output.Contains("192.168.178.25", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("0123456789abcdef0123456789abcdef", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(output.Contains("private-film.mkv", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Sanitize_MasksRawCoreResponseButKeepsPrefix()
    {
        const string input = "GET /xml/settings.xml | Antwort: <settings><nick>Martin</nick></settings>";

        string output = DiagnosticPrivacySanitizer.Sanitize(input);

        Assert.AreEqual(
            "GET /xml/settings.xml | Antwort: [RAW_CORE_RESPONSE_MASKED_FOR_PRIVACY]",
            output);
    }

    [TestMethod]
    public void Sanitize_MasksDirectoryNavigationLogs()
    {
        const string input =
            "Verzeichnis geladen: /home/martin/downloads\n" +
            "Verzeichnisbaum bis Initialpfad geöffnet: C:\\Users\\Martin\\Incoming";

        string output = DiagnosticPrivacySanitizer.Sanitize(input);

        Assert.AreEqual(
            "Verzeichnis geladen: [MASKED_PATH]\n" +
            "Verzeichnisbaum bis Initialpfad geöffnet: [MASKED_PATH]",
            output);
    }

    [TestMethod]
    public void Sanitize_MasksAllLegacyPasswordParameterNames()
    {
        const string input =
            "password=a passwort=b cpass=c corepass=d corepassword=e newpassword=f";

        string output = DiagnosticPrivacySanitizer.Sanitize(input);

        Assert.AreEqual(
            "password=****** passwort=****** cpass=****** corepass=****** corepassword=****** newpassword=******",
            output);
    }

    [TestMethod]
    public void Sanitize_LeavesOrdinaryTechnicalStatusReadable()
    {
        const string input = "Polling restored: downloads=12 uploads=3 serverStatus=connected";

        string output = DiagnosticPrivacySanitizer.Sanitize(input);

        Assert.AreEqual(input, output);
    }
}
