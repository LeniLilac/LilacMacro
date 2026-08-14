using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Tests;

public sealed class ExpeditionRuntimePolicyTests
{
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

    [Theory]
    [InlineData("School Grounds", 350, 700)]
    [InlineData("Flower Forest", 350, 700)]
    [InlineData("Rose Kingdom", 1000, 700)]
    [InlineData("East Town", 700, 700)]
    public void EncounterMovementMatchesFieldTiming(string map, int forward, int right)
    {
        ExpeditionEncounterMovement movement = ExpeditionEncounterPolicy.ForMap(map);
        Assert.Equal(forward, movement.ForwardMilliseconds);
        Assert.Equal(right, movement.RightMilliseconds);
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
    public void PersonalizedHueRequiresDistanceAndMargin()
    {
        ExpeditionNodeColorProfile profile = new();
        profile.Learn(ExpeditionNodeType.Assault, 12);
        profile.Learn(ExpeditionNodeType.Defense, 40);

        Assert.Equal(ExpeditionNodeType.Assault, profile.Classify(13));
        Assert.Null(profile.Classify(26));
        Assert.Null(profile.Classify(80));
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
            ExpeditionRewardPool first = new(new Dictionary<ExpeditionRewardResource, int>
            {
                [ExpeditionRewardResource.FuelCell] = 2,
            });
            ExpeditionRewardPool second = new(new Dictionary<ExpeditionRewardResource, int>
            {
                [ExpeditionRewardResource.FuelCell] = 34,
            });
            await store.RecordPoolAsync(1, first);
            await store.RecordPoolAsync(2, second);
            await store.RecordRerollAsync("cpu", TimeSpan.FromSeconds(8));
            await store.RecordRerollAsync("gpu:0", TimeSpan.FromSeconds(2));

            (int Pools, int Timings, double RerollSeconds) difficulty1 = await store.StatusAsync(1, "cpu");
            (int Pools, int Timings, double RerollSeconds) difficulty2 = await store.StatusAsync(2, "gpu:0");

            Assert.Equal((1, 1, 8d), difficulty1);
            Assert.Equal((1, 1, 2d), difficulty2);
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
    [InlineData("3bx", ExpeditionRewardResource.EquipmentScrap)]
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
            Region(234, 601, 20, 13, "3bx"),
            Region(308, 601, 15, 13, "bx"),
            Region(383, 601, 16, 13, "bx"),
            Region(459, 601, 42, 13, "14,353x"),
            Region(244, 646, 54, 12, "Equipment"),
            Region(270, 656, 27, 11, "Scrap"),
        ];

        Assert.False(ExpeditionRewardPoolService.TryPoolFromRegions(regions, out _));
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
            Assert.NotNull(ExpeditionNodeEvidenceService.CurrentBarHue(band, marker));
        }
    }

    private static string? FindDataset(string name)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LilacMacro Datasets",
            name);
        return Directory.Exists(path) ? path : null;
    }

    private static OcrTextRegion Region(int x, int y, int width, int height, string text) => new()
    {
        Bounds = new PixelRect(x, y, width, height),
        Text = text,
        RecognitionConfidence = 0.99,
    };

    private static RgbImage Crop(RgbImage image, LilacMacro.Core.Geometry.PixelRect region)
    {
        byte[] pixels = new byte[region.Width * region.Height * 3];
        for (int y = 0; y < region.Height; y++)
            Buffer.BlockCopy(image.Pixels, ((region.Y + y) * image.Size.Width + region.X) * 3,
                pixels, y * region.Width * 3, region.Width * 3);
        return new RgbImage(region.Width, region.Height, pixels, takeOwnership: true);
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
