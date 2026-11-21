using System.Linq;
using T3.Core.DataTypes;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Tests for the laser optimizer transform (Core/DataTypes/LaserOptimizerTransforms.cs) — the
/// algorithm lifted 1:1 from the [LaserOptimizer] operator and shared with the output-setup
/// pipeline. These pin the behavior the pipeline relies on: segment reordering with reversal,
/// distance-scaled blanked jumps, corner dwelling, and the pass-through mode.
/// </summary>
public class LaserOptimizerTransformTests
{
    private static LaserPoint Lit(int x, int y) => new(x, y, 65535, 65535, 65535);

    private static LaserPoint Blank(int x, int y) => LaserPoint.CreateBlanked(x, y);

    private static LaserOptimizerTransforms.Scratch _scratch = new();

    private static LaserOptimizerTransforms.Config Config(bool optimize = true, float maxJump = 5000f,
                                                          int blanking = 5, float cornerAngle = 30f, int anchors = 3)
        => new()
               {
                   EnableOptimization = optimize,
                   MaxJumpDistance = maxJump,
                   BlankingDelayPoints = blanking,
                   CornerAngleThreshold = cornerAngle,
                   CornerAnchorCount = anchors,
               };

    [Fact]
    public void Apply_EmptyInput_ReturnsEmpty()
    {
        var result = LaserOptimizerTransforms.Apply([], 0, Config(), _scratch);
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_ReordersSegmentsIntoSeamOptimalTour()
    {
        // Three dots on a line with UNEQUAL gaps (3000 and 6000): the greedy tour from null
        // visits them near-to-far, but the frame is looped by the DAC continuously, so the
        // tour gets rotated to make the closing seam the shortest link. Starting at the
        // middle dot gives the minimal seam (3000) - that is the correct start now, not the
        // dot nearest the origin.
        var input = new[]
                        {
                            Blank(0, 0),
                            Lit(9000, 0), Blank(9000, 0),      // far
                            Lit(3000, 0), Blank(3000, 0),       // near
                            Lit(6000, 0), Blank(6000, 0),       // middle
                        };

        var result = LaserOptimizerTransforms.Apply(input, input.Length, Config(), _scratch);

        var litPoints = result.Where(p => p.I > 0).ToArray();
        Assert.Equal(3, litPoints.Length);

        // Minimal seam: start and end of the loop are the two closest dots (3000 gap).
        var seamDx = litPoints[^1].X - litPoints[0].X;
        var seamDy = litPoints[^1].Y - litPoints[0].Y;
        Assert.True(MathF.Sqrt(seamDx * (float)seamDx + seamDy * (float)seamDy) <= 3000,
                    $"loop seam must be the shortest link, was {MathF.Sqrt(seamDx * (float)seamDx + seamDy * (float)seamDy)}");
    }

    [Fact]
    public void Apply_ReversesSegmentWhenCloserFromTheOtherEnd()
    {
        // A line segment whose END is nearer the origin than its start: the optimizer emits it
        // reversed (end first) to shorten the jump.
        var input = new[]
                        {
                            Lit(10000, 0), Lit(9000, 0), Lit(8000, 0), Blank(0, 0),
                        };

        var result = LaserOptimizerTransforms.Apply(input, input.Length, Config(), _scratch);
        var litIndex = Array.FindIndex(result, p => p.I > 0);
        Assert.True(litIndex >= 0, "expected a lit point");
        Assert.Equal(8000, result[litIndex].X);
    }

    [Fact]
    public void Apply_InsertsBlankedJumpsBetweenShapes()
    {
        // Two single-point shapes (separated by a blank so they are two segments): the move
        // between them must be blanked, never lit - and it must be a RAMP from the previous
        // shape toward the target, ending settled at the target (a single-sample step is a
        // maximal-acceleration demand on the galvos).
        var input = new[]
                        {
                            Lit(0, 0),
                            Blank(0, 0),
                            Lit(20000, 0),
                        };

        var result = LaserOptimizerTransforms.Apply(input, input.Length, Config(maxJump: 5000, blanking: 4), _scratch);

        // Between the two lit points there must be blanked points marching toward the target.
        var litIndices = new List<int>();
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i].I > 0)
                litIndices.Add(i);
        }

        Assert.Equal(2, litIndices.Count);
        var between = result[(litIndices[0] + 1)..litIndices[1]];
        Assert.NotEmpty(between);
        Assert.All(between, p => Assert.Equal(0, p.I));

        // All travel samples stay on the path (between the two shapes, never past the target)...
        Assert.All(between, p => Assert.InRange(p.X, 0, 20000));

        // ...advance monotonically toward the target...
        for (var i = 1; i < between.Length; i++)
            Assert.True(between[i].X >= between[i - 1].X, "travel samples must advance toward the target");

        // ...and the last samples settle at the target so the scanners stop before the beam opens.
        Assert.Equal(20000, between[^1].X);
    }

    [Fact]
    public void Apply_NoReordering_KeepsOriginalOrder()
    {
        var input = new[]
                        {
                            Blank(0, 0),
                            Lit(9000, 0), Blank(9000, 0),
                            Lit(3000, 0), Blank(3000, 0),
                        };

        var result = LaserOptimizerTransforms.Apply(input, input.Length, Config(optimize: false), _scratch);
        var litIndex = Array.FindIndex(result, p => p.I > 0);
        Assert.True(litIndex >= 0, "expected a lit point");
        Assert.Equal(9000, result[litIndex].X);
    }

    [Fact]
    public void Apply_SharpCornerGetsDwellAnchors()
    {
        // A V shape (sharp corner at the middle point) with an angle threshold high enough to
        // trigger dwelling: the corner point repeats.
        var input = new[]
                        {
                            Lit(0, 0), Lit(1000, 1000), Lit(2000, 0),
                        };

        var anchors = 3;
        var result = LaserOptimizerTransforms.Apply(input, input.Length,
                                                    Config(optimize: false, cornerAngle: 170f, anchors: anchors), _scratch);
        var cornerCount = 0;
        foreach (var p in result)
        {
            if (p.I > 0 && p.X == 1000)
                cornerCount++;
        }

        // The corner point appears once plus (anchors - 1) repeats.
        Assert.Equal(anchors, cornerCount);
    }

    [Fact]
    public void Apply_BlankedOnlyInput_CollapsesToSinglePoint()
    {
        // No lit runs: FindSegments yields nothing and the tour emits the first point only.
        var input = new[] { Blank(100, 100), Blank(200, 200), Blank(300, 300) };
        var result = LaserOptimizerTransforms.Apply(input, input.Length, Config(), _scratch);
        Assert.Single(result);
        Assert.Equal(0, result[0].I);
    }

    [Fact]
    public void Apply_ReversedSegment_BlanksToItsEntryEnd()
    {
        // Three two-point segments. The middle one is drawn right-to-left, so the tour visits it
        // reversed (from its END); the seam rotation emits it last. The blanked travel before a
        // segment must land where its first LIT sample is - approaching the other end and then
        // jumping across the whole segment with the beam on is a maximal-acceleration lit stroke
        // (the exact hazard the blanked ramps exist to prevent).
        var input = new[]
                        {
                            Lit(0, 0), Lit(100, 0), Blank(100, 0),
                            Lit(20000, 0), Lit(200, 0), Blank(200, 0),
                            Lit(20050, 0), Lit(20150, 0),
                        };

        var result = LaserOptimizerTransforms.Apply(input, input.Length, Config(maxJump: 5000, blanking: 5), _scratch);

        for (var i = 1; i < result.Length; i++)
        {
            if (result[i].I <= 0 || result[i - 1].I > 0)
                continue;

            var dx = (float)(result[i].X - result[i - 1].X);
            var dy = (float)(result[i].Y - result[i - 1].Y);
            var step = MathF.Sqrt(dx * dx + dy * dy);
            Assert.True(step <= 5000f,
                        $"blanked travel ended at ({result[i - 1].X},{result[i - 1].Y}) but the beam opens at ({result[i].X},{result[i].Y}) - {step} units lit");
        }
    }

    [Fact]
    public void EstimateResultSize_HugeCountStaysPositiveAndCapped()
    {
        // 20M points at the widest blanking/anchor setting overflows the int product
        // (20M * 110 > int.MaxValue): the estimate went negative, the scratch buffer was never
        // grown and a huge frame optimized to nothing instead of to the cap.
        Assert.Equal(LaserOptimizerTransforms.MaxOutputPoints,
                     LaserOptimizerTransforms.EstimateResultSize(20_000_000, 100, 10));

        // Small frames keep the exact worst case.
        Assert.Equal(10 * (1 + 5 + 2) + 1, LaserOptimizerTransforms.EstimateResultSize(10, 5, 3));
    }

    [Fact]
    public void Apply_BoundedOutput_NeverExceedsCap()
    {
        // Many scattered lit points with generous blanking: the result must stay within the
        // hard cap rather than overrunning the scratch buffer.
        var input = new LaserPoint[2000];
        for (var i = 0; i < input.Length; i++)
            input[i] = Lit((i * 977) % 65536 - 32768, (i * 1231) % 65536 - 32768);

        var result = LaserOptimizerTransforms.Apply(input, input.Length, Config(maxJump: 500, blanking: 100, anchors: 10), _scratch);
        Assert.True(result.Length <= LaserOptimizerTransforms.MaxOutputPoints);
    }
}
