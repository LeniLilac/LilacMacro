namespace LilacMacro.App.Lifecycle;

internal enum AppLaunchMode
{
    Macro,
    DatasetBuilder,
    RuntimeLab,
    DeepDebugViewer,
}

internal static class AppLaunchModePolicy
{
    internal const string DatasetBuilderArgument = "--dataset-builder";
    internal const string DatasetBuilderExecutableName = "LilacMacro.DatasetBuilder";
    internal const string RuntimeLabArgument = "--runtime-lab";
    internal const string RuntimeLabExecutableName = "LilacMacro.RuntimeLab";
    internal const string DeepDebugViewerArgument = "--deep-debug-viewer";
    internal const string DeepDebugViewerExecutableName = "LilacMacro.DeepDebugViewer";

    public static AppLaunchMode Resolve(IEnumerable<string> arguments, string? processPath)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool requestsDatasetBuilder = arguments.Contains(
            DatasetBuilderArgument,
            StringComparer.OrdinalIgnoreCase);
        bool requestsRuntimeLab = arguments.Contains(
            RuntimeLabArgument,
            StringComparer.OrdinalIgnoreCase);
        bool requestsDeepDebugViewer = arguments.Contains(
            DeepDebugViewerArgument,
            StringComparer.OrdinalIgnoreCase);
        if ((requestsDatasetBuilder ? 1 : 0) + (requestsRuntimeLab ? 1 : 0) + (requestsDeepDebugViewer ? 1 : 0) > 1)
        {
            throw new ArgumentException("Choose only one LilacMacro tool mode.", nameof(arguments));
        }
        if (requestsDatasetBuilder) return AppLaunchMode.DatasetBuilder;
        if (requestsRuntimeLab) return AppLaunchMode.RuntimeLab;
        if (requestsDeepDebugViewer) return AppLaunchMode.DeepDebugViewer;

        string executableName = Path.GetFileNameWithoutExtension(processPath) ?? string.Empty;
        if (string.Equals(
                executableName,
                DatasetBuilderExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            return AppLaunchMode.DatasetBuilder;
        }
        if (string.Equals(executableName, RuntimeLabExecutableName, StringComparison.OrdinalIgnoreCase))
            return AppLaunchMode.RuntimeLab;
        return string.Equals(executableName, DeepDebugViewerExecutableName, StringComparison.OrdinalIgnoreCase)
            ? AppLaunchMode.DeepDebugViewer
            : AppLaunchMode.Macro;
    }
}
