using VoiceCtrl.Core.Overlay;
using Xunit;

namespace VoiceCtrl.Core.Tests.Overlay;

/// <summary>A press on the pill that becomes a drag (research 11, Interaction): the window follows
/// the pointer once it has moved past the threshold, and never changes size.</summary>
public class OverlayDragTests
{
    private static readonly PixelRect Window = new(800, 900, 320, 96);

    [Fact]
    public void SmallMoves_StayAPress_SoAClickStillCounts()
    {
        var drag = new OverlayDrag(1000, 950, Window, dpiScale: 1.0);

        Assert.Null(drag.Move(1003, 952));
        Assert.False(drag.IsDragging);
    }

    [Fact]
    public void PastTheThreshold_TheWindowFollowsThePointer_KeepingItsSize()
    {
        var drag = new OverlayDrag(1000, 950, Window, dpiScale: 1.0);

        PixelRect? moved = drag.Move(1010, 940);

        Assert.True(drag.IsDragging);
        Assert.Equal(new PixelRect(810, 890, 320, 96), moved);
    }

    [Fact]
    public void OnceDragging_EveryMoveCounts_EvenBackInsideTheThreshold()
    {
        var drag = new OverlayDrag(1000, 950, Window, dpiScale: 1.0);
        drag.Move(1020, 950);

        Assert.Equal(new PixelRect(801, 900, 320, 96), drag.Move(1001, 950));
    }

    [Fact]
    public void TheThreshold_ScalesWithTheMonitorsDpi()
    {
        var drag = new OverlayDrag(1000, 950, Window, dpiScale: 2.0);

        Assert.Null(drag.Move(1007, 950));
        Assert.NotNull(drag.Move(1009, 950));
    }

    [Fact]
    public void RemembersWhereTheWindowWas_ForEscToPutItBack()
    {
        var drag = new OverlayDrag(1000, 950, Window, dpiScale: 1.0);
        drag.Move(1300, 500);

        Assert.Equal(Window, drag.StartWindow);
    }
}
