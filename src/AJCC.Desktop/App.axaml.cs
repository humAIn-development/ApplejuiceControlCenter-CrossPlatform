using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AJCC.Desktop.Services;
using AJCC.Desktop.Views;

namespace AJCC.Desktop;

public sealed partial class App : Application
{
    private bool _dispatcherExceptionHandlerRegistered;

    public override void Initialize()
    {
        StartupDiagnostics.SetPhase("App.Initialize");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupDiagnostics.SetPhase("App.FrameworkInitialization.begin");
        RegisterDispatcherExceptionHandler();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupDiagnostics.SetPhase("MainWindow.create");
            desktop.MainWindow = new MainWindow();
            StartupDiagnostics.WriteState("Main window assigned");
        }

        base.OnFrameworkInitializationCompleted();

        StartupDiagnostics.SetPhase("Startup.complete");
        StartupDiagnostics.WriteState("Framework initialization completed");
    }

    private void RegisterDispatcherExceptionHandler()
    {
        if (_dispatcherExceptionHandlerRegistered)
            return;

        _dispatcherExceptionHandlerRegistered = true;
        Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
            StartupDiagnostics.WriteException(
                "Dispatcher.UIThread.UnhandledException",
                eventArgs.Exception);

        StartupDiagnostics.WriteState("Dispatcher exception handler registered");
    }
}
