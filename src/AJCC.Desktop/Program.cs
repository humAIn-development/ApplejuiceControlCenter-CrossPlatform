using Avalonia;
using AJCC.Desktop.Services;

namespace AJCC.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupDiagnostics.SetPhase("Program.Main.begin");
        StartupDiagnostics.WriteState("Process startup", args);
        RegisterGlobalExceptionHandlers();

        try
        {
            StartupDiagnostics.SetPhase("Avalonia.start");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            StartupDiagnostics.SetPhase("Process.exit");
            StartupDiagnostics.WriteState("Application exited normally");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("Program.Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect();

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            StartupDiagnostics.WriteException(
                "AppDomain.UnhandledException; IsTerminating=" + eventArgs.IsTerminating,
                eventArgs.ExceptionObject as Exception
                ?? new Exception("Unhandled non-Exception object: " + eventArgs.ExceptionObject));

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            StartupDiagnostics.WriteException(
                "TaskScheduler.UnobservedTaskException",
                eventArgs.Exception);

        StartupDiagnostics.WriteState("Global exception handlers registered");
    }
}
