using System.Diagnostics;
using System.Numerics;
using BepuUtilities.Memory;
using Lib.physics;
using T3.Core.DataTypes;
using Xunit;
using Xunit.Abstractions;

namespace Lib.Tests.Physics;

/// <summary>
/// Convex decomposition, compound bodies and density-based mass: the parts of the physics package that let a concave
/// or multi-part geometry collide and weigh like the object it is.
/// </summary>
public class PhysicsDecompositionTests(ITestOutputHelper output)
{
    private const float FrameSec = 1f / 60f;

    [Fact]
    public void HullVolumeOfUnitCubeIsOne()
    {
        var pool = new BufferPool();
        Assert.Equal(1f, PhysicsDecomposition.HullVolume(Box(Vector3.Zero, Vector3.One).Positions, pool), 0.001f);
        pool.Clear();
    }

    [Fact]
    public void ConvexShapesStayOneHull()
    {
        var box = PhysicsGeometry.BuildHulls(Box(Vector3.Zero, new Vector3(2, 1, 0.5f)), merge: false, PhysicsGeometry.HullSplit.Concave, 8);
        Assert.Null(box[0].ConvexParts);
        Assert.Equal(1f, box[0].Volume, 0.01f);

        var sphere = PhysicsGeometry.BuildHulls(Sphere(1f), merge: false, PhysicsGeometry.HullSplit.Concave, 8);
        Assert.Null(sphere[0].ConvexParts);
        Assert.InRange(sphere[0].Volume, 3.9f, 4.19f);
    }

    [Fact]
    public void TorusIsSplitIntoHullsThatKeepTheHoleFree()
    {
        var torus = Torus(1f, 0.3f);
        var stopwatch = Stopwatch.StartNew();
        var piece = PhysicsGeometry.BuildHulls(torus, merge: false, PhysicsGeometry.HullSplit.Concave, 8)[0];
        output.WriteLine($"torus decomposition: {stopwatch.ElapsedMilliseconds} ms, {piece.ConvexParts?.Length} hulls, volume {piece.Volume}");

        Assert.NotNull(piece.ConvexParts);
        Assert.InRange(piece.ConvexParts.Length, 4, 8);

        // The real torus is 2 pi^2 R r^2 = 1.78; one hull around it would be about 3.2.
        Assert.InRange(piece.Volume, 1.6f, 2.6f);

        foreach (var part in piece.ConvexParts)
        {
            // A hull that reached across the hole would hold points on opposite sides of the axis.
            var direction = Vector3.Zero;
            foreach (var point in part)
                direction += Vector3.Normalize(new Vector3(point.X, 0, point.Z));

            direction = Vector3.Normalize(direction);
            foreach (var point in part)
                Assert.True(Vector3.Dot(Vector3.Normalize(new Vector3(point.X, 0, point.Z)), direction) > -0.05f);
        }
    }

    [Fact]
    public void OverlappingShellsAreOneSolid()
    {
        // An L from two closed boxes that overlap in a corner, as models assembled from several meshes often are.
        var shape = Merge(Box(new Vector3(0, 0, 0), new Vector3(3, 1, 1)), Box(new Vector3(-1, 1.5f, 0), new Vector3(1, 4, 1)));
        shape.Parts = [];
        var piece = PhysicsGeometry.BuildHulls(shape, merge: false, PhysicsGeometry.HullSplit.Concave, 8)[0];

        Assert.NotNull(piece.ConvexParts);
        Assert.InRange(piece.ConvexParts.Length, 2, 4);

        // The union is 3 + 4 - 1 = 6; a single hull would be about 9.
        Assert.InRange(piece.Volume, 5.6f, 7.2f);
    }

    [Fact]
    public void WholeObjectKeepsEveryPartAsAHullOfOneBody()
    {
        var geometry = Merge(Box(new Vector3(-2, 0, 0), Vector3.One), Box(new Vector3(2, 0, 0), Vector3.One));
        Assert.Equal(2, geometry.Parts.Length);

        var whole = PhysicsGeometry.BuildHulls(geometry, merge: true);
        Assert.Single(whole);
        Assert.Equal(2, whole[0].ConvexParts.Length);
        Assert.Equal(2f, whole[0].Volume, 0.01f);
    }

    [Fact]
    public void ConcaveTorusFallsOverAPostThatAHullWouldRestOn()
    {
        var torus = Torus(1f, 0.25f);
        var hollow = DropTorusOntoPost(PhysicsGeometry.BuildHulls(torus, merge: false, PhysicsGeometry.HullSplit.Concave, 12));
        var filled = DropTorusOntoPost(PhysicsGeometry.BuildHulls(torus, merge: false));

        output.WriteLine($"decomposed torus rests at {hollow}, single hull at {filled}");
        Assert.True(hollow < 0.6f, "the decomposed torus should slide down the post onto the floor");
        Assert.True(filled > 2.5f, "a single hull fills the hole and rests on top of the post");
    }

    [Fact]
    public void CompoundShapesAreReleasedWithTheirBody()
    {
        using var harness = new Harness();
        var body = harness.Add(new PhysicsBody
                                   {
                                       Id = Guid.NewGuid(),
                                       Shape = PhysicsShapeType.ConvexDecomposition,
                                       Hulls = PhysicsGeometry.BuildHulls(Torus(1f, 0.3f), merge: false, PhysicsGeometry.HullSplit.Always, 6),
                                   });
        harness.Step(FrameSec);
        Assert.Equal(1, harness.Sim.SharedShapeCount);

        body.Enabled = false;
        harness.Step(FrameSec);
        Assert.Equal(0, harness.Sim.SharedShapeCount);
        Assert.Equal(0, harness.Sim.RenderCount(true));
    }

    [Fact]
    public void DensityMakesBiggerBodiesHeavier()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var small = harness.Add(new PhysicsBody { Id = Guid.NewGuid(), Size = Vector3.One, MassFromDensity = true, Density = 1f });
        var large = harness.Add(new PhysicsBody
                                    {
                                        Id = Guid.NewGuid(), Position = new Vector3(0, 10, 0), Size = new Vector3(2), MassFromDensity = true, Density = 1f,
                                    });
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Direction = Vector3.UnitX, Strength = 8f, ScaleByMass = true });
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(small.Id, out var smallPosition);
        harness.Sim.TryGetBodyPosition(large.Id, out var largePosition);
        Assert.Equal(8f, smallPosition.X / largePosition.X, 0.3f);
    }

    [Fact]
    public void DensityOfAHullUsesItsVolume()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var hulls = PhysicsGeometry.BuildHulls(Box(Vector3.Zero, new Vector3(2)), merge: false);
        var fixedMass = harness.Add(new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.ConvexHull, Hulls = hulls, Mass = 1f });
        var dense = harness.Add(new PhysicsBody
                                    {
                                        Id = Guid.NewGuid(), Shape = PhysicsShapeType.ConvexHull, Hulls = hulls, Position = new Vector3(0, 10, 0),
                                        MassFromDensity = true, Density = 0.5f,
                                    });
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Direction = Vector3.UnitX, Strength = 4f, ScaleByMass = true });
        harness.Step(1f);

        // Volume 8 at density 0.5 weighs 4.
        harness.Sim.TryGetBodyPosition(fixedMass.Id, out var fixedPosition);
        harness.Sim.TryGetBodyPosition(dense.Id, out var densePosition);
        Assert.Equal(4f, fixedPosition.X / densePosition.X, 0.2f);
    }

    [Fact]
    public void AutoShapeWithoutGeometryIsABox()
    {
        using var harness = new Harness();
        harness.Add(new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Box, Size = new Vector3(40, 1, 40), Position = new Vector3(0, -0.5f, 0), Kind = PhysicsBodyKind.Static });
        var box = harness.Add(new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Auto, Position = new Vector3(0, 3, 0) });
        harness.Step(3f);

        harness.Sim.TryGetBodyPosition(box.Id, out var position);
        Assert.InRange(position.Y, 0.45f, 0.55f);
    }

    [Fact]
    public void ColliderGeometryShowsEveryHullWithOutwardFaces()
    {
        var piece = PhysicsGeometry.BuildHulls(Torus(1f, 0.3f), merge: false, PhysicsGeometry.HullSplit.Concave, 8)[0];
        var collider = PhysicsGeometry.BuildColliderGeometry([piece]);

        Assert.Single(collider.Parts);
        Assert.True(collider.FaceCount > piece.ConvexParts.Length * 4);
        Assert.True(collider.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Face, out var colors));
        Assert.Equal(collider.FaceCount, colors.Values.Length);

        // With every face wound outward, the divergence theorem gives back the hulls' volume.
        var volume = 0f;
        for (var face = 0; face < collider.FaceCount; face++)
        {
            var start = collider.FaceCornerOffsets[face];
            var end = collider.FaceCornerOffsets[face + 1];
            var a = collider.Positions[collider.CornerPointIndices[start]];
            for (var corner = start + 1; corner + 1 < end; corner++)
                volume += Vector3.Dot(a, Vector3.Cross(collider.Positions[collider.CornerPointIndices[corner]], collider.Positions[collider.CornerPointIndices[corner + 1]])) / 6f;
        }

        Assert.Equal(piece.Volume, volume, 0.02f);
    }

    [Fact]
    public void PosedColliderViewReplacesTheSourceGeometry()
    {
        var torus = Torus(1f, 0.3f);
        var body = new PhysicsBody
                       {
                           Shape = PhysicsShapeType.ConvexHull,
                           Hulls = PhysicsGeometry.BuildHulls(torus, merge: true, PhysicsGeometry.HullSplit.Concave, 8),
                           SourceGeometry = torus,
                           PublishedPoints = [new Point { Position = new Vector3(0, 2, 0), Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One }],
                           PublishedCount = 1,
                       };
        var output = new MeshGeometry();
        var scratch = new List<PhysicsGeometry.PosedBody>();

        Assert.Equal(1, PhysicsGeometry.BuildPosed([body], output, scratch));
        Assert.Equal(torus.FaceCount, output.FaceCount);

        Assert.Equal(1, PhysicsGeometry.BuildPosed([body], output, scratch, showColliders: true));
        Assert.NotEqual(torus.FaceCount, output.FaceCount);
        Assert.True(output.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Face, out _));
        foreach (var position in output.Positions)
            Assert.InRange(position.Y, 1.6f, 2.4f);
    }

    /// <summary>Drops a lying torus centred over a thin post and returns the height its centre comes to rest at.</summary>
    private static float DropTorusOntoPost(PhysicsHull[] hulls)
    {
        using var harness = new Harness();
        harness.Add(new PhysicsBody { Id = Guid.NewGuid(), Size = new Vector3(40, 1, 40), Position = new Vector3(0, -0.5f, 0), Kind = PhysicsBodyKind.Static });
        harness.Add(new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Cylinder, Radius = 0.2f, Length = 2.5f, Position = new Vector3(0, 1.25f, 0), Kind = PhysicsBodyKind.Static });
        var torus = harness.Add(new PhysicsBody
                                    {
                                        Id = Guid.NewGuid(), Shape = PhysicsShapeType.ConvexHull, Hulls = hulls, Position = new Vector3(0, 4, 0),
                                    });
        harness.Step(4f);
        harness.Sim.TryGetBodyPosition(torus.Id, out var position);
        return position.Y;
    }

    private static MeshGeometry Box(Vector3 center, Vector3 size)
    {
        var half = size * 0.5f;
        var positions = new Vector3[8];
        for (var index = 0; index < 8; index++)
            positions[index] = center + new Vector3((index & 1) == 0 ? -half.X : half.X, (index & 2) == 0 ? -half.Y : half.Y, (index & 4) == 0 ? -half.Z : half.Z);

        // Counter-clockwise seen from outside.
        int[] quads = [0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5];
        var offsets = new int[7];
        for (var face = 0; face <= 6; face++)
            offsets[face] = face * 4;

        return new MeshGeometry
                   {
                       Positions = positions,
                       CornerPointIndices = quads,
                       FaceCornerOffsets = offsets,
                       Parts = [new GeometryPart(0, 6, center, 0, 0)],
                   };
    }

    private static MeshGeometry Torus(float majorRadius, float minorRadius, int major = 32, int minor = 16)
    {
        return Grid(major, minor, (u, v) =>
                                  {
                                      var (sinU, cosU) = MathF.SinCos(u * MathF.Tau);
                                      var (sinV, cosV) = MathF.SinCos(v * MathF.Tau);
                                      var ring = majorRadius + minorRadius * cosV;
                                      return new Vector3(ring * cosU, minorRadius * sinV, ring * sinU);
                                  });
    }

    private static MeshGeometry Sphere(float radius, int segments = 24)
    {
        return Grid(segments, segments, (u, v) =>
                                        {
                                            var (sinU, cosU) = MathF.SinCos(u * MathF.Tau);
                                            var (sinV, cosV) = MathF.SinCos((v - 0.5f) * MathF.PI);
                                            return new Vector3(radius * cosV * cosU, radius * sinV, radius * cosV * sinU);
                                        }, wrapV: false);
    }

    /// <summary>A closed quad grid over (u, v); poles of a sphere become degenerate quads, which the voxelizer ignores.</summary>
    private static MeshGeometry Grid(int countU, int countV, Func<float, float, Vector3> surface, bool wrapV = true)
    {
        var rowsV = wrapV ? countV : countV + 1;
        var positions = new Vector3[countU * rowsV];
        for (var v = 0; v < rowsV; v++)
        {
            for (var u = 0; u < countU; u++)
                positions[u + v * countU] = surface(u / (float)countU, v / (float)countV);
        }

        var corners = new List<int>();
        var offsets = new List<int> { 0 };
        for (var v = 0; v < countV; v++)
        {
            for (var u = 0; u < countU; u++)
            {
                var nextU = (u + 1) % countU;
                var nextV = wrapV ? (v + 1) % countV : v + 1;
                corners.Add(u + v * countU);
                corners.Add(u + nextV * countU);
                corners.Add(nextU + nextV * countU);
                corners.Add(nextU + v * countU);
                offsets.Add(corners.Count);
            }
        }

        return new MeshGeometry { Positions = positions, CornerPointIndices = corners.ToArray(), FaceCornerOffsets = offsets.ToArray() };
    }

    private static MeshGeometry Merge(MeshGeometry first, MeshGeometry second)
    {
        var positions = first.Positions.Concat(second.Positions).ToArray();
        var corners = first.CornerPointIndices.Concat(second.CornerPointIndices.Select(index => index + first.PointCount)).ToArray();
        var offsets = first.FaceCornerOffsets.Concat(second.FaceCornerOffsets.Skip(1).Select(offset => offset + first.CornerCount)).ToArray();
        return new MeshGeometry
                   {
                       Positions = positions,
                       CornerPointIndices = corners,
                       FaceCornerOffsets = offsets,
                       Parts =
                       [
                           new GeometryPart(0, first.FaceCount, first.Parts.Length > 0 ? first.Parts[0].Pivot : Vector3.Zero, 0, 0),
                           new GeometryPart(first.FaceCount, second.FaceCount, second.Parts.Length > 0 ? second.Parts[0].Pivot : Vector3.Zero, 1, 1),
                       ],
                   };
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Sim = new PhysicsSimulation(this);
            Settings = new PhysicsWorldSettings
                           {
                               Gravity = new Vector3(0, -9.81f, 0),
                               ContactStiffness = 30f,
                               FixedRate = 60f,
                               Substeps = 2,
                               SolverIterations = 4,
                               TimeScale = 1f,
                               Interpolate = true,
                           };
            Tick();
        }

        public PhysicsSimulation Sim { get; }

        public PhysicsWorldSettings Settings;

        public List<PhysicsForce> Forces { get; } = new();

        public PhysicsBody Add(PhysicsBody body)
        {
            _bodies.Add(body);
            return body;
        }

        public void Step(float seconds)
        {
            var frames = Math.Max(1, (int)Math.Round(seconds / FrameSec));
            for (var frame = 0; frame < frames; frame++)
            {
                _time += FrameSec;
                Tick();
            }
        }

        public void Dispose() => Sim.Dispose();

        private void Tick() => Sim.Update(Settings, _time, _bodies, new List<PhysicsJoint>(), Forces);

        private readonly List<PhysicsBody> _bodies = new();
        private double _time;
    }
}
