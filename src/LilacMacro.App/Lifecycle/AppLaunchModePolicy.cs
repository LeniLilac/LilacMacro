namespace LilacMacro.App.Lifecycle;

internal enum AppLaunchMode
{
    Macro,
    DatasetBuilder,
    RuntimeLab,
}

internal static class AppLaunchModePolicy
{
    internal const string DatasetBuilderArgument = "--dataset-builder";
    internal const string DatasetBuilderExecutableName = "LilacMacro.DatasetBuilder";
    internal const string RuntimeLabArgument = "--runtime-lab";
    internal const string RuntimeLabExecutableName = "LilacMacro.RuntimeLab";

    public static AppLaunchMode Resolve(IEnumerable<string> arguments, string? processPath)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool requestsDatasetBuilder = arguments.Contains(
            DatasetBuilderArgument,
            StringComparer.OrdinalIgnoreCase);
        bool requestsRuntimeLab = arguments.Contains(
            RuntimeLabArgument,
            StringComparer.OrdinalIgnoreCase);
        if (requestsDatasetBuilder && requestsRuntimeLab)
        {
            throw new ArgumentException("Choose either Dataset Builder or Runtime Lab, not both.", nameof(arguments));
        }
        if (requestsDatasetBuilder) return AppLaunchMode.DatasetBuilder;
        if (requestsRuntimeLab) return AppLaunchMode.RuntimeLab;

        string executableName = Path.GetFileNameWithoutExtension(processPath) ?? string.Empty;
        if (string.Equals(
                executableName,
                DatasetBuilderExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            return AppLaunchMode.DatasetBuilder;
        }
        return string.Equals(executableName, RuntimeLabExecutableName, StringComparison.OrdinalIgnoreCase)
            ? AppLaunchMode.RuntimeLab
            : AppLaunchMode.Macro;
    }
}
