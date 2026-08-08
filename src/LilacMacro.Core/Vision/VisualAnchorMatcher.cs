using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Vision;

public sealed class VisualAnchorMatcher
{
    public VisualAnchorMatchResult Match(
        GrayImage frame,
        VisualAnchorProfile profile,
        PixelRect expectedBounds,
        VisualAnchorMatcherOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(profile);
        options ??= new VisualAnchorMatcherOptions();
        profile.Validate();
        options.Validate();
        if (!expectedBounds.IsInside(new PixelSize(frame.Width, frame.Height)))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedBounds));
        }

        if (profile.Strategy == VisualAnchorStrategy.OcrOnly)
        {
            return new VisualAnchorMatchResult(
                VisualAnchorMatchStatus.RequiresOcr, null, 0, 0, 0, -1, 0, 0);
        }

        List<Candidate> candidates = [];
        foreach (double scale in options.ScaleFactors.Distinct())
        {
            int width = Math.Max(8, (int)Math.Round(expectedBounds.Width * scale));
            int height = Math.Max(8, (int)Math.Round(expectedBounds.Height * scale));
            int centerX = expectedBounds.X + expectedBounds.Width / 2;
            int centerY = expectedBounds.Y + expectedBounds.Height / 2;
            for (int yOffset = -options.VerticalSearchRadius; yOffset <= options.VerticalSearchRadius; yOffset += options.SearchStep)
            {
                for (int xOffset = -options.HorizontalSearchRadius; xOffset <= options.HorizontalSearchRadius; xOffset += options.SearchStep)
                {
                    PixelRect bounds = new(centerX + xOffset - width / 2, centerY + yOffset - height / 2, width, height);
                    if (!bounds.IsInside(new PixelSize(frame.Width, frame.Height))) continue;
                    candidates.Add(Score(frame, profile, bounds));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return new VisualAnchorMatchResult(
                VisualAnchorMatchStatus.BelowThreshold, null, 0, 0, 0, -1, 0, 0);
        }

        Candidate best = candidates.MaxBy(candidate => candidate.Score)!;
        double minimumSeparation = Math.Max(best.Bounds.Width, best.Bounds.Height) * 0.55;
        Candidate? second = candidates
            .Where(candidate => CenterDistance(candidate.Bounds, best.Bounds) >= minimumSeparation)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        double margin = second is null ? 1 : best.Score - second.Score;
        VisualAnchorMatchStatus status = best.Score < options.MinimumScore
            ? VisualAnchorMatchStatus.BelowThreshold
            : margin < options.MinimumDistinctMargin
                ? VisualAnchorMatchStatus.Ambiguous
                : VisualAnchorMatchStatus.Matched;
        return new VisualAnchorMatchResult(
            status,
            best.Bounds,
            best.Score,
            best.GrayScore,
            best.EdgeScore,
            best.PhaseIndex,
            candidates.Count,
            margin);
    }

    private static Candidate Score(GrayImage frame, VisualAnchorProfile profile, PixelRect bounds)
    {
        GrayImage candidate = VisualImageMath.CropAndResize(
            frame, bounds, profile.MedianTemplate.Width, profile.MedianTemplate.Height);
        GrayImage candidateEdges = VisualImageMath.Edges(candidate);
        double bestGray = WeightedSimilarity(candidate, profile.MedianTemplate, profile.GrayReliability);
        int phaseIndex = -1;
        for (int index = 0; index < profile.PhaseTemplates.Count; index++)
        {
            double phase = WeightedSimilarity(candidate, profile.PhaseTemplates[index], profile.GrayReliability);
            if (phase > bestGray)
            {
                bestGray = phase;
                phaseIndex = index;
            }
        }

        double edge = WeightedSimilarity(candidateEdges, profile.EdgeTemplate, profile.EdgeReliability);
        (double grayWeight, double edgeWeight) = profile.Strategy switch
        {
            VisualAnchorStrategy.StableAppearance => (0.55, 0.45),
            VisualAnchorStrategy.MultiPhase => (0.40, 0.60),
            _ => (0.25, 0.75),
        };
        return new Candidate(bounds, grayWeight * bestGray + edgeWeight * edge, bestGray, edge, phaseIndex);
    }

    private static double WeightedSimilarity(GrayImage candidate, GrayImage template, GrayImage weights)
    {
        ReadOnlySpan<byte> first = candidate.PixelSpan;
        ReadOnlySpan<byte> second = template.PixelSpan;
        ReadOnlySpan<byte> weight = weights.PixelSpan;
        double totalWeight = 0;
        double firstMean = 0;
        double secondMean = 0;
        for (int index = 0; index < first.Length; index++)
        {
            double current = weight[index] / 255d;
            totalWeight += current;
            firstMean += first[index] * current;
            secondMean += second[index] * current;
        }

        firstMean /= totalWeight;
        secondMean /= totalWeight;
        double numerator = 0;
        double firstVariance = 0;
        double secondVariance = 0;
        double absoluteError = 0;
        for (int index = 0; index < first.Length; index++)
        {
            double current = weight[index] / 255d;
            double firstDelta = first[index] - firstMean;
            double secondDelta = second[index] - secondMean;
            numerator += current * firstDelta * secondDelta;
            firstVariance += current * firstDelta * firstDelta;
            secondVariance += current * secondDelta * secondDelta;
            absoluteError += current * Math.Abs(first[index] - second[index]);
        }

        double denominator = Math.Sqrt(firstVariance * secondVariance);
        if (denominator < 0.0001) return Math.Clamp(1 - absoluteError / (totalWeight * 255d), 0, 1);
        return Math.Clamp((numerator / denominator + 1) / 2, 0, 1);
    }

    private static double CenterDistance(PixelRect first, PixelRect second)
    {
        double x = first.Center.X - second.Center.X;
        double y = first.Center.Y - second.Center.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private sealed record Candidate(
        PixelRect Bounds,
        double Score,
        double GrayScore,
        double EdgeScore,
        int PhaseIndex);
}
