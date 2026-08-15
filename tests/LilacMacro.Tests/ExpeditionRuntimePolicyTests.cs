using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class ExpeditionRuntimePolicyTests
{
    [Fact]
    public void MatchArrivalRequiresVisibleStartGamePrompt()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.MatchPrestart,
            [
                Region(600, 120, 120, 24, "Start Game"),
                Region(600, 420, 120, 24, "Start Game"),
            ]).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            DebugWorkflowCatalog.MatchPrestart,
            [Region(800, 450, 48, 22, "Start")]).IsMatch);
    }

    [Fact]
    public void DefenseWaitsForStartBeforeReplayAndToleratesTransientOverlay()
    {
        Assert.True(ExpeditionDefenseStartPolicy.ArrivalMaximumObservations >= 120);
        Assert.InRange(ExpeditionDefenseStartPolicy.ArrivalRetryMilliseconds, 250, 1000);
        Assert.True(ExpeditionDefenseStartPolicy.PostReplayStartAttempts >= 10);
        Assert.True(
            ExpeditionDefenseStartPolicy.PostReplayStartAttempts *
            ExpeditionDefenseStartPolicy.PostReplayRetryMilliseconds >= 3000);
    }

    [Fact]
    public void DefenseStartEpisodeReopensAfterPromptClears()
    {
        ExpeditionDefenseStartEpisodeTracker tracker = new();

        Assert.True(tracker.Observe(startGameVisible: true));
        tracker.MarkHandled();
        Assert.False(tracker.Observe(startGameVisible: true));

        Assert.False(tracker.Observe(startGameVisible: false));
        Assert.True(tracker.Observe(startGameVisible: true));
    }

    [Fact]
    public void TrackerCountsOnlyBossFollowedByCheckpoint()
    {
        ExpeditionRunTracker tracker = new(extractAtCheckpoint: true, bossesBeforeExtract: 1);

        Assert.Equal(ExpeditionNodeAction.Wait, tracker.Observe(ExpeditionNodeType.Boss));
        Assert.Equal(ExpeditionNodeAction.Wait, tracker.Observe(ExpeditionNodeType.Assault));
        Assert.Equal(ExpeditionNodeAction.Continue, tracker.Observe(ExpeditionNodeType.Checkpoint));
        Assert.Equal(0, tracker.RealBossesCompleted);

        Assert.Equal(ExpeditionNodeAction.Wait, tracker.Observe(ExpeditionNodeType.Boss));
        Assert.Equal(ExpeditionNodeAction.Extract, tracker.Observe(ExpeditionNodeType.Checkpoint));
        Assert.Equal(1, tracker.RealBossesCompleted);
    }

    [Fact]
    public void CheckpointSourceReplaysItsLastActionInsteadOfWaitingForever()
    {
        ExpeditionRunTracker tracker = new(extractAtCheckpoint: true, bossesBeforeExtract: 1);

        Assert.Equal(ExpeditionNodeAction.Wait, tracker.Observe(ExpeditionNodeType.Boss));
        Assert.Equal(ExpeditionNodeAction.Extract, tracker.ObserveCheckpointSource());
        Assert.Equal(ExpeditionNodeAction.Extract, tracker.ObserveCheckpointSource());
    }

    [Fact]
    public void LiveControlProbeIsFrequentButStillPeriodic() =>
        Assert.InRange(
            ExpeditionLiveControlPolicy.ProbeIntervalMilliseconds,
            1_000,
            3_000);

    [Fact]
    public void ProgressWatchdogRejectsAnIndefiniteLocalMonitor()
    {
        Assert.False(ExpeditionProgressPolicy.HasStalled(
            ExpeditionProgressPolicy.MaximumSilence - TimeSpan.FromMilliseconds(1)));
        Assert.True(ExpeditionProgressPolicy.HasStalled(
            ExpeditionProgressPolicy.MaximumSilence));
        Assert.InRange(
            ExpeditionProgressPolicy.MaximumSilence,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromMinutes(8));
    }

    [Fact]
    public void CheckpointControlOutranksEncounterControl()
    {
        Assert.Equal(
            ExpeditionLiveControl.Checkpoint,
            ExpeditionLiveControlPolicy.Select(checkpointAvailable: true, encounterAvailable: true));
        Assert.Equal(
            ExpeditionLiveControl.Encounter,
            ExpeditionLiveControlPolicy.Select(checkpointAvailable: false, encounterAvailable: true));
        Assert.Equal(
            ExpeditionLiveControl.None,
            ExpeditionLiveControlPolicy.Select(checkpointAvailable: false, encounterAvailable: false));
    }

    [Theory]
    [InlineData(ExpeditionNodeType.Defense)]
    [InlineData(ExpeditionNodeType.Elite)]
    [InlineData(ExpeditionNodeType.Encounter)]
    [InlineData(ExpeditionNodeType.Checkpoint)]
    public void ActionableSemanticNodesRequireTheirOwnLiveControls(ExpeditionNodeType node) =>
        Assert.True(ExpeditionLiveControlPolicy.RequiresLiveControlEvidence(node));

    [Theory]
    [InlineData(ExpeditionNodeType.Assault)]
    [InlineData(ExpeditionNodeType.Boss)]
    public void PassiveSemanticNodesDoNotRequireAnInputControl(ExpeditionNodeType node) =>
        Assert.False(ExpeditionLiveControlPolicy.RequiresLiveControlEvidence(node));

    [Fact]
    public void EncounterAndLaterCheckpointsAllowForShipArrival()
    {
        Assert.True(ExpeditionNodeArrivalPolicy.MaximumObservations >= 120);
        Assert.InRange(ExpeditionNodeArrivalPolicy.RetryMilliseconds, 250, 1000);
        Assert.True(
            ExpeditionNodeArrivalPolicy.MaximumObservations *
            ExpeditionNodeArrivalPolicy.RetryMilliseconds >= 60_000);
    }

    [Theory]
    [InlineData("Defense", ExpeditionNodeType.Defense)]
    [InlineData("Elite Node", ExpeditionNodeType.Elite)]
    [InlineData("Current: Assault", ExpeditionNodeType.Assault)]
    [InlineData("BOSS", ExpeditionNodeType.Boss)]
    [InlineData("Encounter", ExpeditionNodeType.Encounter)]
    [InlineData("Checkpoint Rewards", ExpeditionNodeType.Checkpoint)]
    public void TooltipParserAcceptsOneKnownNode(string text, ExpeditionNodeType expected) =>
        Assert.Equal(expected, ExpeditionNodeEvidenceService.ParseNode([text]));

    [Fact]
    public void TooltipParserRejectsUnknownAndConflicts()
    {
        Assert.Null(ExpeditionNodeEvidenceService.ParseNode(["Unknown"]));
        Assert.Null(ExpeditionNodeEvidenceService.ParseNode(["Boss", "Checkpoint"]));
    }

    [Fact]
    public void FirstNodeCalibrationSweepsDatasetHoverLineFromTheLeft()
    {
        PixelPoint marker = new(507, 74);
        IReadOnlyList<PixelPoint> probes = ExpeditionNodeEvidenceService.HoverProbePoints(marker, null);

        Assert.Equal(ExpeditionNodeEvidenceService.HoverLine.X, probes[0].X);
        Assert.All(probes, point => Assert.Equal(74, point.Y));
        Assert.True(probes[^1].X >= marker.X);
        Assert.All(probes, point => Assert.InRange(
            point.X,
            ExpeditionNodeEvidenceService.HoverLine.X,
            ExpeditionNodeEvidenceService.HoverLine.Right - 1));
    }

    [Fact]
    public void TooltipClearPointUsesEstablishedBottomRightRestingArea()
    {
        Assert.Equal(
            ShopPurchasePolicy.HoverClearPoint,
            ExpeditionNodeEvidenceService.TooltipClearPoint);
        Assert.True(ExpeditionNodeEvidenceService.TooltipClearPoint.X > 1300);
        Assert.True(ExpeditionNodeEvidenceService.TooltipClearPoint.Y > 650);
    }

    [Fact]
    public void LearnedNodeHoverUsesCachedOffsetThenBoundedLocalSearch()
    {
        PixelPoint marker = new(650, 74);
        IReadOnlyList<PixelPoint> probes = ExpeditionNodeEvidenceService.HoverProbePoints(marker, 12);

        Assert.Equal(new PixelPoint(662, 74), probes[0]);
        Assert.All(probes, point => Assert.InRange(point.X, 630, 694));
        Assert.Equal(probes.Count, probes.Distinct().Count());
    }

    [Fact]
    public void PersonalizedHueRequiresDistanceAndMargin()
    {
        ExpeditionNodeColorProfile profile = new();
        profile.Learn(ExpeditionNodeType.Assault, 12);
        profile.Learn(ExpeditionNodeType.Defense, 40);

        Assert.Equal(ExpeditionNodeType.Assault, profile.Classify(13));
        Assert.Null(profile.Classify(26));
        Assert.Null(profile.Classify(80));
    }

    [Fact]
    public void ColorProfileTracksEverySemanticNodeCalibration()
    {
        ExpeditionNodeColorProfile profile = new();
        profile.Learn(ExpeditionNodeType.Assault, 14);
        profile.Learn(ExpeditionNodeType.Checkpoint, 19);

        Assert.False(profile.IsComplete);

        foreach (ExpeditionNodeType node in Enum.GetValues<ExpeditionNodeType>())
            profile.Learn(node, 10 + (int)node * 20);

        Assert.True(profile.IsComplete);
    }

    [Fact]
    public void NewlyMovedMarkerAlwaysRequiresFreshSemanticEvidence()
    {
        ExpeditionNodeColorProfile profile = new();
        profile.Learn(ExpeditionNodeType.Assault, 14);
        PixelPoint oldMarker = new(185, 23);
        PixelPoint newMarker = new(177, 22);

        Assert.Equal(
            ExpeditionNodeType.Assault,
            ExpeditionNodeEvidenceService.RetainVerifiedMarker(
                oldMarker, 14, oldMarker, ExpeditionNodeType.Assault, 14));
        Assert.Null(ExpeditionNodeEvidenceService.RetainVerifiedMarker(
            newMarker, 19, oldMarker, ExpeditionNodeType.Assault, 14));

        foreach (ExpeditionNodeType node in Enum.GetValues<ExpeditionNodeType>())
            profile.Learn(node, 10 + (int)node * 20);
        Assert.True(profile.IsComplete);
        Assert.Null(ExpeditionNodeEvidenceService.RetainVerifiedMarker(
            newMarker, 19, oldMarker, ExpeditionNodeType.Assault, 14));
    }

    [Fact]
    public void CurrentBarHueExcludesSaturatedSceneRowsAboveTheFill()
    {
        byte[] pixels = new byte[700 * 62 * 3];
        Paint(pixels, 700, new PixelRect(57, 14, 96, 5), 45, 110, 75);
        Paint(pixels, 700, new PixelRect(57, 20, 96, 7), 220, 158, 40);
        RgbImage bar = new(700, 62, pixels, takeOwnership: true);

        double hue = Assert.IsType<double>(
            ExpeditionNodeEvidenceService.CurrentBarHue(bar, new PixelPoint(177, 22)));

        Assert.InRange(hue, 18, 20);
    }

    [Theory]
    [InlineData("Fuel Cell", ExpeditionRewardResource.FuelCell)]
    [InlineData("Equipment Scrap", ExpeditionRewardResource.EquipmentScrap)]
    [InlineData("Equipment Reroll", ExpeditionRewardResource.EquipmentReroll)]
    [InlineData("Equipment Lock", ExpeditionRewardResource.EquipmentLock)]
    [InlineData("Expedition Coin", ExpeditionRewardResource.ExpeditionCoin)]
    public void RewardTargetsParseSupportedResources(
        string text,
        ExpeditionRewardResource expected)
    {
        ExpeditionRewardResource resource = ExpeditionRewardPolicy.ParseResource(text);
        Assert.Equal(expected, resource);
    }

    [Fact]
    public void DynamicRewardOptimizationAdaptsToRerollThroughput()
    {
        int[] quantities = [.. Enumerable.Repeat(1, 900), .. Enumerable.Repeat(10, 100)];

        ExpeditionRewardOptimization fast = ExpeditionRewardPolicy.Optimize(
            quantities, TimeSpan.FromSeconds(1));
        ExpeditionRewardOptimization slow = ExpeditionRewardPolicy.Optimize(
            quantities, TimeSpan.FromMinutes(10));

        Assert.Equal(10, fast.Threshold);
        Assert.True(slow.Threshold < fast.Threshold);
        Assert.Equal(1000, fast.ObservationCount);
    }

    [Fact]
    public void DynamicRewardOptimizationRejectsInvalidObservations() =>
        Assert.Throws<InvalidDataException>(() => ExpeditionRewardPolicy.Optimize(
            [1, -1], TimeSpan.FromSeconds(10)));

    [Fact]
    public async Task RewardProfilesKeepDifficultyAndDeviceTimingSeparate()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lilac-reward-profile-{Guid.NewGuid():N}");
        try
        {
            ExpeditionRewardProfileStore store = new(root);
            ExpeditionRewardPool first = CompleteRewardPool(2);
            ExpeditionRewardPool second = CompleteRewardPool(34);
            await store.RecordPoolAsync(1, first);
            await store.RecordPoolAsync(2, second);
            await store.RecordRerollAsync("cpu", TimeSpan.FromSeconds(8));
            await store.RecordRerollAsync("gpu:0", TimeSpan.FromSeconds(2));

            (int Pools, int Timings, double RerollSeconds) difficulty1 = await store.StatusAsync(1, "cpu");
            (int Pools, int Timings, double RerollSeconds) difficulty2 = await store.StatusAsync(2, "gpu:0");

            Assert.Equal((1020, 1, 8d), difficulty1);
            Assert.Equal((1001, 1, 2d), difficulty2);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("OCR worker failed: [Errno 13] Permission denied", true)]
    [InlineData("OCR worker failed: Access is denied", true)]
    [InlineData("OCR worker failed: invalid model", false)]
    [InlineData("Permission denied", false)]
    public void PersistentOcrAccessRecoveryIsNarrowlyClassified(string message, bool expected) =>
        Assert.Equal(expected, OcrRunner.IsTransientWorkerAccessFailure(new InvalidOperationException(message)));

    [Fact]
    public void PersistentOcrResponseAccessFailureIsRetryable() =>
        Assert.True(OcrRunner.IsTransientWorkerAccessFailure(
            new OcrWorkerResponseAccessException("response unavailable", new IOException())));

    [Fact]
    public async Task PersistentOcrResponseReaderWaitsForExclusiveWindowsHandle()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "response.json");
        await File.WriteAllTextAsync(path, "{\"text\":\"ready\"}");
        FileStream locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Task<string> read = OcrWorkerResponseReader.ReadAsync(path, CancellationToken.None);
            await Task.Delay(OcrWorkerResponseReader.RetryMilliseconds * 3);
            await locked.DisposeAsync();
            string json = await read;

            Assert.Equal("{\"text\":\"ready\"}", json);
        }
        finally
        {
            await locked.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("34x", ExpeditionRewardResource.FuelCell, 34)]
    [InlineData("Zx", ExpeditionRewardResource.FuelCell, 2)]
    [InlineData("12x", ExpeditionRewardResource.EquipmentScrap, 12)]
    [InlineData("1x", ExpeditionRewardResource.EquipmentLock, 1)]
    [InlineData("4x", ExpeditionRewardResource.EquipmentReroll, 4)]
    [InlineData("bx", ExpeditionRewardResource.EquipmentReroll, 1)]
    [InlineData("bx", ExpeditionRewardResource.EquipmentLock, 1)]
    [InlineData("2bx", ExpeditionRewardResource.FuelCell, 21)]
    [InlineData("2kx", ExpeditionRewardResource.EquipmentScrap, 21)]
    [InlineData("tbx", ExpeditionRewardResource.FuelCell, 11)]
    [InlineData("11bx", ExpeditionRewardResource.ExpeditionCoin, 11)]
    public void RewardQuantityParsingIsResourceScoped(
        string text,
        ExpeditionRewardResource resource,
        int expected) =>
        Assert.Equal(expected, ExpeditionRewardPolicy.ParseQuantity(text, resource));

    [Fact]
    public void FuelSpecificCorrectionIsNotGlobal() =>
        Assert.Null(ExpeditionRewardPolicy.ParseQuantity("Zx", ExpeditionRewardResource.EquipmentScrap));

    [Theory]
    [InlineData("bx", ExpeditionRewardResource.FuelCell)]
    [InlineData("2bx", ExpeditionRewardResource.ExpeditionCoin)]
    [InlineData("2kx", ExpeditionRewardResource.ExpeditionCoin)]
    public void AmbiguousRewardGlyphsRemainUnreadable(string text, ExpeditionRewardResource resource) =>
        Assert.Null(ExpeditionRewardPolicy.ParseQuantity(text, resource));

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(1000)]
    public void RouteOptimizerTestTrialsAcceptBoundedCounts(int trials) =>
        Assert.Equal(trials, ExpeditionRewardPolicy.ValidateTestTrials(trials));

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void RouteOptimizerTestTrialsRejectOutOfRangeCounts(int trials) =>
        Assert.Throws<InvalidDataException>(() => ExpeditionRewardPolicy.ValidateTestTrials(trials));

    [Fact]
    public void RewardPoolAssociatesQuantityWithSameCardLabel()
    {
        OcrTextRegion[] regions =
        [
            Region(180, 620, 25, 15, "Zx"),
            Region(202, 660, 34, 15, "Fuel Cell"),
            Region(244, 620, 28, 15, "12x"),
            Region(320, 620, 28, 15, "600x"),
            Region(396, 620, 28, 15, "500x"),
            Region(267, 652, 50, 15, "Equlpm ent"),
            Region(274, 669, 31, 15, "Scrap"),
        ];

        Assert.Equal(
            (ExpeditionRewardResource.FuelCell, 2),
            ExpeditionRewardPoolService.FindReward(regions, ExpeditionRewardResource.FuelCell));
        Assert.Equal(
            (ExpeditionRewardResource.EquipmentScrap, 12),
            ExpeditionRewardPoolService.FindReward(regions, ExpeditionRewardResource.EquipmentScrap));
    }

    [Fact]
    public void RewardPoolDoesNotAssociateNeighboringCardLabel()
    {
        OcrTextRegion[] regions =
        [
            Region(180, 620, 25, 15, "600x"),
            Region(202, 660, 25, 15, "Yen"),
            Region(244, 620, 28, 15, "2x"),
            Region(320, 620, 28, 15, "600x"),
            Region(396, 620, 28, 15, "500x"),
            Region(267, 660, 58, 15, "Equipment Reroll"),
        ];

        Assert.Equal(
            (ExpeditionRewardResource.EquipmentReroll, 2),
            ExpeditionRewardPoolService.FindReward(regions, ExpeditionRewardResource.EquipmentReroll));
    }

    [Fact]
    public void RewardPoolUsesCardBoundariesForSplitAndFuzzyLabels()
    {
        Assert.Equal(ExpeditionRewardResource.EquipmentReroll,
            ExpeditionRewardPoolService.Identify("equipmentrerdl"));
        OcrTextRegion[] regions =
        [
            Region(234, 601, 20, 13, "2bx"),
            Region(308, 601, 15, 13, "bx"),
            Region(383, 601, 16, 13, "bx"),
            Region(459, 601, 42, 13, "14,353x"),
            Region(244, 646, 54, 12, "Equipment"),
            Region(270, 656, 27, 11, "Scrap"),
            Region(319, 645, 54, 13, "Equipment"),
            Region(348, 656, 25, 10, "Lock"),
            Region(394, 647, 54, 12, "Equipment"),
            Region(418, 655, 29, 11, "Rerdl"),
        ];

        Assert.Equal(
            [ExpeditionRewardResource.EquipmentScrap, ExpeditionRewardResource.EquipmentReroll,
                ExpeditionRewardResource.EquipmentLock],
            ExpeditionRewardPoolService.AssociateRewardCards(regions).Keys.Order().ToArray());
        Assert.True(ExpeditionRewardPoolService.TryPoolFromRegions(regions, out ExpeditionRewardPool pool));
        Assert.Equal(21, pool.Quantity(ExpeditionRewardResource.EquipmentScrap));
        Assert.Equal(1, pool.Quantity(ExpeditionRewardResource.EquipmentLock));
        Assert.Equal(1, pool.Quantity(ExpeditionRewardResource.EquipmentReroll));
    }

    [Fact]
    public void AmbiguousCardQuantityRejectsWholePoolObservation()
    {
        OcrTextRegion[] regions =
        [
            Region(234, 601, 20, 13, "2bx"),
            Region(308, 601, 15, 13, "bx"),
            Region(383, 601, 16, 13, "bx"),
            Region(459, 601, 42, 13, "14,353x"),
            Region(244, 646, 54, 12, "Expedition"),
            Region(270, 656, 27, 11, "Coin"),
        ];

        Assert.False(ExpeditionRewardPoolService.TryPoolFromRegions(regions, out _));
    }

    [Fact]
    public void ReliableTargetSurvivesUnrelatedAmbiguousQuantity()
    {
        OcrTextRegion[] regions =
        [
            Region(233, 601, 23, 16, "15x"),
            Region(310, 602, 16, 13, "7x"),
            Region(383, 601, 17, 14, "2x"),
            Region(459, 601, 44, 13, "23,943x"),
            Region(533, 601, 22, 14, "2bx"),
            Region(609, 601, 30, 12, "280x"),
            Region(682, 601, 15, 14, "bx"),
            Region(758, 601, 30, 12, "500x"),
            Region(244, 646, 54, 12, "Equipment"),
            Region(270, 655, 26, 11, "Scrap"),
            Region(330, 654, 42, 12, "Fuelcell"),
            Region(394, 647, 53, 12, "Equipment"),
            Region(418, 656, 29, 10, "Rerdll"),
            Region(546, 645, 51, 12, "Expedition"),
            Region(574, 655, 24, 12, "Coin"),
        ];

        Assert.True(ExpeditionRewardPoolService.TryTargetPoolFromRegions(
            regions,
            ExpeditionRewardResource.EquipmentReroll,
            out ExpeditionRewardPool pool,
            out bool completePool));
        Assert.False(completePool);
        Assert.Equal(2, pool.Quantity(ExpeditionRewardResource.EquipmentReroll));
    }

    [Fact]
    public void VerifiedRouteWithoutTargetRewardMeansZeroRatherThanReadFailure()
    {
        ExpeditionRewardPool pool = ExpeditionRewardPoolService.PoolForObservation(
            ExpeditionRewardResource.FuelCell,
            null);

        Assert.Equal(0, pool.Quantity(ExpeditionRewardResource.FuelCell));
        Assert.False(ExpeditionRewardPolicy.Accepts(
            pool,
            ExpeditionRewardResource.FuelCell,
            minimum: 34));
    }

    [Fact]
    public void MissingTargetRequiresPopulatedRewardStripEvidence()
    {
        Assert.True(ExpeditionRewardPoolService.HasPopulatedRewardStrip(
        [
            Region(200, 620, 20, 15, "12x"),
            Region(260, 620, 20, 15, "2x"),
            Region(320, 620, 20, 15, "32x"),
            Region(380, 620, 20, 15, "600x"),
        ]));
        Assert.False(ExpeditionRewardPoolService.HasPopulatedRewardStrip(
        [
            Region(200, 620, 20, 15, "12x"),
            Region(260, 620, 20, 15, "2x"),
            Region(320, 620, 20, 15, "32x"),
        ]));
    }

    [Fact]
    public void RouteTransitionRequiresExactBackButton()
    {
        Assert.True(ExpeditionRewardPoolService.HasBackButton([Region(90, 610, 50, 20, "Back")]));
        Assert.False(ExpeditionRewardPoolService.HasBackButton([Region(90, 610, 80, 20, "Backpack")]));
    }

    [Fact]
    public void RestartTransitionRequiresBothConfirmationActions()
    {
        Assert.True(ExpeditionSettingsService.HasRestartConfirmation(
            [Region(500, 350, 80, 20, "Restart"), Region(650, 350, 70, 20, "Cancel")]));
        Assert.False(ExpeditionSettingsService.HasRestartConfirmation(
            [Region(500, 350, 80, 20, "Restart")]));
    }

    [Fact]
    public void CheckpointSourceAndConfirmationOwnSeparateDatasetRegions()
    {
        Assert.Equal("Continue Button", ExpeditionCheckpointStateCatalog.SpawnContinueSource.RegionLabel);
        Assert.Equal("Button Area", ExpeditionCheckpointStateCatalog.ContinueSource.RegionLabel);
        Assert.Equal("Continue Confirm", ExpeditionCheckpointStateCatalog.ContinueConfirmation.RegionLabel);
        Assert.Equal("Button Area", ExpeditionCheckpointStateCatalog.ExtractSource.RegionLabel);
        Assert.Equal("Confirm Area", ExpeditionCheckpointStateCatalog.ExtractConfirmation.RegionLabel);
        Assert.NotEqual(
            ExpeditionCheckpointStateCatalog.ContinueSource.RegionLabel,
            ExpeditionCheckpointStateCatalog.ContinueConfirmation.RegionLabel);
        Assert.NotEqual(
            ExpeditionCheckpointStateCatalog.ExtractSource.RegionLabel,
            ExpeditionCheckpointStateCatalog.ExtractConfirmation.RegionLabel);
        Assert.Equal("Continue Button", ExpeditionCheckpointStateCatalog.EncounterContinueSource.RegionLabel);
        Assert.Equal(
            "Continue Confirm",
            ExpeditionCheckpointStateCatalog.EncounterContinueConfirmation.RegionLabel);
        Assert.NotEqual(
            ExpeditionCheckpointStateCatalog.EncounterContinueSource.RegionLabel,
            ExpeditionCheckpointStateCatalog.EncounterContinueConfirmation.RegionLabel);
    }

    [Fact]
    public void SpawnCheckpointSourceRequiresOnlyItsImmediateContinueControl()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.SpawnContinueSource,
            [Region(661, 507, 69, 20, "Continue"), Region(674, 557, 36, 21, "850")]).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.ContinueSource,
            [Region(661, 507, 69, 20, "Continue"), Region(674, 557, 36, 21, "850")]).IsMatch);
    }

    [Fact]
    public void ContinueSourceRequiresTheCompleteCheckpointButtonPair()
    {
        Assert.False(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.ContinueSource,
            [Region(733, 508, 41, 19, "Conti")]).IsMatch);
        Assert.True(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.ContinueSource,
            [
                Region(596, 509, 56, 18, "Extract"),
                Region(733, 508, 69, 19, "Continue"),
            ]).IsMatch);
    }

    [Fact]
    public void ContinueSourceAcceptsObservedTruncatedExtractWithIndependentContinue()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.ContinueSource,
            [
                Region(596, 509, 42, 18, "Extr"),
                Region(733, 508, 69, 19, "Continue"),
            ]).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.ContinueSource,
            [Region(596, 509, 42, 18, "Extr")]).IsMatch);
    }

    [Fact]
    public void ContinueConfirmationRejectsTheBackgroundContinueControl()
    {
        Assert.False(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.ContinueConfirmation,
            [Region(663, 509, 67, 18, "Continue")]).IsMatch);
        Assert.True(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.ContinueConfirmation,
            [
                Region(620, 281, 160, 20, "Continue Expedition"),
                Region(555, 392, 64, 18, "Continue"),
                Region(753, 391, 53, 21, "Cancel"),
            ]).IsMatch);
    }

    [Fact]
    public void EncounterContinueSourceAndConfirmationRemainSeparateStates()
    {
        Assert.True(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.EncounterContinueSource,
            [Region(590, 485, 90, 22, "Continue")]).IsMatch);
        Assert.False(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.EncounterContinueConfirmation,
            [Region(590, 485, 90, 22, "Continue")]).IsMatch);
        Assert.True(DebugOcrStateRunner.Evaluate(
            ExpeditionCheckpointStateCatalog.EncounterContinueConfirmation,
            [
                Region(620, 281, 160, 20, "Continue Expedition"),
                Region(555, 392, 64, 18, "Continue"),
                Region(753, 391, 53, 21, "Cancel"),
            ]).IsMatch);
    }

    [Fact]
    public void CurrentStartGameEvidenceCoversAllRecordedUiScales()
    {
        Assert.EndsWith(
            "new-start-game-button-20260814-082314",
            DebugWorkflowCatalog.MatchPrestart.DatasetDirectory,
            StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], DebugWorkflowCatalog.MatchPrestart.RegionFrames);
        Assert.Equal("match prestart", DebugWorkflowCatalog.MatchPrestart.RegionLabel);
    }

    [Fact]
    public void RetainedCheckpointSourceIsRetriedOnlyAfterStablePastStateEvidence()
    {
        Assert.Equal(
            CheckpointTransitionDecision.ObserveAgain,
            CheckpointTransitionPolicy.Decide(false, true, true, 1, 0));
        Assert.Equal(
            CheckpointTransitionDecision.OpenConfirmation,
            CheckpointTransitionPolicy.Decide(false, true, true, 2, 0));
        Assert.Equal(
            CheckpointTransitionDecision.Complete,
            CheckpointTransitionPolicy.Decide(false, false, true, 0, 2));
    }

    [Fact]
    public void CheckpointActionLimitStillAllowsPostActionVerification()
    {
        Assert.False(CheckpointTransitionPolicy.CanAct(
            CheckpointTransitionPolicy.MaximumActions));
        Assert.True(CheckpointTransitionPolicy.CanObserve(0));
        Assert.False(CheckpointTransitionPolicy.CanObserve(
            CheckpointTransitionPolicy.MaximumIndeterminateObservations));
    }

    [Fact]
    public void ModalActionIsPairedWithCancelInsteadOfSelectedByScreenOrder()
    {
        OcrTextRegion expected = Region(500, 350, 80, 20, "Restart");
        OcrTextRegion? actual = ModalActionLocator.FindPairedAction(
            [
                Region(510, 275, 120, 20, "Restart Game"),
                expected,
                Region(650, 350, 70, 20, "Cancel"),
                Region(500, 510, 80, 20, "Restart"),
            ],
            text => text.Contains("Restart", StringComparison.OrdinalIgnoreCase),
            text => text.Contains("Cancel", StringComparison.OrdinalIgnoreCase));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void DuplicateShopHeadingCannotImpersonateTheStackedSelector()
    {
        OcrTextRegion heading = Region(40, 60, 150, 20, "Gold Shop");
        OcrTextRegion selector = Region(400, 286, 62, 19, "Gold Shop");
        OcrTextRegion? actual = ModalActionLocator.FindStackedSelector(
            [heading, selector, Region(393, 342, 81, 19, "Cosmetic Shop")],
            text => OcrRuleEngine.Normalize(text) is "goldshop",
            text => OcrRuleEngine.Normalize(text) is "cosmeticshop");

        Assert.Same(selector, actual);
    }

    [Fact]
    public void MultiScaleDatasetLocatesCurrentMarker()
    {
        string? root = FindDataset("expedition-node-set4-20260812-204347");
        if (root is null) return;
        int[] expectedX = [480, 480, 515, 515, 550, 550];
        for (int index = 0; index < expectedX.Length; index++)
        {
            RgbImage full = LoadPng(Path.Combine(root, "images", $"frame-{index + 1:D4}.png"));
            RgbImage band = Crop(full, ExpeditionNodeEvidenceService.BarBand);
            LilacMacro.Core.Geometry.PixelPoint marker = Assert.IsType<LilacMacro.Core.Geometry.PixelPoint>(
                ExpeditionNodeEvidenceService.FindCurrentMarker(band));
            Console.WriteLine($"frame {index + 1}: marker={ExpeditionNodeEvidenceService.BarBand.X + marker.X},{ExpeditionNodeEvidenceService.BarBand.Y + marker.Y}");
            Assert.InRange(ExpeditionNodeEvidenceService.BarBand.X + marker.X, expectedX[index] - 24, expectedX[index] + 24);
            double hue = Assert.IsType<double>(ExpeditionNodeEvidenceService.CurrentBarHue(band, marker));
            Assert.InRange(hue, 17, 21);
        }
    }

    private static string? FindDataset(string name)
    {
        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LilacMacro Datasets",
            name);
        if (Directory.Exists(local)) return local;

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string bundled = Path.Combine(
                directory.FullName,
                "src",
                "LilacMacro.App",
                "Assets",
                "RuntimeEvidence",
                name);
            if (Directory.Exists(bundled)) return bundled;
            directory = directory.Parent;
        }
        return null;
    }

    private static OcrTextRegion Region(int x, int y, int width, int height, string text) => new()
    {
        Bounds = new PixelRect(x, y, width, height),
        Text = text,
        RecognitionConfidence = 0.99,
    };

    private static ExpeditionRewardPool CompleteRewardPool(int fuelCell) => new(
        Enum.GetValues<ExpeditionRewardResource>()
            .Where(resource => resource != ExpeditionRewardResource.None)
            .ToDictionary(resource => resource,
                resource => resource == ExpeditionRewardResource.FuelCell ? fuelCell : 0));

    private static RgbImage Crop(RgbImage image, LilacMacro.Core.Geometry.PixelRect region)
    {
        byte[] pixels = new byte[region.Width * region.Height * 3];
        for (int y = 0; y < region.Height; y++)
            Buffer.BlockCopy(image.Pixels, ((region.Y + y) * image.Size.Width + region.X) * 3,
                pixels, y * region.Width * 3, region.Width * 3);
        return new RgbImage(region.Width, region.Height, pixels, takeOwnership: true);
    }

    private static void Paint(
        byte[] pixels,
        int width,
        PixelRect region,
        byte red,
        byte green,
        byte blue)
    {
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X; x < region.Right; x++)
            {
                int offset = (y * width + x) * 3;
                pixels[offset] = red;
                pixels[offset + 1] = green;
                pixels[offset + 2] = blue;
            }
    }

    private static RgbImage LoadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame source = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        FormatConvertedBitmap converted = new(source, PixelFormats.Rgb24, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 3];
        converted.CopyPixels(pixels, converted.PixelWidth * 3, 0);
        return new RgbImage(converted.PixelWidth, converted.PixelHeight, pixels, takeOwnership: true);
    }
}
