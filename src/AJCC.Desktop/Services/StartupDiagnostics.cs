using System.Text;
using AJCC.Core.Services;

namespace AJCC.Desktop.Services;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private const long MaxDiagnosticLogBytes = 4L * 1024 * 1024;
    private static string _phase = "Process.constructed";

    public static void SetPhase(string phase)
    {
        _phase = string.IsNullOrWhiteSpace(phase) ? "unknown" : phase.Trim();
        WriteState("Startup phase changed");
    }

    public static void WriteState(string message, IEnumerable<string>? args = null)
    {
        try
        {
            string line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
                $"PID={Environment.ProcessId} Thread={Environment.CurrentManagedThreadId} " +
                $"Phase={_phase} {message}";

            if (args is not null)
            {
                line += " Args=" + string.Join(
                    " | ",
                    args.Select(static arg => DiagnosticPrivacySanitizer.Sanitize(arg ?? string.Empty)));
            }

            AppendDiagnostic(line + Environment.NewLine);
        }
        catch
        {
        }
    }

    public static void WriteException(string source, Exception exception)
    {
        try
        {
            string header =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
                $"PID={Environment.ProcessId} Thread={Environment.CurrentManagedThreadId} " +
                $"Phase={_phase} Source={source}";

            AppendDiagnostic(
                header + Environment.NewLine +
                "EXCEPTION BEGIN" + Environment.NewLine +
                exception + Environment.NewLine +
                "EXCEPTION END" + Environment.NewLine);

            WriteStartupError(
                header + Environment.NewLine +
                Environment.NewLine +
                exception);
        }
        catch
        {
        }
    }

    public static string ReadRecentLogTails(int maxBytesPerFile = 64 * 1024)
    {
        if (maxBytesPerFile <= 0)
            return string.Empty;

        StringBuilder builder = new();
        string folder = GetDiagnosticsFolder();

        lock (Sync)
        {
            AppendRecentLogTail(
                builder,
                Path.Combine(folder, "startup-diagnostics.log"),
                "startup-diagnostics.log",
                maxBytesPerFile);

            AppendRecentLogTail(
                builder,
                Path.Combine(folder, "startup-error.log"),
                "startup-error.log",
                maxBytesPerFile);
        }

        return builder.ToString();
    }

    private static void AppendRecentLogTail(
        StringBuilder builder,
        string path,
        string label,
        int maxBytes)
    {
        if (builder.Length > 0)
            builder.AppendLine();

        builder.AppendLine($"===== {label} (bounded tail) =====");

        if (!File.Exists(path))
        {
            builder.AppendLine("[not present]");
            return;
        }

        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            long start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);

            using StreamReader reader = new(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: start == 0,
                bufferSize: 4096,
                leaveOpen: false);

            string text = reader.ReadToEnd();
            if (start > 0)
            {
                int firstLineBreak = text.IndexOf('\n');
                if (firstLineBreak >= 0 && firstLineBreak + 1 < text.Length)
                    text = text[(firstLineBreak + 1)..];
            }

            builder.Append(text);
            if (text.Length > 0 && !text.EndsWith('\n'))
                builder.AppendLine();
        }
        catch (Exception ex)
        {
            builder.AppendLine($"[read failed: {ex.GetType().Name}]");
        }
    }

    private static void AppendDiagnostic(string content)
    {
        string folder = GetDiagnosticsFolder();
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "startup-diagnostics.log");

        lock (Sync)
        {
            if (File.Exists(path) && new FileInfo(path).Length >= MaxDiagnosticLogBytes)
            {
                File.WriteAllText(
                    path,
                    "[older startup diagnostics truncated]" + Environment.NewLine,
                    Encoding.UTF8);
            }

            File.AppendAllText(path, content, Encoding.UTF8);
        }
    }

    private static void WriteStartupError(string content)
    {
        string folder = GetDiagnosticsFolder();
        Directory.CreateDirectory(folder);

        lock (Sync)
        {
            File.WriteAllText(
                Path.Combine(folder, "startup-error.log"),
                content,
                Encoding.UTF8);
        }
    }

    private static string GetDiagnosticsFolder()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(root, "AJCC-X");
    }
}
