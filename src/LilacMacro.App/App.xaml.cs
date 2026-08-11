using System.Windows;
using System.Windows.Threading;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.DeepDebugViewer;
using LilacMacro.App.Lifecycle;
using LilacMacro.App.Infrastructure;

namespace LilacMacro.App;

public partial class App : Application
{
    private readonly DeepDebugSessionService _deepDebug = new();

    public App()
    {
        DispatcherUnhandledException += OnUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        MacroInstanceContext.Initialize(eventArgs.Args);
        AppLaunchMode launchMode = AppLaunchModePolicy.Resolve(
            eventArgs.Args,
            Environment.ProcessPath);
        await MacroConfigurationMigrator.EnsureOwnerSharedConfigurationAsync();
        Window startupWindow = launchMode switch
        {
            AppLaunchMode.DatasetBuilder => new MainWindow(_deepDebug, Workspace.ToolShellKind.DatasetBuilder),
            AppLaunchMode.RuntimeLab => new MainWindow(_deepDebug, Workspace.ToolShellKind.RuntimeLab),
            AppLaunchMode.DeepDebugViewer => new DeepDebugViewerWindow(),
            _ => new MacroShellWindow(_deepDebug, await Runtime.MacroOwnerState.LoadAsync()),
        };
        MainWindow = startupWindow;
        startupWindow.Show();
        if (startupWindow is DeepDebugViewerWindow viewer)
        {
            string? archivePath = eventArgs.Args.FirstOrDefault(argument =>
                argument.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (archivePath is not null) viewer.OpenArchiveFromCommandLine(archivePath);
        }
    }

    private static async void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        Exception root = eventArgs.Exception;
        while (root.InnerException is not null) root = root.InnerException;
        string crashDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "logs");
        string crashPath = Path.Combine(crashDirectory, "latest-crash.txt");
        try
        {
            Directory.CreateDirectory(crashDirectory);
            File.WriteAllText(crashPath, eventArgs.Exception.ToString());
        }
        catch (IOException)
        {
            crashPath = "the local crash log";
        }
        if (Current is App app)
        {
            app._deepDebug.RecordEvent("application", "unhandled_exception", new
            {
                Error = eventArgs.Exception.ToString(),
                CrashLog = crashPath,
            });
            await app._deepDebug.CompleteActiveAsync("unhandled-error", eventArgs.Exception);
        }
        MessageBox.Show(
            $"{root.GetType().Name}: {root.Message}\n\nDetails: {crashPath}",
            "LilacMacro stopped safely",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        eventArgs.Handled = true;
    }
}
