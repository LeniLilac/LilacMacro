using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Tests;

public sealed class TeamSwapLayoutTests
{
    private static readonly PixelSize ClientSize = new(1366, 700);

    [Fact]
    public void ArbitraryTeamNames_PairsSplitAndWholeSaveLoadButtonsByGeometry()
    {
        TeamSwapLayout? layout = TeamSwapLayout.TryCreate(
            [
                Region("Unit Teams", new PixelRect(235, 92, 129, 28)),
                Region("Raid carry", new PixelRect(270, 171, 67, 17)),
                Region("Save", new PixelRect(985, 186, 44, 24)),
                Region("Team", new PixelRect(1029, 186, 48, 24)),
                Region("Load Team", new PixelRect(987, 262, 90, 22)),
                Region("My favorites", new PixelRect(271, 345, 90, 14)),
                Region("Save Team", new PixelRect(985, 360, 92, 24)),
                Region("Load", new PixelRect(987, 436, 42, 22)),
                Region("Team", new PixelRect(1029, 436, 48, 22)),
                Region("Anything", new PixelRect(269, 516, 70, 21)),
                Region("Save", new PixelRect(985, 534, 44, 24)),
                Region("Team", new PixelRect(1029, 534, 48, 24)),
                Region("Load Team", new PixelRect(987, 610, 90, 22)),
            ],
            ClientSize);

        Assert.NotNull(layout);
        Assert.Equal(174, layout.RowPitch);
        Assert.Equal(3, layout.Rows.Count);
        Assert.Equal(new PixelRect(985, 186, 92, 24), layout.Rows[0].SaveBounds);
        Assert.Equal(new PixelRect(987, 436, 90, 22), layout.Rows[1].LoadBounds);
        Assert.Equal([273, 447, 621], layout.LoadBounds.Select(bounds => bounds.Center.Y));
    }

    [Fact]
    public void TryCreate_RejectsMissingTitleOrTwoCompleteRows()
    {
        Assert.Null(TeamSwapLayout.TryCreate(
            [
                Region("Save Team", new PixelRect(985, 186, 92, 24)),
                Region("Load Team", new PixelRect(987, 262, 90, 22)),
            ],
            ClientSize));
        Assert.Null(TeamSwapLayout.TryCreate(
            [
                Region("Unit Teams", new PixelRect(235, 92, 129, 28)),
                Region("Save Team", new PixelRect(985, 186, 92, 24)),
                Region("Load Team", new PixelRect(987, 262, 90, 22)),
            ],
            ClientSize));
    }

    [Fact]
    public void Calibration_ResolvesEndpointAndMiddleTeamsWithoutNames()
    {
        TeamSwapLayout top = CreateThreeRowLayout(235, 92, 174);
        TeamSwapLayout bottom = CreateThreeRowLayout(235, 92, 174);
        TeamSwapCalibration? calibration = TeamSwapCalibration.TryCreate(
            ClientSize,
            top,
            bottom,
            new PixelRect(1124, 132, 7, 148),
            new PixelRect(1124, 420, 7, 148));

        Assert.NotNull(calibration);
        TeamSwapResolvedTarget teamOne = calibration.Resolve(1, top.TitleBounds)!;
        TeamSwapResolvedTarget teamTwo = calibration.Resolve(2, top.TitleBounds)!;
        TeamSwapResolvedTarget teamThree = calibration.Resolve(3, top.TitleBounds)!;
        TeamSwapResolvedTarget teamFour = calibration.Resolve(4, top.TitleBounds)!;
        TeamSwapResolvedTarget teamFive = calibration.Resolve(5, top.TitleBounds)!;
        TeamSwapResolvedTarget teamEight = calibration.Resolve(8, top.TitleBounds)!;
        Assert.Equal(TeamSwapViewport.Top, teamOne.Viewport);
        Assert.Equal(TeamSwapViewport.Top, teamTwo.Viewport);
        Assert.Equal(TeamSwapViewport.Middle, teamThree.Viewport);
        Assert.Equal(TeamSwapViewport.Middle, teamFour.Viewport);
        Assert.Equal(TeamSwapViewport.Middle, teamFive.Viewport);
        Assert.Equal(TeamSwapViewport.Bottom, teamEight.Viewport);
        Assert.Equal(teamOne.LoadPoint.Y - 87, teamThree.LoadPoint.Y);
        Assert.Equal(teamOne.LoadPoint.Y + 87, teamFour.LoadPoint.Y);
        Assert.Equal(teamOne.LoadPoint.Y + 261, teamFive.LoadPoint.Y);
        Assert.Equal(teamOne.LoadPoint.Y + 348, teamEight.LoadPoint.Y);
        Assert.Equal(new PixelPoint(1127, 206), teamFour.DragStart);
        Assert.Equal(new PixelPoint(1127, 350), teamFour.DragEnd);

        PixelRect shiftedTitle = top.TitleBounds with
        {
            X = top.TitleBounds.X + 4,
            Y = top.TitleBounds.Y - 3,
            Width = top.TitleBounds.Width + 2,
        };
        TeamSwapResolvedTarget shifted = calibration.Resolve(4, shiftedTitle)!;
        Assert.Equal(new PixelPoint(1131, 203), shifted.DragStart);
        Assert.Equal(teamFour.LoadPoint.X + 4, shifted.LoadPoint.X);
    }

    [Fact]
    public void Calibration_RejectsInconsistentThumbOrRowScale()
    {
        TeamSwapLayout top = CreateThreeRowLayout(235, 92, 174);
        TeamSwapLayout differentPitch = CreateThreeRowLayout(235, 92, 120);
        Assert.Null(TeamSwapCalibration.TryCreate(
            ClientSize,
            top,
            differentPitch,
            new PixelRect(1124, 132, 7, 148),
            new PixelRect(1124, 420, 7, 148)));
        Assert.Null(TeamSwapCalibration.TryCreate(
            ClientSize,
            top,
            top,
            new PixelRect(1124, 420, 7, 148),
            new PixelRect(1124, 132, 7, 148)));
    }

    [Fact]
    public void ScrollbarDetector_UsesRepeatedMovingGrayComponent()
    {
        PixelRect search = new(1000, 80, 60, 500);
        RgbImage top = CreateScrollbarImage(search.Width, search.Height, 35);
        RgbImage bottom = CreateScrollbarImage(search.Width, search.Height, 315);

        TeamScrollbarEndpoints? endpoints = TeamScrollbarDetector.TryCalibrate(
            [top, Clone(top)],
            [bottom, Clone(bottom)],
            search);

        Assert.NotNull(endpoints);
        Assert.Equal(new PixelRect(1028, 115, 6, 120), endpoints.TopBounds);
        Assert.Equal(new PixelRect(1028, 395, 6, 120), endpoints.BottomBounds);
    }

    [Fact]
    public void ScrollbarDetector_RejectsStaticOrUnstableGrayComponents()
    {
        PixelRect search = new(1000, 80, 60, 500);
        RgbImage top = CreateScrollbarImage(search.Width, search.Height, 35);
        Assert.Null(TeamScrollbarDetector.TryCalibrate(
            [top, Clone(top)],
            [top, Clone(top)],
            search));
        Assert.Null(TeamScrollbarDetector.TryCalibrate(
            [top, CreateScrollbarImage(search.Width, search.Height, 60)],
            [CreateScrollbarImage(search.Width, search.Height, 315),
                CreateScrollbarImage(search.Width, search.Height, 315)],
            search));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void TeamNumber_RejectsValuesOutsideOneThroughEight(int teamNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TeamSwapLayout.ValidateTeamNumber(teamNumber));
    }

    [Fact]
    public void ConfirmDialog_RequiresCancelBeforeConfirmPointIsAvailable()
    {
        OcrTextRegion confirm = Region("Confirm", new PixelRect(533, 388, 70, 24));
        OcrTextRegion cancel = Region("Cancel", new PixelRect(770, 388, 59, 24));
        TeamLoadConfirmLayout? layout = TeamLoadConfirmLayout.TryCreate([confirm, cancel]);
        Assert.NotNull(layout);
        Assert.Equal(new PixelPoint(568, 400), layout.ConfirmPoint);
        Assert.Null(TeamLoadConfirmLayout.TryCreate([confirm]));
    }

    [Fact]
    public void IncludeDialog_UsesExactButtonAndRequiresExcludeAndCancel()
    {
        OcrTextRegion title = Region("Include Equipment", new PixelRect(607, 261, 152, 22));
        OcrTextRegion include = Region("Include", new PixelRect(497, 406, 65, 24));
        OcrTextRegion exclude = Region("Exclude", new PixelRect(650, 407, 66, 20));
        OcrTextRegion cancel = Region("Cancel", new PixelRect(807, 406, 60, 24));
        TeamIncludeEquipmentLayout? layout = TeamIncludeEquipmentLayout.TryCreate(
            [title, include, exclude, cancel]);
        Assert.NotNull(layout);
        Assert.Equal(include.Bounds, layout.IncludeBounds);
        Assert.Null(TeamIncludeEquipmentLayout.TryCreate([title, exclude, cancel]));
    }

    private static TeamSwapLayout CreateThreeRowLayout(int titleX, int titleY, int pitch)
    {
        PixelRect title = new(titleX, titleY, 129, 28);
        List<OcrTextRegion> regions = [Region("Unit Teams", title)];
        for (int index = 0; index < 3; index++)
        {
            int saveY = 186 + index * pitch;
            regions.Add(Region("Save Team", new PixelRect(985, saveY, 92, 24)));
            regions.Add(Region("Load Team", new PixelRect(987, saveY +
                (int)Math.Round(pitch * 0.43), 90, 22)));
        }
        return TeamSwapLayout.TryCreate(regions, ClientSize)!;
    }

    private static RgbImage CreateScrollbarImage(int width, int height, int thumbY)
    {
        byte[] pixels = Enumerable.Repeat((byte)30, width * height * 3).ToArray();
        Fill(pixels, width, 6, 210, 5, 80, 130);
        Fill(pixels, width, 28, thumbY, 6, 120, 128);
        return new RgbImage(width, height, pixels, takeOwnership: true);
    }

    private static void Fill(
        byte[] pixels,
        int imageWidth,
        int x,
        int y,
        int width,
        int height,
        byte value)
    {
        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                int index = (row * imageWidth + column) * 3;
                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
            }
        }
    }

    private static RgbImage Clone(RgbImage image) => new(
        image.Size.Width,
        image.Size.Height,
        image.Pixels);

    private static OcrTextRegion Region(string text, PixelRect bounds) => new()
    {
        Bounds = bounds,
        Text = text,
        RecognitionConfidence = 0.99,
    };
}
