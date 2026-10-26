namespace Yata.Tests;

/// <summary>The closed notch's clock wakes up when the minute turns, not every second.</summary>
public class ClockTests
{
    [Theory]
    [InlineData(0, 60020)]        // right on the minute: the next one
    [InlineData(59.5, 520)]
    [InlineData(12.25, 47770)]
    public void Wakes_just_after_the_minute_turns(double secondsIntoMinute, double expectedMs)
    {
        var now = new DateTime(2026, 10, 26, 15, 7, 0).AddSeconds(secondsIntoMinute);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), MainWindow.UntilNextMinute(now));
    }
}
