using Yata.Views;

namespace Yata.Tests;

/// <summary>Yata's frames: every pose is drawn, and the letter never covers him.</summary>
public class RavenSpriteTests
{
    // Poses the news raven can strike while carrying the letter (perched fidgets and flight).
    private static readonly RavenFrame[] LetterFrames =
    {
        RavenFrame.Perch, RavenFrame.Blink, RavenFrame.Preen, RavenFrame.LookUp, RavenFrame.Stretch,
        RavenFrame.Crouch, RavenFrame.Hop, RavenFrame.FlyUp, RavenFrame.FlyMid, RavenFrame.FlyDown,
    };

    public static TheoryData<RavenFrame> AllFrames()
    {
        var data = new TheoryData<RavenFrame>();
        foreach (var frame in Enum.GetValues<RavenFrame>())
            data.Add(frame);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllFrames))]
    public void Every_frame_is_drawn(RavenFrame frame)
    {
        Assert.True(RavenSprite.HasFrame(frame));
        Assert.Contains(Cells(), c => RavenSprite.IsLit(frame, c.Column, c.Row));
    }

    [Fact]
    public void The_letter_never_overlaps_the_raven()
    {
        foreach (var frame in LetterFrames)
        {
            var clash = Cells().Where(c => RavenSprite.IsLetterLit(c.Column, c.Row) && RavenSprite.IsLit(frame, c.Column, c.Row));
            Assert.True(!clash.Any(), $"{frame} overlaps the letter");
        }
    }

    [Fact]
    public void Gestures_only_use_drawn_frames()
    {
        var gestures = new[]
        {
            RavenAnimator.BlinkSteps, RavenAnimator.PreenSteps, RavenAnimator.StretchSteps, RavenAnimator.HopSteps,
            RavenAnimator.LandSteps, RavenAnimator.CawSteps, RavenAnimator.ShakeSteps,
        };
        foreach (var step in gestures.SelectMany(g => g))
        {
            Assert.True(RavenSprite.HasFrame(step.Frame));
            Assert.InRange(step.Ms, 50, 3000);
        }
    }

    private static IEnumerable<(int Column, int Row)> Cells() =>
        from row in Enumerable.Range(0, RavenSprite.Rows)
        from column in Enumerable.Range(0, RavenSprite.Columns)
        select (column, row);
}
