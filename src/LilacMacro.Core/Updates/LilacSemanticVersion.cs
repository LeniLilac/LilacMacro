namespace LilacMacro.Core.Updates;

public readonly record struct LilacSemanticVersion(int Major, int Minor, int Patch)
    : IComparable<LilacSemanticVersion>
{
    public int CompareTo(LilacSemanticVersion other)
    {
        int major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        int minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool TryParse(string? value, out LilacSemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Split('.');
        if (parts.Length != 3
            || !TryPart(parts[0], out int major)
            || !TryPart(parts[1], out int minor)
            || !TryPart(parts[2], out int patch))
        {
            return false;
        }
        version = new LilacSemanticVersion(major, minor, patch);
        return true;
    }

    public static bool TryParseTag(string? value, out LilacSemanticVersion version)
    {
        version = default;
        return value is { Length: > 1 } && value[0] == 'v' && TryParse(value[1..], out version);
    }

    public static LilacSemanticVersion FromAssemblyVersion(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build));

    private static bool TryPart(string value, out int part)
    {
        part = 0;
        if (value.Length is < 1 or > 9 || value.Length > 1 && value[0] == '0') return false;
        return value.All(char.IsAsciiDigit) && int.TryParse(value, out part) && part >= 0;
    }
}
