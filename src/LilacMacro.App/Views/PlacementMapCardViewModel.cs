using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public sealed class PlacementMapCardViewModel
{
    public PlacementMapCardViewModel(PlacementMapReference reference)
    {
        Reference = reference;
        Images = reference.ImagePaths
            .Select((path, index) => new PlacementReferenceImageViewModel($"VIEW {index + 1}", path))
            .ToArray();
    }

    public PlacementMapReference Reference { get; }

    public string Id => Reference.Definition.Id;

    public PlacementMapMode Mode => Reference.Definition.Mode;

    public string ModeLabel => Mode.ToString().ToUpperInvariant();

    public string DisplayName => Reference.Definition.DisplayName;

    public string CopyLabel => $"{ModeLabel} / {DisplayName}";

    public string ViewCount => $"{Images.Count} {(Images.Count == 1 ? "VIEW" : "VIEWS")}";

    public Uri ThumbnailUri => new(Path.GetFullPath(Images[0].Path), UriKind.Absolute);

    public IReadOnlyList<PlacementReferenceImageViewModel> Images { get; }

    public int ImageWidth => Reference.ImageWidth;

    public int ImageHeight => Reference.ImageHeight;
}

public sealed record PlacementReferenceImageViewModel(string Label, string Path);
