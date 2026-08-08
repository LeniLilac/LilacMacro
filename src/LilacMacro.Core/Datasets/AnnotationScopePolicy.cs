namespace LilacMacro.Core.Datasets;

public static class AnnotationScopePolicy
{
    public static Guid Promote(DatasetManifest manifest, DatasetFrame sourceFrame, BoxAnnotation source)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(sourceFrame);
        ArgumentNullException.ThrowIfNull(source);
        if (!sourceFrame.Annotations.Contains(source))
        {
            throw new ArgumentException("The source annotation does not belong to the source frame.", nameof(source));
        }

        Guid groupId = source.GlobalGroupId ?? Guid.NewGuid();
        source.GlobalGroupId = groupId;
        foreach (DatasetFrame frame in manifest.Frames.Where(frame => !ReferenceEquals(frame, sourceFrame)))
        {
            BoxAnnotation[] exactLocalMatches = frame.Annotations
                .Where(annotation => annotation.GlobalGroupId is null && annotation.Bounds == source.Bounds)
                .ToArray();
            BoxAnnotation member = exactLocalMatches.Length == 1
                ? exactLocalMatches[0]
                : new BoxAnnotation { Bounds = source.Bounds };
            if (!frame.Annotations.Contains(member)) frame.Annotations.Add(member);
            ApplySharedFields(source, member, groupId);
        }
        return groupId;
    }

    public static void Demote(DatasetManifest manifest, BoxAnnotation source)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(source);
        if (source.GlobalGroupId is not { } groupId) return;
        if (!manifest.Frames.Any(frame => frame.Annotations.Contains(source)))
        {
            throw new ArgumentException("The source annotation does not belong to the dataset.", nameof(source));
        }

        foreach (DatasetFrame frame in manifest.Frames)
        {
            frame.Annotations.RemoveAll(annotation =>
                annotation.GlobalGroupId == groupId && !ReferenceEquals(annotation, source));
        }
        source.GlobalGroupId = null;
    }

    public static void Synchronize(DatasetManifest manifest, BoxAnnotation source)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(source);
        if (source.GlobalGroupId is not { } groupId) return;
        foreach (BoxAnnotation member in Members(manifest, groupId))
        {
            if (!ReferenceEquals(member, source)) ApplySharedFields(source, member, groupId);
        }
    }

    public static void Delete(DatasetManifest manifest, BoxAnnotation source)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(source);
        if (source.GlobalGroupId is not { } groupId)
        {
            foreach (DatasetFrame frame in manifest.Frames) frame.Annotations.Remove(source);
            return;
        }

        foreach (DatasetFrame frame in manifest.Frames)
        {
            frame.Annotations.RemoveAll(annotation => annotation.GlobalGroupId == groupId);
        }
    }

    public static BoxAnnotation? FindMember(DatasetFrame frame, Guid groupId) =>
        frame.Annotations.FirstOrDefault(annotation => annotation.GlobalGroupId == groupId);

    public static BoxAnnotation[] Members(DatasetManifest manifest, Guid groupId) => manifest.Frames
        .SelectMany(frame => frame.Annotations)
        .Where(annotation => annotation.GlobalGroupId == groupId)
        .ToArray();

    public static void AddMembersToNewFrame(DatasetManifest manifest, DatasetFrame newFrame)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(newFrame);
        foreach (IGrouping<Guid, BoxAnnotation> group in manifest.Frames
                     .SelectMany(frame => frame.Annotations)
                     .Where(annotation => annotation.GlobalGroupId.HasValue)
                     .GroupBy(annotation => annotation.GlobalGroupId!.Value))
        {
            BoxAnnotation source = group.First();
            BoxAnnotation member = new() { Bounds = source.Bounds };
            ApplySharedFields(source, member, group.Key);
            newFrame.Annotations.Add(member);
        }
    }

    private static void ApplySharedFields(BoxAnnotation source, BoxAnnotation target, Guid groupId)
    {
        target.GlobalGroupId = groupId;
        target.Label = source.Label;
        target.Notes = source.Notes;
        target.MinimumPoolMatches = source.MinimumPoolMatches;
    }
}
