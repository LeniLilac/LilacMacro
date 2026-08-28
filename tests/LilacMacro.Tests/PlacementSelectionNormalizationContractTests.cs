namespace LilacMacro.Tests;

public sealed class PlacementSelectionNormalizationContractTests
{
    [Fact]
    public void EveryPlacementAttemptNormalizesBeforeSelectingAndVerifying()
    {
        string source = ReadSource("PlacementPlaybackService.cs");
        int loop = source.IndexOf(
            "for (int attempt = 1; attempt <= PlacementSelectionRetryPolicy.MaximumAttempts && !selected; attempt++)",
            StringComparison.Ordinal);
        int initialQuickPlace = source.LastIndexOf(
            "await workspace.RunQuickPlacementBatchAsync(", loop, StringComparison.Ordinal);
        int retryQuickPlace = source.IndexOf(
            "await workspace.RunQuickPlacementBatchAsync(", loop, StringComparison.Ordinal);
        int normalize = source.IndexOf("_panel.NormalizeSelectionAsync(", loop, StringComparison.Ordinal);
        int select = source.IndexOf("await workspace.ClickRobloxAsync(", loop, StringComparison.Ordinal);
        int calibrate = source.IndexOf("_panel.CalibrateAsync(", loop, StringComparison.Ordinal);

        Assert.True(initialQuickPlace >= 0 && initialQuickPlace < loop);
        Assert.True(loop < retryQuickPlace);
        Assert.True(retryQuickPlace < normalize);
        Assert.True(normalize < select);
        Assert.True(select < calibrate);
    }

    [Fact]
    public void NormalizationReobservesBeforeTheBoundedIdleClick()
    {
        string source = ReadSource("UnitPanelEvidenceService.cs");
        int normalize = source.IndexOf("NormalizeSelectionAsync(", StringComparison.Ordinal);
        int observe = source.IndexOf("await workspace.CaptureLiveFrameAsync(", normalize, StringComparison.Ordinal);
        int actionPoint = source.IndexOf("UnitPanelDismissalPolicy.ActionPoint(", observe, StringComparison.Ordinal);
        int idleClick = source.IndexOf("await workspace.ClickRobloxAsync(", observe, StringComparison.Ordinal);
        int evidenceGuard = source.IndexOf("if (layout is not null)", idleClick, StringComparison.Ordinal);
        int verifyHidden = source.IndexOf(
            "await DismissAsync(layout, status, cancellationToken);",
            evidenceGuard,
            StringComparison.Ordinal);

        Assert.True(normalize >= 0);
        Assert.True(normalize < observe);
        Assert.True(observe < idleClick);
        Assert.True(idleClick < actionPoint);
        Assert.True(actionPoint < evidenceGuard);
        Assert.True(evidenceGuard < verifyHidden);
    }

    [Fact]
    public void ExhaustedPlacementProofNamesBothUnresolvedFailureClasses()
    {
        string source = ReadSource("PlacementPlaybackService.cs");

        Assert.Contains("GAME PLACEMENT REJECTION", source, StringComparison.Ordinal);
        Assert.Contains("PANEL-PROOF FAILURE IS UNRESOLVED", source, StringComparison.Ordinal);
        Assert.Contains("INCLUDING COST OR PLACEMENT LIMIT", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string fileName) => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "LilacMacro.App",
        "Runtime",
        fileName));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "runtime-evidence.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the LilacMacro repository root.");
    }
}
