namespace LilacMacro.Core.Automation;

public readonly record struct AutomationTextStroke(int VirtualKey, bool Shift);

public static class AutomationTextInput
{
    public const int MaximumLength = 64;
    public const int OemMinusVirtualKey = 0xBD;

    public static IReadOnlyList<AutomationTextStroke> Create(
        string value,
        bool capsLockEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength)
            throw new ArgumentOutOfRangeException(nameof(value), $"Text input is limited to {MaximumLength} characters.");

        return value.Select(character => CreateStroke(character, capsLockEnabled)).ToArray();
    }

    private static AutomationTextStroke CreateStroke(char character, bool capsLockEnabled) => character switch
    {
        >= 'A' and <= 'Z' => new(character, Shift: !capsLockEnabled),
        >= 'a' and <= 'z' => new(char.ToUpperInvariant(character), Shift: capsLockEnabled),
        >= '0' and <= '9' => new(character, Shift: false),
        '-' => new(OemMinusVirtualKey, Shift: false),
        '_' => new(OemMinusVirtualKey, Shift: true),
        _ => throw new InvalidDataException(
            "Text input supports only ASCII letters, digits, hyphens, and underscores."),
    };
}
