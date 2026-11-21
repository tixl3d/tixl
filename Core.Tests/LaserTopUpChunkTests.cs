using System;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>A real EtherDream empties a 180-point frame in 6 ms; the sender must hand it whole repeats.</summary>
public class LaserTopUpChunkTests
{
    private static LaserPoint[] Square(int count)
    {
        var f = new LaserPoint[count];
        for (var i = 0; i < count; i++)
            f[i] = LaserPoint.CreateBlanked(i * 10 - 900, (i % 7) * 100);
        return f;
    }

    [Fact]
    public void SmallFrameIsRepeatedWholeToFillTheFreeRoom()
    {
        var raw = Square(180);
        var scratch = Array.Empty<LaserPoint>();
        var sendFrame = LaserOutputStream.PrependDarkApproach(raw, 0, 0, ref scratch).AsSpan().ToArray();

        var chunk = LaserOutputStream.BuildTopUpChunk(sendFrame, 0, raw, 1700, ref scratch, out var consumed);

        Assert.Equal(sendFrame.Length, consumed);              // the frame itself is fully consumed once
        Assert.True(chunk.Length > 1500 && chunk.Length <= 1700, $"chunk {chunk.Length}");
        Assert.Equal(raw[^1].X, chunk[^1].X);                   // ends on a frame boundary, never mid-frame
        Assert.Equal(raw[^1].Y, chunk[^1].Y);
        Assert.Equal(0, (chunk.Length - sendFrame.Length) % (raw.Length + 6));
    }

    [Fact]
    public void LargeFrameIsStillSlicedByRoomWithoutRepeats()
    {
        var raw = Square(5000);
        var scratch = Array.Empty<LaserPoint>();
        var sendFrame = LaserOutputStream.PrependDarkApproach(raw, 0, 0, ref scratch).AsSpan().ToArray();

        var chunk = LaserOutputStream.BuildTopUpChunk(sendFrame, 100, raw, 1700, ref scratch, out var consumed);

        Assert.Equal(1700, consumed);
        Assert.Equal(1700, chunk.Length);
        Assert.Equal(sendFrame[100].X, chunk[0].X);
    }
}
