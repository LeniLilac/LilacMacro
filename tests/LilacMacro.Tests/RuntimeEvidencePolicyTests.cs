using System.Reflection;
using System.Text.Json;
using LilacMacro.App.Debugging;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class RuntimeEvidencePolicyTests
{
    [Fact]
    public async Task EveryRuntimeStateUsesOneExplicitBundledAnnotation()
    {
        string evidenceRoot = EvidenceRoot();
        DatasetStore store = new();
        DebugStateDatasetContextLoader contexts = new();

        foreach (DebugStateSpec state in StateSpecs())
        {
            Assert.False(string.IsNullOrWhiteSpace(state.RegionLabel));
            Assert.Equal(evidenceRoot, Path.GetDirectoryName(Path.GetFullPath(state.DatasetDirectory)));

            DatasetLocation dataset = await LoadAsync(store, state.DatasetDirectory);
            foreach (int frameNumber in state.RegionFrames)
            {
                Assert.InRange(frameNumber, 1, dataset.Manifest.Frames.Count);
                BoxAnnotation[] matches = dataset.Manifest.Frames[frameNumber - 1].Annotations
                    .Where(annotation => string.Equals(
                        annotation.Label, state.RegionLabel, StringComparison.Ordinal))
                    .ToArray();
                Assert.Single(matches);
            }

            DebugStateDatasetContext context = await contexts.LoadAsync(
                state, CancellationToken.None);
            Assert.True(context.RegionOfInterest.Width > 0);
            Assert.True(context.RegionOfInterest.Height > 0);
        }
    }

    [Fact]
    public async Task EmbeddedContextCatalogMatchesRepositoryEvidenceForEveryState()
    {
        DebugStateDatasetContextLoader contexts = new();
        string absentRoot = Path.Combine(
            Path.GetTempPath(), $"LilacMacro-embedded-context-{Guid.NewGuid():N}");

        foreach (DebugStateSpec state in StateSpecs())
        {
            DebugStateDatasetContext repository = await contexts.LoadAsync(
                state, CancellationToken.None);
            DebugStateDatasetContext embedded = await contexts.LoadAsync(
                state with
                {
                    DatasetDirectory = Path.Combine(
                        absentRoot, Path.GetFileName(state.DatasetDirectory)),
                },
                CancellationToken.None);

            Assert.Equal(repository.RegionOfInterest, embedded.RegionOfInterest);
            Assert.Equal(repository.VisualAnchors, embedded.VisualAnchors);
        }
    }

    [Fact]
    public async Task TowerPreviewMapAndFloorUsesItsDedicatedThreeScaleOwner()
    {
        DebugStateDatasetContextLoader contexts = new();

        DebugStateDatasetContext preview = await contexts.LoadAsync(
            TowerWorkflowCatalog.TowerPreviewMapFloor, CancellationToken.None);
        DebugStateDatasetContext stage = await contexts.LoadAsync(
            TowerWorkflowCatalog.TowerStage, CancellationToken.None);

        Assert.Equal(new PixelRect(595, 136, 699, 137), preview.RegionOfInterest);
        Assert.NotEqual(stage.RegionOfInterest, preview.RegionOfInterest);
        Assert.Equal(3, TowerWorkflowCatalog.TowerPreviewMapFloor.RegionFrames.Count);
        Assert.Equal("Select Stage", Assert.Single(TowerWorkflowCatalog.TowerStage.Targets).Name);
        Assert.Null(TowerWorkflowCatalog.TowerStage.PoolTargetNames);
    }

    [Fact]
    public void TowerPreviewAcceptsCombinedFloorAndMapTextFromLiveOcr()
    {
        OcrStateEvaluation evaluation = DebugOcrStateRunner.Evaluate(
            TowerWorkflowCatalog.TowerPreviewMapFloor,
            [new OcrTextRegion
            {
                Bounds = new PixelRect(807, 206, 232, 23),
                Text = "Floor 6 - School Grounds",
                DetectionConfidence = 1,
                RecognitionConfidence = 0.9748940467834473,
            }]);

        Assert.True(evaluation.IsMatch);
        Assert.Contains(evaluation.Matches, match => match.Target == "Floor");
        Assert.Contains(evaluation.Matches, match => match.Target == "School Grounds");
    }

    [Fact]
    public async Task EveryStaticSearchRegionEqualsItsBundledAnnotation()
    {
        RuntimeSearchRegionEvidence[] evidence = RuntimeSearchRegionEvidenceCatalog.All.ToArray();
        Assert.Equal(evidence.Length, evidence.Select(item => item.Owner).Distinct(StringComparer.Ordinal).Count());
        DatasetStore store = new();

        foreach (RuntimeSearchRegionEvidence item in evidence)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Intent));
            string directory = RuntimeEvidenceDatasetCatalog.Dataset(item.Dataset);
            Assert.Equal(EvidenceRoot(), Path.GetDirectoryName(Path.GetFullPath(directory)));
            DatasetLocation dataset = await LoadAsync(store, directory);
            Assert.InRange(item.Frame, 1, dataset.Manifest.Frames.Count);
            BoxAnnotation annotation = Assert.Single(
                dataset.Manifest.Frames[item.Frame - 1].Annotations,
                candidate => string.Equals(candidate.Label, item.AnnotationLabel, StringComparison.Ordinal));
            Assert.Equal(item.Bounds, annotation.Bounds);
            Assert.Equal(item.Bounds, ReadOwner(item.Owner));
        }

        string[] actualOwners = typeof(RuntimeSearchRegionEvidenceCatalog).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => field.FieldType == typeof(PixelRect) && field.Name != "FullClient")
                .Select(field => $"{type.Name}.{field.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedOwners = evidence.Select(item => item.Owner)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedOwners, actualOwners);
    }

    [Fact]
    public async Task BundleInventoryExactlyMatchesSpecificationAndHashes()
    {
        string evidenceRoot = EvidenceRoot();
        string repository = RepositoryRoot();
        string specificationPath = Path.Combine(repository, "eng", "runtime-evidence.json");
        using JsonDocument specification = JsonDocument.Parse(await File.ReadAllTextAsync(
            specificationPath, CancellationToken.None));
        Assert.Equal(1, specification.RootElement.GetProperty("schema_version").GetInt32());
        string[] expected = specification.RootElement.GetProperty("datasets")
            .EnumerateArray()
            .Select(dataset => dataset.GetProperty("name").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Length, expected.Distinct(StringComparer.Ordinal).Count());
        string[] actual = Directory.GetDirectories(evidenceRoot)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        Assert.Equal(expected, actual);

        DatasetStore store = new();
        foreach (string name in expected)
        {
            DatasetLocation dataset = await LoadAsync(store, Path.Combine(evidenceRoot, name));
            Assert.NotEmpty(dataset.Manifest.Frames);
        }
    }

    [Fact]
    public async Task ContextLoaderRejectsMissingExplicitRegionLabel()
    {
        DebugStateSpec state = DebugWorkflowCatalog.Lobby with { RegionLabel = null };
        DebugStateDatasetContextLoader loader = new();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            loader.LoadAsync(state, CancellationToken.None));

        Assert.Contains("no explicit ROI annotation label", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContextLoaderRejectsDuplicateMatchingRegionLabels()
    {
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(), $"LilacMacro-RuntimeEvidence-{Guid.NewGuid():N}");
        string datasetDirectory = Path.Combine(temporaryRoot, "duplicate-region-label");
        try
        {
            CopyDirectory(DebugWorkflowCatalog.Lobby.DatasetDirectory, datasetDirectory);
            DatasetStore store = new();
            DatasetLocation dataset = await store.LoadAsync(datasetDirectory, CancellationToken.None);
            DatasetFrame frame = dataset.Manifest.Frames[DebugWorkflowCatalog.Lobby.RegionFrames[0] - 1];
            BoxAnnotation annotation = Assert.Single(
                frame.Annotations,
                candidate => string.Equals(
                    candidate.Label, DebugWorkflowCatalog.Lobby.RegionLabel, StringComparison.Ordinal));
            frame.Annotations.Add(annotation with { Id = Guid.NewGuid(), GlobalGroupId = null });
            await store.SaveAsync(dataset, CancellationToken.None);

            DebugStateDatasetContextLoader loader = new();
            DebugStateSpec state = DebugWorkflowCatalog.Lobby with
            {
                DatasetDirectory = datasetDirectory,
            };
            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                loader.LoadAsync(state, CancellationToken.None));

            Assert.Contains("requires exactly one ROI annotation", exception.Message, StringComparison.Ordinal);
            Assert.Contains("found 2", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static IEnumerable<DebugStateSpec> StateSpecs() =>
        typeof(DebugWorkflowCatalog)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(DebugStateSpec))
            .Select(field => Assert.IsType<DebugStateSpec>(field.GetValue(null)))
            .Concat(TowerWorkflowCatalog.All())
            .Concat(ExpeditionCheckpointStateCatalog.All())
            .Concat(ExpeditionRewardStateCatalog.All())
            .Concat(DebugCodeWorkflowCatalog.All());

    private static PixelRect ReadOwner(string owner)
    {
        string[] parts = owner.Split('.', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, parts.Length);
        Type type = Assert.Single(
            typeof(RuntimeSearchRegionEvidenceCatalog).Assembly.GetTypes(),
            candidate => string.Equals(candidate.Name, parts[0], StringComparison.Ordinal));
        FieldInfo? field = type.GetField(
            parts[1], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<PixelRect>(field.GetValue(null));
    }

    private static async Task<DatasetLocation> LoadAsync(DatasetStore store, string directory)
    {
        try
        {
            return await store.LoadAsync(directory, CancellationToken.None);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Bundled runtime evidence '{Path.GetFileName(directory)}' is invalid: {exception.Message}",
                exception);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string EvidenceRoot() => Path.GetFullPath(Path.GetDirectoryName(
        RuntimeEvidenceDatasetCatalog.Dataset("lobby-20260802-185951"))!);

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
