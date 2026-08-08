namespace LilacMacro.Core.Placements;

public static class PlacementReferencePolicy
{
    public static IReadOnlyDictionary<Guid, string> BuildDisplayLabels(
        IReadOnlyList<PlacementStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        PlacementStep[] placements = steps
            .Where(step => step.Kind == PlacementStepKind.Place)
            .ToArray();
        IReadOnlyDictionary<int, int> counts = placements
            .GroupBy(step => step.UnitSlot)
            .ToDictionary(group => group.Key, group => group.Count());
        Dictionary<int, int> offsets = [];
        Dictionary<Guid, string> labels = [];

        foreach (PlacementStep placement in placements)
        {
            int offset = offsets.GetValueOrDefault(placement.UnitSlot);
            offsets[placement.UnitSlot] = offset + 1;
            labels.Add(
                placement.Id,
                counts[placement.UnitSlot] == 1
                    ? placement.UnitSlot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : $"{placement.UnitSlot}{AlphabeticId(offset)}");
        }

        return labels;
    }

    private static string AlphabeticId(int zeroBased)
    {
        int value = checked(zeroBased + 1);
        Span<char> buffer = stackalloc char[8];
        int position = buffer.Length;
        while (value > 0)
        {
            value--;
            buffer[--position] = (char)('a' + value % 26);
            value /= 26;
        }

        return new string(buffer[position..]);
    }
}
