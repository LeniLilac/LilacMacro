using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class AnnotationScopePolicyTests
{
    [Fact]
    public void Promote_CreatesLinkedMemberOnEveryFrameAndPreservesExistingTrials()
    {
        DatasetManifest manifest = Manifest(3);
        BoxAnnotation source = new()
        {
            Bounds = new PixelRect(10, 20, 80, 40),
            Label = "Lobby OCR area",
            Notes = "coarse",
            MinimumPoolMatches = 1,
        };
        BoxAnnotation existing = new() { Bounds = source.Bounds };
        existing.OcrTrials.Add(Trial());
        manifest.Frames[0].Annotations.Add(source);
        manifest.Frames[1].Annotations.Add(existing);

        Guid groupId = AnnotationScopePolicy.Promote(manifest, manifest.Frames[0], source);

        Assert.All(manifest.Frames, frame =>
        {
            BoxAnnotation member = Assert.Single(frame.Annotations, annotation => annotation.GlobalGroupId == groupId);
            Assert.Equal(source.Bounds, member.Bounds);
            Assert.Equal(source.Label, member.Label);
            Assert.Equal(1, member.MinimumPoolMatches);
        });
        Assert.Single(existing.OcrTrials);
    }

    [Fact]
    public void Demote_RemovesPropagatedCopiesAndKeepsSourceLocal()
    {
        DatasetManifest manifest = Manifest(3);
        BoxAnnotation source = new() { Bounds = new PixelRect(10, 20, 80, 40) };
        manifest.Frames[0].Annotations.Add(source);
        AnnotationScopePolicy.Promote(manifest, manifest.Frames[0], source);

        AnnotationScopePolicy.Demote(manifest, source);

        Assert.Same(source, Assert.Single(manifest.Frames[0].Annotations));
        Assert.Null(source.GlobalGroupId);
        Assert.All(manifest.Frames.Skip(1), frame => Assert.Empty(frame.Annotations));
    }

    [Fact]
    public void DeleteAfterDemote_RemovesTheFormerGlobalRegionFromEveryFrame()
    {
        DatasetManifest manifest = Manifest(3);
        BoxAnnotation source = new() { Bounds = new PixelRect(10, 20, 80, 40) };
        manifest.Frames[0].Annotations.Add(source);
        AnnotationScopePolicy.Promote(manifest, manifest.Frames[0], source);

        AnnotationScopePolicy.Demote(manifest, source);
        AnnotationScopePolicy.Delete(manifest, source);

        Assert.All(manifest.Frames, frame => Assert.Empty(frame.Annotations));
    }

    [Fact]
    public void Delete_RemovesEveryGlobalMember()
    {
        DatasetManifest manifest = Manifest(2);
        BoxAnnotation source = new() { Bounds = new PixelRect(10, 20, 80, 40) };
        manifest.Frames[0].Annotations.Add(source);
        AnnotationScopePolicy.Promote(manifest, manifest.Frames[0], source);

        AnnotationScopePolicy.Delete(manifest, source);

        Assert.All(manifest.Frames, frame => Assert.Empty(frame.Annotations));
    }

    [Fact]
    public void AddMembersToNewFrame_InheritsEveryGlobalCoarseRegionWithoutOcrTrials()
    {
        DatasetManifest manifest = Manifest(1);
        BoxAnnotation source = new()
        {
            Bounds = new PixelRect(10, 20, 80, 40),
            Label = "Lobby",
            MinimumPoolMatches = 1,
        };
        source.OcrTrials.Add(Trial());
        manifest.Frames[0].Annotations.Add(source);
        Guid groupId = AnnotationScopePolicy.Promote(manifest, manifest.Frames[0], source);
        DatasetFrame appended = Manifest(1).Frames[0] with { FileName = "frame-0002.png" };

        AnnotationScopePolicy.AddMembersToNewFrame(manifest, appended);

        BoxAnnotation member = Assert.Single(appended.Annotations);
        Assert.Equal(groupId, member.GlobalGroupId);
        Assert.Equal(source.Bounds, member.Bounds);
        Assert.Equal(source.Label, member.Label);
        Assert.Empty(member.OcrTrials);
    }

    private static DatasetManifest Manifest(int frameCount) => new()
    {
        CreatedAtUtc = DateTimeOffset.UtcNow,
        SourceWindowTitle = "Roblox",
        SourceProcessId = 42,
        ClientWidth = 320,
        ClientHeight = 240,
        RequestedFrameCount = frameCount,
        RequestedDurationSeconds = 1,
        Frames = Enumerable.Range(0, frameCount).Select(index => new DatasetFrame
        {
            FileName = $"frame-{index + 1:0000}.png",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Sha256 = new string('0', 64),
            Width = 320,
            Height = 240,
        }).ToList(),
    };

    private static OcrTrial Trial() => new()
    {
        ModelName = "PP-OCRv6_small_rec",
        Text = "Lobby",
        Confidence = 1,
        ModelLoadMilliseconds = 0,
        InferenceMilliseconds = 1,
        RuntimeVersion = "test",
        RanAtUtc = DateTimeOffset.UtcNow,
    };
}
