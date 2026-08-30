using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AJCC.Core.Links;
using AJCC.Core.Models;
using AJCC.Desktop.Services;
using AJCC.Desktop.Views;

namespace AJCC.Desktop;

public sealed partial class App : Application
{
    private static AjSingleInstanceService? _configuredSingleInstanceService;
    private static string[] _configuredStartupArguments = Array.Empty<string>();
    private bool _dispatcherExceptionHandlerRegistered;
    private MainWindow? _mainWindow;

    internal static void ConfigureStartup(
        AjSingleInstanceService singleInstanceService,
        string[]? startupArguments)
    {
        _configuredSingleInstanceService = singleInstanceService;
        _configuredStartupArguments = startupArguments?.ToArray() ?? Array.Empty<string>();
    }

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
            AjStartupImportRequest startupRequest =
                AjStartupArgumentParser.Parse(_configuredStartupArguments);
            _mainWindow = new MainWindow(startupRequest);
            desktop.MainWindow = _mainWindow;
            StartupDiagnostics.WriteState("Main window assigned", _configuredStartupArguments);
        }

        base.OnFrameworkInitializationCompleted();

        if (_configuredSingleInstanceService is not null)
        {
            _configuredSingleInstanceService.ArgumentsReceived += SingleInstanceOnArgumentsReceived;
            _configuredSingleInstanceService.StartServer();
            StartupDiagnostics.WriteState("Single-instance IPC server started");
        }

        StartupDiagnostics.SetPhase("Startup.complete");
        StartupDiagnostics.WriteState("Framework initialization completed");
    }

    private void SingleInstanceOnArgumentsReceived(string[] args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MainWindow? window = _mainWindow;
            if (window is null)
                return;

            AjStartupImportRequest request = AjStartupArgumentParser.Parse(args);
            if (request.HasItems)
            {
                StartupDiagnostics.WriteState(
                    "External AJFSP/AJL arguments handed to running instance without activation",
                    args);
                window.EnqueueExternalStartupArguments(args);
                return;
            }

            ActivateExistingUi(window);
        });
    }

    private static void ActivateExistingUi(MainWindow window)
    {
        try
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            if (!window.IsVisible)
                window.Show();

            window.Activate();
            StartupDiagnostics.WriteState("Existing UI activated after secondary startup");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("SingleInstance.ActivateExistingUi", ex);
        }
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
