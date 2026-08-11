using LilacMacro.Core.LocalSession;

namespace LilacMacro.App.Runtime;

internal sealed record MacroSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Dictionary<string, int?> KeyBindings { get; init; } = [];

    public ExecutionTarget ExecutionTarget { get; init; } = ExecutionTarget.LocalDesktop;
}
