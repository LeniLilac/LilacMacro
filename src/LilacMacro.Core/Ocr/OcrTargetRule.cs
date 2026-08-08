namespace LilacMacro.Core.Ocr;

public sealed record OcrTargetRule
{
    public OcrTargetRule(string name, params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(aliases);
        if (aliases.Length == 0 || aliases.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty OCR alias is required.", nameof(aliases));
        }

        Name = name;
        Aliases = aliases.ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<string> Aliases { get; }
}
