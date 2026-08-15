namespace LilacMacro.Core.Services;

public static class ControlSnapshotTrust
{
    public static readonly Uri Endpoint = new("https://macro.expeditions.gg/v1/control");

    public static readonly IReadOnlyDictionary<string, string> PublicKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["primary-1"] = "MCowBQYDK2VwAyEADMaRnWHjzh0GI4PBAfc8sxctSHO96lmnnxDNK/sVi3E=",
        };
}
