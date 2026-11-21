#nullable enable
using System;
using System.IO;
using System.Linq;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.DataTypes.Volume.Tools;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests.Volume;

/// <summary>
/// Opt-in checks against real OpenVDB files: set TIXL_VDB_SAMPLES to a folder holding the OpenVDB sample
/// models (bunny.vdb, smoke.vdb, fire.vdb, ...). Skipped silently when the variable is not set.
/// </summary>
public class RealVdbFileCheck
{
    private readonly ITestOutputHelper _out;

    public RealVdbFileCheck(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Every file in the sample folder, whatever it holds. Two things are asserted: a supported file
    /// must produce at least one grid and flatten into a valid NanoVDB buffer, and a file that ends
    /// up empty must say which grids it skipped - an empty volume has to mean "nothing usable here",
    /// never "the reader gave up quietly".
    /// </summary>
    [Fact]
    public void EverySampleFileEitherLoadsOrExplainsItself()
    {
        var folder = Environment.GetEnvironmentVariable("TIXL_VDB_SAMPLES");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;

        var checkedFiles = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.vdb").OrderBy(x => x))
        {
            var name = Path.GetFileName(path);
            var volume = VdbFile.Load(path);

            if (volume.Grids.Count > 0)
            {
                var floatGrid = volume.GetFloatGrid();
                if (floatGrid != null)
                {
                    var buffer = NanoVdbBuffer.FromGrid(floatGrid);
                    Assert.True(buffer.TryValidate(out var error), $"{name}: {error}");
                    Assert.True(floatGrid.ActiveVoxelCount > 0, $"{name}: float grid has no active voxels");
                }

                _out.WriteLine($"loaded   {name}: {string.Join(", ", volume.Grids.Select(g => g.Name))}");
            }
            else
            {
                Assert.True(volume.SkippedGrids.Count > 0, $"{name}: no grids and nothing reported as skipped");
                _out.WriteLine($"skipped  {name}: {string.Join(", ", volume.SkippedGrids)}");
            }

            checkedFiles++;
        }

        Assert.True(checkedFiles > 0, "the sample folder held no .vdb files");
    }

    [Theory]
    [InlineData("bunny.vdb")]
    [InlineData("smoke.vdb")]
    [InlineData("fire.vdb")]
    [InlineData("explosion.vdb")]
    public void SampleFileDecodesCoherently(string fileName)
    {
        var path = FindSample(fileName);
        if (path == null)
            return;

        var volume = VdbFile.Load(path);
        Assert.NotEmpty(volume.Grids);

        foreach (var grid in volume.Grids)
        {
            if (grid is not VdbGrid<float> floats)
                continue;

            var (neighborDelta, randomDelta, gradient) = Measure(floats);
            _out.WriteLine($"{fileName} '{grid.Name}' {grid.GridClass}: voxels={grid.ActiveVoxelCount:N0} neighbor={neighborDelta:F3} random={randomDelta:F3} grad={gradient:F3}");

            // Voxels that decode in the right order are smooth neighbors; a scrambled order looks like random pairs.
            Assert.True(neighborDelta < randomDelta * 0.6, $"'{grid.Name}' neighbors differ as much as random voxels");
            if (grid.GridClass == VdbGridClass.LevelSet)
                Assert.InRange(gradient, 0.9, 1.1);
        }
    }

    [Theory]
    [InlineData("bunny.vdb")]
    [InlineData("smoke.vdb")]
    public void SampleFileSurvivesNanoVdbConversion(string fileName)
    {
        var path = FindSample(fileName);
        if (path == null)
            return;

        var grid = VdbFile.Load(path).GetFloatGrid();
        Assert.NotNull(grid);

        var reader = new NanoVdbReaderTests.Reader(NanoVdbBuffer.FromGrid(grid).Buffer);
        var compared = 0;
        foreach (var leaf in grid.Tree.Leaves)
        {
            if (compared % 7 == 0)
            {
                for (var i = 0; i < 512; i += 5)
                {
                    var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                    Assert.Equal(grid.Tree.GetValue(coord), reader.Read(coord));
                }
            }

            compared++;
        }

        Assert.True(compared > 0);
    }

    private static string? FindSample(string fileName)
    {
        var folder = Environment.GetEnvironmentVariable("TIXL_VDB_SAMPLES");
        if (string.IsNullOrEmpty(folder))
            return null;

        var path = Path.Combine(folder, fileName);
        return File.Exists(path) ? path : null;
    }

    private static (double NeighborDelta, double RandomDelta, double Gradient) Measure(VdbGrid<float> grid)
    {
        var voxelSize = grid.Transform.VoxelSize;
        double neighbor = 0, random = 0, gradient = 0;
        long neighborCount = 0, randomCount = 0, gradientCount = 0;
        var rng = new Random(1);
        var steps = new[] { new VdbCoord(1, 0, 0), new VdbCoord(0, 1, 0), new VdbCoord(0, 0, 1) };

        foreach (var leaf in grid.Tree.Leaves)
        {
            for (var i = 0; i < 512; i++)
            {
                if (!leaf.ValueMask.IsOn(i))
                    continue;

                var value = leaf.Values[i];
                var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                foreach (var step in steps)
                {
                    var other = coord + step;
                    if (!grid.Tree.IsValueOn(other))
                        continue;

                    neighbor += Math.Abs(value - grid.Tree.GetValue(other));
                    neighborCount++;
                }

                random += Math.Abs(value - leaf.Values[rng.Next(512)]);
                randomCount++;

                if (grid.GridClass == VdbGridClass.LevelSet && Math.Abs(value) < 2 * voxelSize)
                {
                    var gx = (grid.Tree.GetValue(coord + steps[0]) - grid.Tree.GetValue(coord - steps[0])) / (2 * voxelSize);
                    var gy = (grid.Tree.GetValue(coord + steps[1]) - grid.Tree.GetValue(coord - steps[1])) / (2 * voxelSize);
                    var gz = (grid.Tree.GetValue(coord + steps[2]) - grid.Tree.GetValue(coord - steps[2])) / (2 * voxelSize);
                    gradient += Math.Sqrt(gx * gx + gy * gy + gz * gz);
                    gradientCount++;
                }
            }
        }

        return (neighbor / Math.Max(neighborCount, 1) / voxelSize,
                random / Math.Max(randomCount, 1) / voxelSize,
                gradientCount > 0 ? gradient / gradientCount : 0);
    }
}
