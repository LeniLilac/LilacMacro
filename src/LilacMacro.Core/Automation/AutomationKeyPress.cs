namespace LilacMacro.Core.Automation;

public readonly record struct AutomationKeyPress(int VirtualKey, int HoldMilliseconds)
{
    public const int DefaultVirtualKey = 0x57;
    public const int DefaultHoldMilliseconds = 1000;
    public const int MinimumHoldMilliseconds = 1;
    public const int MaximumHoldMilliseconds = 120000;

    public string KeyName => KeyboardKey.GetDisplayName(VirtualKey);

    public static AutomationKeyPress Create(
        int virtualKey,
        int holdMilliseconds,
        int reservedVirtualKey)
    {
        if (!KeyboardKey.IsSupportedAutomationKey(virtualKey))
        {
            throw new InvalidDataException("Choose a supported key.");
        }
        if (virtualKey == reservedVirtualKey)
        {
            throw new InvalidDataException("Choose a key other than the F6 start and stop key.");
        }
        if (holdMilliseconds is < MinimumHoldMilliseconds or > MaximumHoldMilliseconds)
        {
            throw new InvalidDataException(
                $"Hold time must be {MinimumHoldMilliseconds} to {MaximumHoldMilliseconds} ms.");
        }
        return new AutomationKeyPress(virtualKey, holdMilliseconds);
    }
}
