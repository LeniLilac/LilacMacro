using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Tests;

public sealed class ExpeditionRewardPopupPolicyTests
{
    [Fact]
    public void Popup_requires_three_select_upgrade_matches()
    {
        Assert.True(ExpeditionRewardPopupPolicy.IsPopup(
            [Button(260), Button(620), Button(980)]));
        Assert.False(ExpeditionRewardPopupPolicy.IsPopup(
            [Button(260), Button(620)]));
    }

    [Fact]
    public void Popup_requires_the_matches_to_share_one_action_row()
    {
        Assert.False(ExpeditionRewardPopupPolicy.IsPopup(
            [Button(260, y: 480), Button(620, y: 480), Button(980, y: 540)]));
    }

    [Fact]
    public void Selects_the_runtime_detected_rightmost_match()
    {
        OcrTextRegion rightmost = Button(980);

        OcrTextRegion? selected = ExpeditionRewardPopupPolicy.SelectRightmost(
            [Button(260), Button(620), rightmost]);

        Assert.Same(rightmost, selected);
    }

    [Fact]
    public void Tied_rightmost_matches_fail_closed()
    {
        Assert.Null(ExpeditionRewardPopupPolicy.SelectRightmost(
            [Button(260), Button(980), Button(980)]));
    }

    private static OcrTextRegion Button(int x, int y = 490) => new()
    {
        Bounds = new PixelRect(x, y, 80, 18),
        Text = "Select Upgrade",
        RecognitionConfidence = 0.99,
    };
}
