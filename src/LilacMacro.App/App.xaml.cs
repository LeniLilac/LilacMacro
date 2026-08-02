using System.Windows;
using System.Windows.Threading;

namespace LilacMacro.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
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
        MessageBox.Show(
            $"{root.GetType().Name}: {root.Message}\n\nDetails: {crashPath}",
            "LilacMacro stopped safely",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        eventArgs.Handled = true;
    }
}
