using LilacMacro.App.Diagnostics;
using LilacMacro.App.Debugging;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    private IDisposable RegisterDeepDebugFrameCaptureProvider() =>
        _deepDebug.RegisterFrameCaptureProvider(
            "main-macro",
            async token => await _workspace.CaptureLiveFrameAsync(
                DebugWorkflowCatalog.ClientSize, token, "deep-debug-interval"));
}
