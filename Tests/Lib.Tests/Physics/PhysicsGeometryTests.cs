using System.Numerics;
using Lib.physics;
using T3.Core.DataTypes;
using Xunit;

namespace Lib.Tests.Physics;

/// <summary>
/// The step between a <see cref="MeshGeometry"/> and the collision data of a body: which points make up a
/// piece, where its pivot is, and whether a change in the geometry is noticed.
/// </summary>
public class PhysicsGeometryTests
{
    [Fact]
    public void EachPartBecomesAHullAroundItsPivot()
    {
        var geometry = TwoCubes(out var pivotA, out var pivotB);
        var hulls = PhysicsGeometry.BuildHulls(geometry, merge: false);

        Assert.Equal(2, hulls.Length);
        Assert.Equal(pivotA, hulls[0].Pivot);
        Assert.Equal(pivotB, hulls[1].Pivot);
        Assert.Equal(8, hulls[0].Points.Length);
        Assert.Equal(8, hulls[1].Points.Length);

        // Points are stored relative to the pivot: a unit cube spans -0.5..0.5 whatever its place in the geometry.
        foreach (var hull in hulls)
        {
            foreach (var point in hull.Points)
            {
                Assert.Equal(0.5f, MathF.Abs(point.X), 0.0001f);
                Assert.Equal(0.5f, MathF.Abs(point.Y), 0.0001f);
                Assert.Equal(0.5f, MathF.Abs(point.Z), 0.0001f);
            }
        }
    }

    [Fact]
    public void MergeAndPartlessGeometryGiveOneHullAroundTheOrigin()
    {
        var geometry = TwoCubes(out _, out _);
        var merged = PhysicsGeometry.BuildHulls(geometry, merge: true);
        Assert.Single(merged);
        Assert.Equal(16, merged[0].Points.Length);
        Assert.Equal(Vector3.Zero, merged[0].Pivot);

        geometry.Parts = [];
        var partless = PhysicsGeometry.BuildHulls(geometry, merge: false);
        Assert.Single(partless);
        Assert.Equal(16, partless[0].Points.Length);
    }

    [Fact]
    public void QuadsAreFannedIntoTriangles()
    {
        var geometry = TwoCubes(out _, out _);
        var triangles = PhysicsGeometry.BuildTriangles(geometry);

        // Two cubes, six quads each, two triangles per quad.
        Assert.Equal(2 * 6 * 2 * 3, triangles.Length);
        Assert.Equal(geometry.GetTriangleCount() * 3, triangles.Length);
    }

    [Fact]
    public void OutOfRangeIndicesAreSkippedInsteadOfThrowing()
    {
        var geometry = TwoCubes(out _, out _);
        geometry.CornerPointIndices[0] = 9999;
        geometry.CornerPointIndices[5] = -3;
        geometry.Parts = [new GeometryPart(-5, 3, Vector3.Zero, 0, 0), new GeometryPart(10, 500, Vector3.Zero, 1, 1)];

        var hulls = PhysicsGeometry.BuildHulls(geometry, merge: false);
        var triangles = PhysicsGeometry.BuildTriangles(geometry);

        Assert.Equal(2, hulls.Length);
        Assert.True(triangles.Length < 72);
        Assert.Equal(0, triangles.Length % 3);
    }

    [Fact]
    public void LargePiecesAreThinnedOut()
    {
        const int pointCount = PhysicsGeometry.MaxHullPoints * 3 + 17;
        var geometry = new MeshGeometry
                           {
                               Positions = new Vector3[pointCount],
                               CornerPointIndices = new int[pointCount],
                               FaceCornerOffsets = new int[pointCount / 3 + 1],
                           };
        for (var index = 0; index < pointCount; index++)
        {
            geometry.Positions[index] = new Vector3(MathF.Cos(index), MathF.Sin(index * 0.7f), MathF.Cos(index * 1.3f));
            geometry.CornerPointIndices[index] = index;
        }

        for (var face = 0; face < geometry.FaceCornerOffsets.Length; face++)
            geometry.FaceCornerOffsets[face] = face * 3;

        var hull = PhysicsGeometry.BuildHulls(geometry, merge: false)[0];
        Assert.InRange(hull.Points.Length, PhysicsGeometry.MaxHullPoints / 2, PhysicsGeometry.MaxHullPoints);
    }

    [Fact]
    public void StampNoticesAPointMovedInPlace()
    {
        var geometry = TwoCubes(out _, out _);
        var before = PhysicsGeometry.Stamp(geometry);
        Assert.Equal(before, PhysicsGeometry.Stamp(geometry));

        // Producers rebuild into the same instance, so the reference and the counts stay the same.
        geometry.Positions[3] += new Vector3(0, 0.25f, 0);
        var moved = PhysicsGeometry.Stamp(geometry);
        Assert.NotEqual(before, moved);

        geometry.Parts[1] = geometry.Parts[1] with { Pivot = new Vector3(9, 9, 9) };
        Assert.NotEqual(moved, PhysicsGeometry.Stamp(geometry));
    }

    [Fact]
    public void GeometryPiecesSimulateAndStayPublishedAtTheirPivots()
    {
        var geometry = TwoCubes(out var pivotA, out var pivotB);
        var hulls = PhysicsGeometry.BuildHulls(geometry, merge: false);

        using var simulation = new PhysicsSimulation(this);
        var settings = new PhysicsWorldSettings { Gravity = new Vector3(0, -9.81f, 0), ContactStiffness = 30f, FixedRate = 60f, Substeps = 2, SolverIterations = 4, TimeScale = 1f };
        var bodies = new List<PhysicsBody>
                         {
                             new() { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Position = new Vector3(0, -0.5f, 0), Size = new Vector3(40, 1, 40) },
                             new()
                                 {
                                     Id = Guid.NewGuid(),
                                     Shape = PhysicsShapeType.ConvexHull,
                                     Hulls = hulls,
                                     InstanceCount = 2,
                                     Instances =
                                         [
                                             new PhysicsBodyInstance { Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, HullIndex = 0 },
                                             new PhysicsBodyInstance { Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, HullIndex = 1 },
                                         ],
                                 },
                         };
        var joints = new List<PhysicsJoint>();
        var points = new Point[2];

        simulation.Update(settings, 0, bodies, joints);
        simulation.WritePoints(points, false);
        Assert.True(Vector3.Distance(pivotA, points[0].Position) < 0.001f, "a piece starts exactly where the geometry has it");
        Assert.True(Vector3.Distance(pivotB, points[1].Position) < 0.001f);

        for (var frame = 1; frame <= 240; frame++)
            simulation.Update(settings, frame / 60.0, bodies, joints);

        simulation.WritePoints(points, false);
        Assert.InRange(points[0].Position.Y, 0.45f, 0.56f);
        Assert.InRange(points[1].Position.Y, 0.45f, 0.56f);
        Assert.Equal(pivotA.X, points[0].Position.X, 0.05f);
        Assert.Equal(pivotB.X, points[1].Position.X, 0.05f);
    }

    [Fact]
    public void FracturedGeometryComesBackWithEveryPartMovedByItsBody()
    {
        var geometry = TwoCubes(out var pivotA, out var pivotB);
        var body = new PhysicsBody
                       {
                           Id = Guid.NewGuid(),
                           Shape = PhysicsShapeType.ConvexHull,
                           Hulls = PhysicsGeometry.BuildHulls(geometry, merge: false),
                           SourceGeometry = geometry,
                           SourcePerPart = true,
                           InstanceCount = 2,
                           Instances =
                               [
                                   new PhysicsBodyInstance { Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, HullIndex = 0 },
                                   new PhysicsBodyInstance { Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, HullIndex = 1 },
                               ],
                       };
        var posed = new MeshGeometry();
        var points = Simulate(body, withGround: true, seconds: 0f);

        // Before anything moves the result is the source, point for point.
        Assert.Equal(2, Pose(body, posed));
        for (var index = 0; index < geometry.Positions.Length; index++)
            Assert.True(Vector3.Distance(geometry.Positions[index], posed.Positions[index]) < 0.001f);

        points = Simulate(body, withGround: true, seconds: 4f, points);
        Assert.Equal(2, Pose(body, posed));

        // Both cubes rest on the ground: every point is at height 0 or 1, and the topology is the source's own.
        foreach (var position in posed.Positions)
            Assert.True(MathF.Abs(position.Y) < 0.06f || MathF.Abs(position.Y - 1f) < 0.06f, $"point at {position}");

        Assert.Same(geometry.CornerPointIndices, posed.CornerPointIndices);
        Assert.Equal(2, posed.Parts.Length);
        Assert.InRange(posed.Parts[0].Pivot.Y, 0.45f, 0.56f);
        Assert.Equal(pivotA.X, posed.Parts[0].Pivot.X, 0.05f);
        Assert.Equal(pivotB.X, posed.Parts[1].Pivot.X, 0.05f);

        // The source is never written to.
        Assert.Equal(pivotA, geometry.Parts[0].Pivot);
        Assert.Equal(pivotA.Y - 0.5f, geometry.Positions[0].Y, 0.0001f);
    }

    [Fact]
    public void GeometryAtPointsComesBackAsOneCopyPerBody()
    {
        var geometry = TwoCubes(out _, out _);
        geometry.Parts = [];
        var body = new PhysicsBody
                       {
                           Id = Guid.NewGuid(),
                           Shape = PhysicsShapeType.ConvexHull,
                           Hulls = PhysicsGeometry.BuildHulls(geometry, merge: true),
                           SourceGeometry = geometry,
                           InstanceCount = 3,
                           Instances = new PhysicsBodyInstance[3],
                       };
        for (var index = 0; index < 3; index++)
        {
            body.Instances[index] = new PhysicsBodyInstance
                                        {
                                            Position = new Vector3(index * 30f, 0, 0),
                                            Orientation = Quaternion.Identity,
                                            Scale = new Vector3(index == 2 ? 2f : 1f),
                                            Color = new Vector4(index, 0, 0, 1),
                                            HullIndex = 0,
                                        };
        }

        Simulate(body, withGround: false, seconds: 0f);
        var posed = new MeshGeometry();
        Assert.Equal(3, Pose(body, posed));

        Assert.Equal(geometry.PointCount * 3, posed.PointCount);
        Assert.Equal(geometry.FaceCount * 3, posed.FaceCount);
        Assert.Equal(3, posed.Parts.Length);
        Assert.True(Vector3.Distance(posed.Positions[0], geometry.Positions[0]) < 0.001f);
        Assert.True(Vector3.Distance(posed.Positions[geometry.PointCount], geometry.Positions[0] + new Vector3(30, 0, 0)) < 0.001f);
        Assert.True(Vector3.Distance(posed.Positions[geometry.PointCount * 2], geometry.Positions[0] * 2f + new Vector3(60, 0, 0)) < 0.001f);
        Assert.True(posed.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Part, out var colors));
        Assert.Equal(2f, colors.Values[2].X);

        // Every corner index stays inside its own copy.
        for (var corner = 0; corner < posed.CornerCount; corner++)
            Assert.Equal(corner / geometry.CornerCount, posed.CornerPointIndices[corner] / geometry.PointCount);
    }

    [Theory]
    [InlineData(PhysicsShapeType.Box)]
    [InlineData(PhysicsShapeType.Sphere)]
    [InlineData(PhysicsShapeType.Capsule)]
    [InlineData(PhysicsShapeType.Cylinder)]
    public void PrimitiveBodyComesBackAsGeometryOfItsOwnSize(PhysicsShapeType shape)
    {
        var body = new PhysicsBody { Id = Guid.NewGuid(), Shape = shape, Size = new Vector3(2, 4, 6), Radius = 1.5f, Length = 3f, Position = new Vector3(10, 20, 30) };
        Simulate(body, withGround: false, seconds: 0f, gravity: false);
        var posed = new MeshGeometry();
        Assert.Equal(1, Pose(body, posed));

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in posed.Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        var expected = shape switch
                           {
                               PhysicsShapeType.Box      => new Vector3(2, 4, 6),
                               PhysicsShapeType.Sphere   => new Vector3(3, 3, 3),
                               PhysicsShapeType.Capsule  => new Vector3(3, 6, 3),
                               _                         => new Vector3(3, 3, 3),
                           };
        Assert.True(Vector3.Distance(expected, max - min) < 0.08f, $"{shape}: extent {max - min}");
        Assert.True(Vector3.Distance(new Vector3(10, 20, 30), (min + max) * 0.5f) < 0.01f);

        // Faces wind counter-clockwise seen from outside, like the rest of the geometry library.
        Assert.True(posed.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var normals));
        var centre = new Vector3(10, 20, 30);
        for (var face = 0; face < posed.FaceCount; face++)
        {
            var start = posed.FaceCornerOffsets[face];
            var a = posed.Positions[posed.CornerPointIndices[start]];
            var b = posed.Positions[posed.CornerPointIndices[start + 1]];
            var c = posed.Positions[posed.CornerPointIndices[start + 2]];
            var d = posed.Positions[posed.CornerPointIndices[start + 3]];
            var faceNormal = Vector3.Cross(b - a, c - b) + Vector3.Cross(d - c, a - d);
            Assert.True(Vector3.Dot(faceNormal, (a + b + c + d) * 0.25f - centre) > 0f, $"{shape}: face {face} winds inwards");
            Assert.True(Vector3.Dot(normals.Values[start], faceNormal) > 0f, $"{shape}: normal of face {face} points inwards");
        }
    }

    [Fact]
    public void SeveralBodiesAreJoinedIntoOneGeometry()
    {
        var box = new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Box, Size = new Vector3(2, 2, 2) };
        var ball = new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Sphere, Radius = 1f, Position = new Vector3(50, 0, 0) };
        var unsimulated = new PhysicsBody { Id = Guid.NewGuid() };

        using var simulation = new PhysicsSimulation(new object());
        var settings = new PhysicsWorldSettings { ContactStiffness = 30f, FixedRate = 60f, Substeps = 2, SolverIterations = 4, TimeScale = 1f };
        simulation.Update(settings, 0, [box, ball], []);
        var points = new Point[simulation.RenderCount(true)];
        simulation.WritePoints(points, true);
        simulation.PublishBodyRanges(points, new BufferWithViews());

        var joined = new MeshGeometry();
        var scratch = new List<PhysicsGeometry.PosedBody>();
        Assert.Equal(2, PhysicsGeometry.BuildPosed([box, unsimulated, ball], joined, scratch));

        var boxAlone = new MeshGeometry();
        var ballAlone = new MeshGeometry();
        Pose(box, boxAlone);
        Pose(ball, ballAlone);
        Assert.Equal(boxAlone.PointCount + ballAlone.PointCount, joined.PointCount);
        Assert.Equal(boxAlone.FaceCount + ballAlone.FaceCount, joined.FaceCount);
        Assert.Equal(2, joined.Parts.Length);
        Assert.Equal(boxAlone.FaceCount, joined.Parts[1].FaceStart);
        Assert.True(joined.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var normals));
        Assert.Equal(joined.CornerCount, normals.Values.Length);

        // The ball's corners point into the ball's points, which sit around x = 50.
        for (var corner = boxAlone.CornerCount; corner < joined.CornerCount; corner++)
            Assert.InRange(joined.Positions[joined.CornerPointIndices[corner]].X, 48.9f, 51.1f);

        Assert.Equal(joined.CornerCount, joined.FaceCornerOffsets[^1]);
    }

    [Fact]
    public void BodyThatHasNotBeenSimulatedGivesNothing()
    {
        var body = new PhysicsBody { Id = Guid.NewGuid() };
        Assert.Equal(0, Pose(body, new MeshGeometry()));
    }

    private static int Pose(PhysicsBody body, MeshGeometry output)
    {
        MeshGeometry? cache = null;
        (PhysicsShapeType, Vector3, float, float) key = default;
        return PhysicsGeometry.BuildPosed(body, output, ref cache!, ref key);
    }

    /// <summary>Runs a world holding the body and publishes its poses the way PhysicsWorld does.</summary>
    private static PhysicsSimulation Simulate(PhysicsBody body, bool withGround, float seconds, PhysicsSimulation? simulation = null, bool gravity = true)
    {
        simulation ??= new PhysicsSimulation(new object()) { StepBudgetSec = 100f };
        var settings = new PhysicsWorldSettings
                           {
                               Gravity = gravity && withGround ? new Vector3(0, -9.81f, 0) : Vector3.Zero,
                               ContactStiffness = 30f, FixedRate = 60f, Substeps = 2, SolverIterations = 4, TimeScale = 1f,
                           };
        var bodies = new List<PhysicsBody> { body };
        if (withGround)
            bodies.Add(new PhysicsBody { Id = GroundId, Kind = PhysicsBodyKind.Static, Position = new Vector3(0, -0.5f, 0), Size = new Vector3(60, 1, 60) });

        var joints = new List<PhysicsJoint>();
        var frames = (int)MathF.Round(seconds * 60f);
        for (var frame = 0; frame <= frames; frame++)
        {
            _clock += frame == 0 ? 0 : 1 / 60.0;
            simulation.Update(settings, _clock, bodies, joints);
        }

        var points = new Point[simulation.RenderCount(true)];
        simulation.WritePoints(points, true);
        simulation.PublishBodyRanges(points, new BufferWithViews());
        return simulation;
    }

    private static readonly Guid GroundId = Guid.NewGuid();

    [ThreadStatic]
    private static double _clock;

    /// <summary>Two unit cubes built from quads, each its own part with the pivot at its centre.</summary>
    private static MeshGeometry TwoCubes(out Vector3 pivotA, out Vector3 pivotB)
    {
        pivotA = new Vector3(-3, 2, 0);
        pivotB = new Vector3(4, 5, 1);
        int[] quads = [0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3];

        var geometry = new MeshGeometry
                           {
                               Positions = new Vector3[16],
                               CornerPointIndices = new int[48],
                               FaceCornerOffsets = new int[13],
                           };
        Vector3[] pivots = [pivotA, pivotB];
        for (var cube = 0; cube < 2; cube++)
        {
            for (var corner = 0; corner < 8; corner++)
            {
                geometry.Positions[cube * 8 + corner] = pivots[cube] + new Vector3((corner & 1) == 0 ? -0.5f : 0.5f,
                                                                                    (corner & 2) == 0 ? -0.5f : 0.5f,
                                                                                    (corner & 4) == 0 ? -0.5f : 0.5f);
            }

            for (var index = 0; index < 24; index++)
                geometry.CornerPointIndices[cube * 24 + index] = cube * 8 + quads[index];
        }

        for (var face = 0; face <= 12; face++)
            geometry.FaceCornerOffsets[face] = face * 4;

        geometry.Parts = [new GeometryPart(0, 6, pivotA, 0, 0), new GeometryPart(6, 6, pivotB, 1, 1)];
        return geometry;
    }
}
