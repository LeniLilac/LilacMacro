namespace LilacMacro.Core.Vision;

public sealed class VisualFingerprintBuilder
{
    public VisualAnchorProfile Build(
        VisualAnchorDefinition definition,
        IReadOnlyList<VisualAnchorSample> samples,
        DateTimeOffset builtAtUtc,
        VisualFingerprintBuilderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(samples);
        options ??= new VisualFingerprintBuilderOptions();
        definition.Validate();
        options.Validate();
        if (samples.Count < options.MinimumSamples)
        {
            throw new ArgumentException($"At least {options.MinimumSamples} aligned samples are required.", nameof(samples));
        }

        foreach (VisualAnchorSample sample in samples) sample.Validate();
        int clientWidth = samples[0].Frame.Width;
        int clientHeight = samples[0].Frame.Height;
        if (samples.Any(sample => sample.Frame.Width != clientWidth || sample.Frame.Height != clientHeight))
        {
            throw new ArgumentException("Every sample must use the same client dimensions.", nameof(samples));
        }

        int referenceWidth = Median(samples.Select(sample => sample.Bounds.Width));
        int referenceHeight = Median(samples.Select(sample => sample.Bounds.Height));
        int canonicalWidth = Math.Min(referenceWidth, options.MaximumCanonicalWidth);
        int canonicalHeight = Math.Min(referenceHeight, options.MaximumCanonicalHeight);
        if (canonicalWidth < 8 || canonicalHeight < 8)
        {
            throw new ArgumentException("Aligned OCR bounds must be at least 8 by 8 pixels.", nameof(samples));
        }

        GrayImage[] aligned = samples
            .Select(sample => VisualImageMath.CropAndResize(sample.Frame, sample.Bounds, canonicalWidth, canonicalHeight))
            .ToArray();
        GrayImage[] edges = aligned.Select(VisualImageMath.Edges).ToArray();
        GrayImage median = VisualImageMath.Median(aligned);
        GrayImage edgeMedian = VisualImageMath.Median(edges);
        (double[] grayDeviation, double meanDeviation) = VisualImageMath.StandardDeviation(aligned);
        (double[] edgeDeviation, _) = VisualImageMath.StandardDeviation(edges);
        GrayImage grayReliability = VisualImageMath.Reliability(
            canonicalWidth, canonicalHeight, grayDeviation, options.DynamicPixelStandardDeviation);
        GrayImage edgeReliability = VisualImageMath.Reliability(
            canonicalWidth, canonicalHeight, edgeDeviation, options.DynamicPixelStandardDeviation);

        double stableRatio = grayDeviation.Count(value => value <= options.StablePixelStandardDeviation) /
            (double)grayDeviation.Length;
        double dynamicRatio = grayDeviation.Count(value => value >= options.DynamicPixelStandardDeviation) /
            (double)grayDeviation.Length;
        double edgeEnergy = edgeMedian.PixelSpan.ToArray().Average(value => value) / 255d;
        GrayImage[] phases = SelectPhases(aligned, options);
        VisualAnchorStrategy strategy = SelectStrategy(stableRatio, dynamicRatio, edgeEnergy, phases.Length);
        VisualFingerprintMetrics metrics = new(
            meanDeviation,
            stableRatio,
            dynamicRatio,
            edgeEnergy,
            phases.Length);

        VisualAnchorProfile profile = new(
            definition,
            Guid.NewGuid(),
            builtAtUtc,
            strategy,
            clientWidth,
            clientHeight,
            samples.Count,
            referenceWidth,
            referenceHeight,
            median,
            edgeMedian,
            grayReliability,
            edgeReliability,
            phases,
            metrics);
        profile.Validate();
        return profile;
    }

    private static GrayImage[] SelectPhases(
        IReadOnlyList<GrayImage> samples,
        VisualFingerprintBuilderOptions options)
    {
        List<GrayImage> selected = [samples[0]];
        while (selected.Count < Math.Min(options.MaximumPhaseTemplates, samples.Count))
        {
            GrayImage? next = null;
            double farthest = 0;
            foreach (GrayImage sample in samples)
            {
                double nearest = selected.Min(existing => VisualImageMath.MeanAbsoluteDistance(sample, existing));
                if (nearest > farthest)
                {
                    farthest = nearest;
                    next = sample;
                }
            }

            if (next is null || farthest < options.PhaseDistanceThreshold) break;
            selected.Add(next);
        }

        return selected.ToArray();
    }

    private static VisualAnchorStrategy SelectStrategy(
        double stableRatio,
        double dynamicRatio,
        double edgeEnergy,
        int phaseCount)
    {
        if (edgeEnergy < 0.012 && stableRatio < 0.20) return VisualAnchorStrategy.OcrOnly;
        if (dynamicRatio < 0.08) return VisualAnchorStrategy.StableAppearance;
        if (phaseCount > 1 && dynamicRatio >= 0.20) return VisualAnchorStrategy.MultiPhase;
        return VisualAnchorStrategy.AnimatedAppearance;
    }

    private static int Median(IEnumerable<int> values)
    {
        int[] ordered = values.Order().ToArray();
        return ordered[ordered.Length / 2];
    }
}
