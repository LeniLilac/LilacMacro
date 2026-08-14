using System.Windows.Threading;
using LilacMacro.Core.Updates;

namespace LilacMacro.App.Updates;

internal sealed class UpdateShutdownMonitor : IDisposable
{
    private readonly string requestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LilacMacro",
        "UpdateControl",
        "update-request.txt");
    private readonly DispatcherTimer timer;
    private readonly LilacSemanticVersion currentVersion;
    private readonly Action closeApplication;
    private bool checking;

    public UpdateShutdownMonitor(
        Dispatcher dispatcher,
        LilacSemanticVersion currentVersion,
        Action closeApplication)
    {
        this.currentVersion = currentVersion;
        this.closeApplication = closeApplication;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, Check, dispatcher);
    }

    public void Start() => timer.Start();

    private async void Check(object? sender, EventArgs eventArgs)
    {
        if (checking || !File.Exists(requestPath)) return;
        checking = true;
        try
        {
            string text = await File.ReadAllTextAsync(requestPath).ConfigureAwait(true);
            CoordinatedUpdateRequest request = CoordinatedUpdateText.ParseRequest(text);
            if (!CoordinatedUpdateText.ShouldClose(request, currentVersion, DateTimeOffset.UtcNow)) return;
            timer.Stop();
            closeApplication();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            // A partial, stale, or inaccessible request never authorizes application shutdown.
        }
        finally
        {
            checking = false;
        }
    }

    public void Dispose() => timer.Stop();
}
