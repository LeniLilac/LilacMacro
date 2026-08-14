namespace LilacMacro.Core.Automation;

public enum ExpeditionRewardResource
{
    None,
    FuelCell,
    EquipmentScrap,
    EquipmentReroll,
    EquipmentLock,
    ExpeditionCoin,
}

public sealed record ExpeditionRewardPool(IReadOnlyDictionary<ExpeditionRewardResource, int> Quantities)
{
    public int Quantity(ExpeditionRewardResource resource) => Quantities.TryGetValue(resource, out int value) ? value : 0;
}

public static class ExpeditionRewardPolicy
{
    public const int MaximumTestTrials = 1000;
    public const int MinimumOptimizationSamples = 500;
    public const int RecommendedOptimizationSamples = 1000;
    public const int MinimumAcceptedSamples = 30;
    public static readonly TimeSpan DefaultRunDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultRerollDuration = TimeSpan.FromSeconds(10);

    public static bool Accepts(ExpeditionRewardPool pool, ExpeditionRewardResource resource, int minimum) =>
        resource == ExpeditionRewardResource.None || pool.Quantity(resource) >= Math.Max(0, minimum);

    public static ExpeditionRewardResource ParseResource(string value) => Normalize(value) switch
    {
        "fuelcell" => ExpeditionRewardResource.FuelCell,
        "equipmentscrap" => ExpeditionRewardResource.EquipmentScrap,
        "equipmentreroll" => ExpeditionRewardResource.EquipmentReroll,
        "equipmentlock" => ExpeditionRewardResource.EquipmentLock,
        "expeditioncoin" => ExpeditionRewardResource.ExpeditionCoin,
        "none" or "" => ExpeditionRewardResource.None,
        _ => throw new InvalidDataException($"Unsupported Expedition reward target '{value}'."),
    };

    public static ExpeditionRewardOptimization Optimize(
        IReadOnlyList<int> quantities,
        TimeSpan rerollDuration,
        TimeSpan? runDuration = null)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        if (quantities.Count == 0 || quantities.Any(quantity => quantity < 0))
            throw new InvalidDataException("Expedition reward optimization requires nonnegative observations.");
        TimeSpan match = runDuration ?? DefaultRunDuration;
        if (match <= TimeSpan.Zero || rerollDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(rerollDuration));

        ExpeditionRewardOptimization? best = null;
        foreach (int threshold in quantities.Append(0).Distinct().Order())
        {
            int[] accepted = quantities.Where(quantity => quantity >= threshold).ToArray();
            if (threshold > 0 && accepted.Length < Math.Min(MinimumAcceptedSamples, quantities.Count)) continue;
            double probability = accepted.Length / (double)quantities.Count;
            if (probability <= 0) continue;
            double expected = accepted.Average();
            double seconds = match.TotalSeconds + rerollDuration.TotalSeconds * ((1 / probability) - 1);
            ExpeditionRewardOptimization candidate = new(
                threshold,
                probability,
                expected,
                TimeSpan.FromSeconds(seconds),
                expected / seconds * 3600,
                quantities.Count,
                accepted.Length);
            if (best is null || candidate.ExpectedPerHour > best.ExpectedPerHour + 1e-9 ||
                (Math.Abs(candidate.ExpectedPerHour - best.ExpectedPerHour) <= 1e-9 &&
                 candidate.Threshold < best.Threshold))
            {
                best = candidate;
            }
        }
        return best ?? throw new InvalidDataException("Expedition reward optimization found no supported threshold.");
    }

    public static int ValidateTestTrials(int trials)
    {
        if (trials is < 1 or > MaximumTestTrials)
            throw new InvalidDataException($"Trials must be between 1 and {MaximumTestTrials}.");
        return trials;
    }

    public static int? ParseQuantity(string value, ExpeditionRewardResource resource)
    {
        string normalized = Normalize(value).TrimEnd('x', 'c');
        if (resource == ExpeditionRewardResource.FuelCell && normalized == "z")
            return 2;
        normalized = normalized.Replace('o', '0');
        if (normalized is "i" or "l") return 1;
        if (normalized == "b" && resource is
            ExpeditionRewardResource.EquipmentLock or ExpeditionRewardResource.EquipmentReroll)
        {
            return 1;
        }
        if (normalized is "2b" or "2k" && resource is
            ExpeditionRewardResource.FuelCell or ExpeditionRewardResource.EquipmentScrap)
        {
            return 21;
        }
        if (normalized == "tb" && resource is
            ExpeditionRewardResource.FuelCell or ExpeditionRewardResource.EquipmentScrap)
        {
            return 11;
        }
        if (normalized == "11b" && resource is
            ExpeditionRewardResource.EquipmentScrap or ExpeditionRewardResource.ExpeditionCoin)
        {
            return 11;
        }
        return normalized.Length > 0 && normalized.All(char.IsDigit) &&
               int.TryParse(normalized, out int parsed)
            ? parsed
            : null;
    }

    private static string Normalize(string value) => new(
        value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

public sealed record ExpeditionRewardOptimization(
    int Threshold,
    double AcceptanceProbability,
    double ExpectedAcceptedQuantity,
    TimeSpan ExpectedCycleDuration,
    double ExpectedPerHour,
    int ObservationCount,
    int AcceptedObservationCount);
