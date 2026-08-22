using System.Windows;
using System.Windows.Threading;
using System.Net.Http;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.DeepDebugViewer;
using LilacMacro.App.Lifecycle;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Theming;
using LilacMacro.App.Updates;
using LilacMacro.Core.Updates;
using LilacMacro.Windows.LocalSession;
using LilacMacro.Windows.SystemInformation;
using LilacMacro.App.Views;

namespace LilacMacro.App;

public partial class App : Application
{
    private DeepDebugSessionService? _deepDebug;
    private UpdateShutdownMonitor? _updateShutdown;
    private Mutex? _managedInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += OnUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        MacroInstanceContext.Initialize(eventArgs.Args);
        string localAppDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro");
        _deepDebug = new DeepDebugSessionService(
            localAppDataRoot,
            MacroInstanceContext.Current.DiagnosticsRoot,
            operatingSystemVersion: WindowsVersionDescription.Read());
        if (MacroInstanceContext.Current.IsManagedRunner && !AcquireManagedInstanceMutex())
        {
            Shutdown(0);
            return;
        }
        AppLaunchMode launchMode = AppLaunchModePolicy.Resolve(
            eventArgs.Args,
            Environment.ProcessPath);
        await MacroConfigurationMigrator.EnsureOwnerSharedConfigurationAsync();
        Window startupWindow;
        if (launchMode == AppLaunchMode.Macro)
        {
            Runtime.MacroOwnerState ownerState = await Runtime.MacroOwnerState.LoadAsync();
            AppThemeManager.Apply(ownerState.ThemeMode, ownerState.ColorTheme);
            bool firstRunPrivacy = !ownerState.HasAcceptedCurrentPrivacyChoices;
            if (firstRunPrivacy)
            {
                ShutdownMode previousMode = ShutdownMode;
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                PrivacyChoicesWindow privacyWindow = new(ownerState);
                MainWindow = privacyWindow;
                bool accepted = privacyWindow.ShowDialog() == true;
                ShutdownMode = previousMode;
                if (!accepted)
                {
                    Shutdown(0);
                    return;
                }
            }
            if (ShouldCheckGpuSetup(firstRunPrivacy, MacroInstanceContext.Current.IsManagedRunner))
                await ShowGpuSetupIfNeededAsync();
            startupWindow = new MacroShellWindow(_deepDebug, ownerState);
        }
        else
        {
            startupWindow = launchMode switch
            {
                AppLaunchMode.DatasetBuilder => new MainWindow(_deepDebug, Workspace.ToolShellKind.DatasetBuilder),
                AppLaunchMode.RuntimeLab => new MainWindow(_deepDebug, Workspace.ToolShellKind.RuntimeLab),
                AppLaunchMode.DeepDebugViewer => new DeepDebugViewerWindow(),
                _ => throw new InvalidOperationException($"Unsupported launch mode {launchMode}."),
            };
        }
        MainWindow = startupWindow;
        startupWindow.Show();
        if (MacroInstanceContext.Current.IsManagedRunner)
            _ = PrepareManagedRunnerAsync();
        LilacSemanticVersion currentVersion = LilacSemanticVersion.FromAssemblyVersion(
            typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));
        _updateShutdown = new UpdateShutdownMonitor(
            Dispatcher,
            currentVersion,
            () => MainWindow?.Close());
        _updateShutdown.Start();
        if (startupWindow is DeepDebugViewerWindow viewer)
        {
            string? archivePath = eventArgs.Args.FirstOrDefault(argument =>
                argument.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (archivePath is not null) viewer.OpenArchiveFromCommandLine(archivePath);
        }
    }

    private static async Task PrepareManagedRunnerAsync()
    {
        try
        {
            RunnerDesktopPersonalization.ApplyCurrentSession();
            await new RunnerFirstLaunchBootstrap().RunAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or HttpRequestException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            Notifications.AppToastService.ShowError("RUNNER SETUP INCOMPLETE", exception.Message);
        }
    }

    private async Task ShowGpuSetupIfNeededAsync()
    {
        using OcrRunner setupOcr = new(_deepDebug!);
        if (setupOcr.IsDeviceReady(OcrRunner.GpuDevice)) return;

        OcrGpuInfo? gpu;
        try
        {
            gpu = await setupOcr.ProbeGpuAsync();
        }
        catch
        {
            return;
        }
        if (gpu is null) return;

        ShutdownMode previousMode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        GpuOcrSetupWindow setupWindow = new(setupOcr, gpu);
        MainWindow = setupWindow;
        setupWindow.ShowDialog();
        ShutdownMode = previousMode;
    }

    internal static bool ShouldCheckGpuSetup(bool acceptedPrivacyThisLaunch, bool isManagedRunner) =>
        acceptedPrivacyThisLaunch || isManagedRunner;

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _updateShutdown?.Dispose();
        if (_managedInstanceMutex is not null)
        {
            _managedInstanceMutex.ReleaseMutex();
            _managedInstanceMutex.Dispose();
            _managedInstanceMutex = null;
        }
        base.OnExit(eventArgs);
    }

    private bool AcquireManagedInstanceMutex()
    {
        string name = ManagedInstanceMutexName(MacroInstanceContext.Current.Id);
        Mutex candidate = new(initiallyOwned: true, name, out bool createdNew);
        if (createdNew)
        {
            _managedInstanceMutex = candidate;
            return true;
        }
        candidate.Dispose();
        return false;
    }

    internal static string ManagedInstanceMutexName(string profileId) =>
        $@"Local\LilacMacro.ManagedInstance.{profileId}";

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
        if (Current is App { _deepDebug: { } deepDebug })
        {
            deepDebug.RecordEvent("application", "unhandled_exception", new
            {
                Error = eventArgs.Exception.ToString(),
                CrashLog = crashPath,
            });
            await deepDebug.CompleteActiveAsync("unhandled-error", eventArgs.Exception);
        }
        MessageBox.Show(
            $"{root.GetType().Name}: {root.Message}\n\nDetails: {crashPath}",
            "LilacMacro stopped safely",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        eventArgs.Handled = true;
    }
}
