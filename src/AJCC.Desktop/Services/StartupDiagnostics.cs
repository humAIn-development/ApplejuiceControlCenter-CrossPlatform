using System.Text;

namespace AJCC.Desktop.Services;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
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
                line += " Args=" + string.Join(" | ", args);

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

    private static void AppendDiagnostic(string content)
    {
        string folder = GetDiagnosticsFolder();
        Directory.CreateDirectory(folder);

        lock (Sync)
        {
            File.AppendAllText(
                Path.Combine(folder, "startup-diagnostics.log"),
                content,
                Encoding.UTF8);
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
