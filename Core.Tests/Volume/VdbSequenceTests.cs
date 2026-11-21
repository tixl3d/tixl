#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using T3.Core.DataTypes.Volume.IO;
using Xunit;

namespace Core.Tests.Volume;

public class VdbSequenceTests
{
    [Theory]
    [InlineData("smoke_####.nvdb", 7, "smoke_0007.nvdb")]
    [InlineData("smoke_####.nvdb", 1234, "smoke_1234.nvdb")]
    [InlineData("smoke_##.nvdb", 7, "smoke_07.nvdb")]
    [InlineData("smoke_%04d.nvdb", 7, "smoke_0007.nvdb")]
    [InlineData("smoke_%d.nvdb", 42, "smoke_42.nvdb")]
    [InlineData("smoke.vdb", 7, "smoke.vdb")]
    [InlineData("", 7, "")]
    public void ExpandsTheFrameToken(string pattern, int frame, string expected)
    {
        Assert.Equal(expected, VdbSequence.Expand(pattern, frame));
    }

    [Fact]
    public void ReportsWhetherAPatternIsAnimated()
    {
        Assert.True(VdbSequence.IsAnimated("cache_####.nvdb"));
        Assert.True(VdbSequence.IsAnimated("cache_%03d.nvdb"));
        Assert.False(VdbSequence.IsAnimated("bunny.vdb"));
        Assert.False(VdbSequence.IsAnimated(""));
    }

    [Fact]
    public void FrameNumbersWiderThanTheTokenAreNotTruncated()
    {
        // A four-hash token with a five-digit frame would otherwise collide with its neighbours.
        Assert.Equal("cache_12345.nvdb", VdbSequence.Expand("cache_####.nvdb", 12345));
    }

    [Fact]
    public void PathsKeepTheirSeparators()
    {
        var pattern = Path.Combine("caches", "smoke", "frame_####.nvdb");
        var expanded = VdbSequence.Expand(pattern, 12);
        Assert.Equal(Path.Combine("caches", "smoke", "frame_0012.nvdb"), expanded);
    }

    [Fact]
    public void CacheReturnsWhatWasStored()
    {
        var cache = new VdbFrameCache(1024);
        cache.Put("a", new byte[100]);

        Assert.True(cache.TryTake("a", out var bytes));
        Assert.Equal(100, bytes!.Length);
        Assert.False(cache.TryTake("missing", out _));
    }

    [Fact]
    public void CacheEvictsTheLeastRecentlyUsedFrame()
    {
        var cache = new VdbFrameCache(300);
        cache.Put("a", new byte[100]);
        cache.Put("b", new byte[100]);
        cache.Put("c", new byte[100]);

        // Touching "a" makes "b" the oldest, so adding "d" must drop "b".
        Assert.True(cache.TryTake("a", out _));
        cache.Put("d", new byte[100]);

        Assert.True(cache.TryTake("a", out _));
        Assert.False(cache.TryTake("b", out _));
        Assert.True(cache.TryTake("c", out _));
        Assert.True(cache.TryTake("d", out _));
        Assert.Equal(300, cache.UsedBytes);
    }

    [Fact]
    public void FrameLargerThanTheBudgetIsNotRetained()
    {
        var cache = new VdbFrameCache(100);
        cache.Put("huge", new byte[101]);

        Assert.False(cache.TryTake("huge", out _));
        Assert.Equal(0, cache.UsedBytes);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ReplacingAFrameDoesNotDoubleCount()
    {
        var cache = new VdbFrameCache(1024);
        cache.Put("a", new byte[100]);
        cache.Put("a", new byte[200]);

        Assert.Equal(200, cache.UsedBytes);
        Assert.True(cache.TryTake("a", out var bytes));
        Assert.Equal(200, bytes!.Length);
    }

    [Fact]
    public void ConcurrentWritersAndReadersStayWithinBudget()
    {
        // The preloader fills from a worker while the evaluation thread takes; the budget must
        // hold no matter how the two interleave.
        var cache = new VdbFrameCache(4096);

        Parallel.For(0, 400, i =>
                            {
                                if (i % 3 == 0)
                                    cache.TryTake($"f{i % 40}", out _);
                                else
                                    cache.Put($"f{i % 40}", new byte[512]);
                            });

        Assert.True(cache.UsedBytes <= cache.BudgetBytes, $"used {cache.UsedBytes} of {cache.BudgetBytes}");
        Assert.True(cache.Count <= 4096 / 512);
    }

    [Fact]
    public void ClearReleasesEverything()
    {
        var cache = new VdbFrameCache(1024);
        cache.Put("a", new byte[100]);
        cache.Clear();

        Assert.Equal(0, cache.UsedBytes);
        Assert.False(cache.TryTake("a", out _));
    }
}
