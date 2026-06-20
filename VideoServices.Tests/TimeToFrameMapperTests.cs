using Xunit;

namespace T3.VideoServices.Tests;

public class TimeToFrameMapperTests
{
    [Theory]
    [InlineData(7.0, 5.0, 2.0)]   // wraps past the end
    [InlineData(-1.0, 5.0, 4.0)]  // negative wraps forward
    [InlineData(0.0, 5.0, 0.0)]
    [InlineData(5.0, 5.0, 0.0)]   // exactly one period wraps to 0
    public void ResolvePlaybackSeconds_Loop_Wraps(double requested, double duration, double expected)
    {
        Assert.Equal(expected, TimeToFrameMapper.ResolvePlaybackSeconds(requested, duration, loop: true), 6);
    }

    [Theory]
    [InlineData(7.0, 5.0, 5.0)]   // clamps to last
    [InlineData(-1.0, 5.0, 0.0)]  // clamps to first
    [InlineData(3.0, 5.0, 3.0)]
    public void ResolvePlaybackSeconds_NoLoop_Clamps(double requested, double duration, double expected)
    {
        Assert.Equal(expected, TimeToFrameMapper.ResolvePlaybackSeconds(requested, duration, loop: false), 6);
    }

    [Fact]
    public void ResolvePlaybackSeconds_ZeroDuration_IsSafe()
    {
        Assert.Equal(0.0, TimeToFrameMapper.ResolvePlaybackSeconds(3.0, 0.0, loop: true));
        Assert.Equal(0.0, TimeToFrameMapper.ResolvePlaybackSeconds(3.0, 0.0, loop: false));
    }

    [Theory]
    [InlineData(2.0, 0, 1, 60000, 120000)]
    [InlineData(2.0, 0, 1, 30000, 60000)]
    [InlineData(0.0, 0, 1, 60000, 0)]
    [InlineData(1.0, 1000, 1, 60000, 61000)] // start-time offset is added
    public void SecondsToPts_FloorsToGrid(double seconds, long start, int num, int den, long expected)
    {
        Assert.Equal(expected, TimeToFrameMapper.SecondsToPts(seconds, start, num, den));
    }

    [Fact]
    public void SecondsToPts_IsDeterministicAndMonotonic()
    {
        long Prev = long.MinValue;
        for (var s = 0.0; s < 5.0; s += 0.013)
        {
            var a = TimeToFrameMapper.SecondsToPts(s, 0, 1, 60000);
            var b = TimeToFrameMapper.SecondsToPts(s, 0, 1, 60000);
            Assert.Equal(a, b);              // same input → same output
            Assert.True(a >= Prev);          // non-decreasing in time
            Prev = a;
        }
    }

    [Fact]
    public void PtsToSeconds_RoundTripsWithinOneFrame()
    {
        const int num = 1, den = 60000;
        for (var s = 0.1; s < 5.0; s += 0.37)
        {
            var pts = TimeToFrameMapper.SecondsToPts(s, 0, num, den);
            var back = TimeToFrameMapper.PtsToSeconds(pts, 0, num, den);
            Assert.True(Math.Abs(back - s) <= num / (double)den, $"{back} vs {s}");
        }
    }
}
