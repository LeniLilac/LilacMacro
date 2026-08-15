using System.Text.Json;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionRewardProfileStore
{
    private const int SchemaVersion = 3;
    private const int MaximumSamplesPerDifficulty = 5000;
    private const int MaximumTimingSamples = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly string _path;

    public ExpeditionRewardProfileStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LilacMacro"))
    {
    }

    internal ExpeditionRewardProfileStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _path = Path.Combine(Path.GetFullPath(root), "expedition-reward-profiles.json");
    }

    public async Task RecordPoolAsync(
        int difficulty,
        ExpeditionRewardPool pool,
        CancellationToken cancellationToken = default)
    {
        ValidateDifficulty(difficulty);
        ArgumentNullException.ThrowIfNull(pool);
        if (!pool.IsComplete)
            throw new InvalidDataException("Expedition reward profiles require a complete five-resource pool.");
        Document document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        string key = difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DifficultyProfile profile = document.Difficulties.GetValueOrDefault(key) ?? new DifficultyProfile();
        if (profile.PoolCount >= MaximumSamplesPerDifficulty) Decay(profile);
        foreach (ExpeditionRewardResource resource in Enum.GetValues<ExpeditionRewardResource>()
                     .Where(resource => resource != ExpeditionRewardResource.None))
        {
            string resourceKey = resource.ToString();
            Dictionary<string, int> histogram = profile.Histograms.GetValueOrDefault(resourceKey) ??
                new Dictionary<string, int>(StringComparer.Ordinal);
            string quantity = pool.Quantity(resource).ToString(System.Globalization.CultureInfo.InvariantCulture);
            histogram[quantity] = histogram.GetValueOrDefault(quantity) + 1;
            profile.Histograms[resourceKey] = histogram;
        }
        profile.PoolCount++;
        document.Difficulties[key] = profile;
        await SaveDocumentAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordRerollAsync(
        string device,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        string environmentKey = EnvironmentKey(device);
        if (elapsed <= TimeSpan.Zero || elapsed > TimeSpan.FromMinutes(5)) return;
        Document document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        List<double> samples = document.RerollSeconds.GetValueOrDefault(environmentKey) ?? [];
        samples.Add(elapsed.TotalSeconds);
        if (samples.Count > MaximumTimingSamples) samples.RemoveRange(0, samples.Count - MaximumTimingSamples);
        document.RerollSeconds[environmentKey] = samples;
        await SaveDocumentAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExpeditionRewardOptimization?> OptimizeAsync(
        int difficulty,
        ExpeditionRewardResource resource,
        string device,
        CancellationToken cancellationToken = default)
    {
        ValidateDifficulty(difficulty);
        if (resource == ExpeditionRewardResource.None) return null;
        Document document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        string key = difficulty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DifficultyProfile profile = document.Difficulties.GetValueOrDefault(key) ?? new DifficultyProfile();
        int observationCount = ExpeditionRewardPriorCatalog.PoolCount(difficulty) + profile.PoolCount;
        if (observationCount < ExpeditionRewardPolicy.MinimumOptimizationSamples) return null;
        Dictionary<string, int> histogram = CombinedHistogram(difficulty, resource, profile);
        int[] quantities = histogram.SelectMany(entry => Enumerable.Repeat(
                int.Parse(entry.Key, System.Globalization.CultureInfo.InvariantCulture), entry.Value))
            .ToArray();
        List<double> timings = document.RerollSeconds.GetValueOrDefault(EnvironmentKey(device)) ?? [];
        double rerollSeconds = timings.Count == 0
            ? ExpeditionRewardPolicy.DefaultRerollDuration.TotalSeconds
            : timings.Average();
        return ExpeditionRewardPolicy.Optimize(quantities, TimeSpan.FromSeconds(rerollSeconds));
    }

    public async Task<(int Pools, int Timings, double RerollSeconds)> StatusAsync(
        int difficulty,
        string device,
        CancellationToken cancellationToken = default)
    {
        ValidateDifficulty(difficulty);
        Document document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
        int localPools = (document.Difficulties.GetValueOrDefault(difficulty.ToString()) ?? new DifficultyProfile())
            .PoolCount;
        int pools = ExpeditionRewardPriorCatalog.PoolCount(difficulty) + localPools;
        List<double> timings = document.RerollSeconds.GetValueOrDefault(EnvironmentKey(device)) ?? [];
        return (pools, timings.Count, timings.Count == 0
            ? ExpeditionRewardPolicy.DefaultRerollDuration.TotalSeconds
            : timings.Average());
    }

    private async Task<Document> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new Document();
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using FileStream stream = File.OpenRead(_path);
                Document? loaded = await JsonSerializer.DeserializeAsync<Document>(
                    stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (loaded?.Version == SchemaVersion) return loaded;
                if (loaded?.Version == 2)
                {
                    return new Document
                    {
                        RerollSeconds = loaded.RerollSeconds,
                    };
                }
                return new Document();
            }
            catch (JsonException)
            {
                return new Document();
            }
            catch (Exception error) when (
                (error is IOException or UnauthorizedAccessException) && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task SaveDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateDifficulty(int difficulty)
    {
        if (difficulty is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(difficulty));
    }

    private static string EnvironmentKey(string device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        return device.Trim().ToLowerInvariant();
    }

    private static void Decay(DifficultyProfile profile)
    {
        int retained = 0;
        foreach (Dictionary<string, int> histogram in profile.Histograms.Values)
        {
            foreach (string quantity in histogram.Keys.ToArray())
            {
                int count = histogram[quantity] / 2;
                if (count == 0) histogram.Remove(quantity);
                else histogram[quantity] = count;
            }
            retained = Math.Max(retained, histogram.Values.Sum());
        }
        profile.PoolCount = retained;
    }

    private static Dictionary<string, int> CombinedHistogram(
        int difficulty,
        ExpeditionRewardResource resource,
        DifficultyProfile profile)
    {
        Dictionary<string, int> combined = ExpeditionRewardPriorCatalog.Histogram(difficulty, resource);
        Dictionary<string, int> local = profile.Histograms.GetValueOrDefault(resource.ToString()) ?? [];
        foreach ((string quantity, int count) in local)
        {
            combined[quantity] = combined.GetValueOrDefault(quantity) + count;
        }
        return combined;
    }

    private sealed class Document
    {
        public int Version { get; set; } = SchemaVersion;
        public Dictionary<string, DifficultyProfile> Difficulties { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<double>> RerollSeconds { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class DifficultyProfile
    {
        public int PoolCount { get; set; }
        public Dictionary<string, Dictionary<string, int>> Histograms { get; init; } = new(StringComparer.Ordinal);
    }
}
