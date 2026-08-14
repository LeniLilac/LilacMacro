namespace LilacMacro.Core.Automation;

public static class UtilityRespawnPolicy
{
    public static IReadOnlyList<int> CreateKeyOrder(int areasMenuVirtualKey)
    {
        if (!KeyboardKey.IsSupportedAutomationKey(areasMenuVirtualKey))
            throw new InvalidDataException("Areas menu must have a supported key.");

        return
        [
            areasMenuVirtualKey,
            KeyboardKey.Escape,
            'R',
            KeyboardKey.Enter,
        ];
    }
}
