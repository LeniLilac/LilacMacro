using System.Text;

namespace LilacMacro.Core.Datasets;

public static class DatasetNaming
{
    public static string ValidateDisplayName(string? name)
    {
        string value = (name ?? string.Empty).Trim();
        if (value.Length is < 1 or > 120)
        {
            throw new ArgumentException("Dataset name must contain between 1 and 120 characters.", nameof(name));
        }
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Dataset name cannot contain control characters.", nameof(name));
        }
        return value;
    }

    public static string Slugify(string name)
    {
        string value = ValidateDisplayName(name).Normalize(NormalizationForm.FormKD);
        StringBuilder result = new(capacity: Math.Min(value.Length, 48));
        bool pendingSeparator = false;

        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && result.Length > 0) result.Append('-');
                result.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
            if (result.Length >= 48) break;
        }

        string slug = result.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "dataset" : slug;
    }
}
