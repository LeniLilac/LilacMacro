namespace LilacMacro.Core.Ocr;

public static class ScrollTrialSchedule
{
    public const int MaximumWheelUnits = 10000;

    public static IReadOnlyList<int> Create(
        int startingWheelUnits,
        int increment,
        int trialCount)
    {
        if (startingWheelUnits is < 1 or > MaximumWheelUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingWheelUnits),
                $"Starting scroll units must be between 1 and {MaximumWheelUnits}.");
        }
        if (increment is < 0 or > MaximumWheelUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(increment),
                $"Scroll increment must be between 0 and {MaximumWheelUnits}.");
        }
        if (trialCount is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trialCount),
                "Trials must be between 1 and 1000.");
        }

        long finalWheelUnits = startingWheelUnits + (long)increment * (trialCount - 1);
        if (finalWheelUnits > MaximumWheelUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(increment),
                $"The final trial would exceed {MaximumWheelUnits} scroll units.");
        }

        return Enumerable.Range(0, trialCount)
            .Select(index => startingWheelUnits + increment * index)
            .ToArray();
    }
}
