using System.Numerics;
using Lib.physics;
using T3.Core.DataTypes;
using Xunit;

namespace Lib.Tests.Physics;

/// <summary>
/// Covers what one description can stand for beyond a single primitive: many instances, convex pieces of a
/// geometry, triangle-mesh colliders, collision groups, forces, reported contacts and the joint modes that
/// link instances. Driven the same way as <see cref="PhysicsSimulationTests"/>.
/// </summary>
public class PhysicsInstancingTests
{
    private const float FrameSec = 1f / 60f;

    [Fact]
    public void InstancedDescriptionCreatesOneBodyPerInstanceInOrder()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var spheres = Sphere(Vector3.Zero, 0.25f);
        SetInstances(spheres, 10, index => new Vector3(index * 2f, 0, 0));
        harness.Bodies.Add(spheres);
        harness.Step(0.1f);

        var points = harness.ReadPoints();
        Assert.Equal(10, points.Length);
        for (var index = 0; index < points.Length; index++)
            Assert.Equal(index * 2f, points[index].Position.X, 0.001f);
    }

    [Fact]
    public void InstancesArePlacedRelativeToTheDescription()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var spheres = Sphere(new Vector3(0, 10, 0), 0.25f);
        spheres.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        SetInstances(spheres, 1, _ => new Vector3(1, 0, 0));
        harness.Bodies.Add(spheres);
        harness.Step(FrameSec);

        // A quarter turn around Y carries +X onto -Z.
        var point = harness.ReadPoints()[0];
        Assert.Equal(0f, point.Position.X, 0.001f);
        Assert.Equal(10f, point.Position.Y, 0.001f);
        Assert.Equal(-1f, point.Position.Z, 0.001f);
    }

    [Fact]
    public void ShrinkingAndGrowingTheInstanceCountFollowsTheDescription()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var spheres = Sphere(Vector3.Zero, 0.25f);
        SetInstances(spheres, 8, index => new Vector3(index * 2f, 0, 0));
        harness.Bodies.Add(spheres);
        harness.Step(0.1f);
        Assert.Equal(8, harness.BodyCount);

        spheres.InstanceCount = 3;
        harness.Step(FrameSec);
        Assert.Equal(3, harness.BodyCount);

        spheres.InstanceCount = 6;
        harness.Step(FrameSec);
        Assert.Equal(6, harness.BodyCount);
    }

    [Fact]
    public void InstanceKeepsFallingWhileItsInstanceStaysTheSame()
    {
        using var harness = new Harness();
        var spheres = Sphere(Vector3.Zero, 0.25f);
        SetInstances(spheres, 2, index => new Vector3(index * 2f, 100, 0));
        harness.Bodies.Add(spheres);
        harness.Step(0.5f);
        var early = harness.ReadPoints()[1].Position.Y;
        harness.Step(0.5f);
        var late = harness.ReadPoints()[1].Position.Y;

        Assert.True(late < early - 1f, "an unchanged instance must not be teleported back to its input position");
    }

    [Fact]
    public void InstanceScaleColorAndSpeedArePublished()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var boxes = Box(Vector3.Zero, new Vector3(1, 2, 3));
        boxes.LinearVelocity = new Vector3(3, 0, 0);
        SetInstances(boxes, 2, index => new Vector3(0, index * 10f, 0));
        boxes.Instances[1].Scale = new Vector3(2f);
        boxes.Instances[1].Color = new Vector4(1, 0, 0, 1);
        harness.Bodies.Add(boxes);
        harness.Step(0.2f);

        var points = harness.ReadPoints();
        Assert.Equal(new Vector3(1, 2, 3), points[0].Scale);
        Assert.Equal(new Vector3(2, 4, 6), points[1].Scale);
        Assert.Equal(Vector4.One, points[0].Color);
        Assert.Equal(new Vector4(1, 0, 0, 1), points[1].Color);
        Assert.Equal(3f, points[0].F2, 0.01f);
    }

    [Fact]
    public void HullBodyIsPublishedAtItsPivotNotItsCentreOfMass()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;

        // A unit cube whose corner, not its centre, sits on the pivot; the pivot is at (5, 6, 7) in the geometry.
        var body = Hull(CubePoints(new Vector3(0.5f), 1f), new Vector3(5, 6, 7));
        harness.Bodies.Add(body);
        harness.Step(0.2f);

        var point = harness.ReadPoints()[0];
        Assert.Equal(5f, point.Position.X, 0.001f);
        Assert.Equal(6f, point.Position.Y, 0.001f);
        Assert.Equal(7f, point.Position.Z, 0.001f);

        // The simulated body itself sits at the cube's centre.
        Assert.True(harness.Sim.TryGetBodyPosition(body.Id, out var center));
        Assert.Equal(5.5f, center.X, 0.01f);
        Assert.Equal(6.5f, center.Y, 0.01f);
    }

    [Fact]
    public void HullPiecesFallAndRestOnTheGround()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));

        var pieces = new PhysicsBody
                         {
                             Id = Guid.NewGuid(),
                             Shape = PhysicsShapeType.ConvexHull,
                             Hulls =
                                 [
                                     new PhysicsHull { Points = CubePoints(Vector3.Zero, 1f), Pivot = new Vector3(-3, 2, 0) },
                                     new PhysicsHull { Points = CubePoints(Vector3.Zero, 2f), Pivot = new Vector3(3, 4, 0) },
                                 ],
                         };
        SetInstances(pieces, 2, _ => Vector3.Zero);
        pieces.Instances[0].HullIndex = 0;
        pieces.Instances[1].HullIndex = 1;
        harness.Bodies.Add(pieces);
        harness.Step(4f);

        var points = harness.ReadPoints();
        Assert.InRange(points[0].Position.Y, 0.45f, 0.56f);
        Assert.InRange(points[1].Position.Y, 0.95f, 1.06f);
        Assert.Equal(-3f, points[0].Position.X, 0.05f);
        Assert.Equal(3f, points[1].Position.X, 0.05f);
    }

    [Fact]
    public void InstancesOfOnePieceShareOneShapeUntilTheLastIsRemoved()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var body = Hull(CubePoints(Vector3.Zero, 1f), Vector3.Zero);
        SetInstances(body, 20, index => new Vector3(index * 3f, 0, 0));
        harness.Bodies.Add(body);
        harness.Step(0.1f);

        Assert.Equal(20, harness.BodyCount);
        Assert.Equal(1, harness.Sim.SharedShapeCount);

        // A different scale is a different shape.
        body.Instances[5].Scale = new Vector3(2f);
        harness.Step(FrameSec);
        Assert.Equal(2, harness.Sim.SharedShapeCount);

        harness.Bodies.Remove(body);
        harness.Step(FrameSec);
        Assert.Equal(0, harness.BodyCount);
        Assert.Equal(0, harness.Sim.SharedShapeCount);
    }

    [Fact]
    public void ReplacedHullDataRebuildsTheBody()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var body = Hull(CubePoints(Vector3.Zero, 1f), new Vector3(0, 3, 0));
        harness.Bodies.Add(body);
        harness.Step(3f);
        Assert.InRange(harness.ReadPoints()[0].Position.Y, 0.45f, 0.56f);

        // The producer hands over new arrays when its geometry changes; a taller cube must rest higher.
        body.Hulls = [new PhysicsHull { Points = CubePoints(Vector3.Zero, 3f), Pivot = new Vector3(0, 3, 0) }];
        harness.Step(3f);
        Assert.InRange(harness.ReadPoints()[0].Position.Y, 1.45f, 1.56f);
        Assert.Equal(1, harness.Sim.SharedShapeCount);
    }

    [Theory]
    [InlineData(0)] // no points
    [InlineData(3)] // too few
    [InlineData(4)] // a flat quad
    public void HullWithoutVolumeFallsBackToABox(int pointCount)
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));

        Vector3[] flat = [new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1), new(-1, 0, 1)];
        var body = Hull(flat[..pointCount], new Vector3(0, 3, 0));
        body.Size = Vector3.One;
        harness.Bodies.Add(body);
        harness.Step(3f);

        Assert.Equal(1, harness.BodyCount);
        Assert.InRange(harness.ReadPoints()[0].Position.Y, 0.45f, 0.56f);
        Assert.Equal(0, harness.Sim.SharedShapeCount);
    }

    [Fact]
    public void HullWithNonFinitePointsFallsBackToABox()
    {
        using var harness = new Harness();
        var points = CubePoints(Vector3.Zero, 1f);
        points[3] = new Vector3(float.NaN, 0, 0);
        harness.Bodies.Add(Hull(points, Vector3.Zero));
        harness.Step(0.5f);

        Assert.Equal(1, harness.BodyCount);
        Assert.True(float.IsFinite(harness.ReadPoints()[0].Position.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticMeshCatchesASphereWhicheverWayItsTrianglesWind(bool flipped)
    {
        using var harness = new Harness();
        harness.Bodies.Add(new PhysicsBody
                               {
                                   Id = Guid.NewGuid(),
                                   Shape = PhysicsShapeType.Mesh,
                                   Kind = PhysicsBodyKind.Static,
                                   MeshTriangles = QuadTriangles(10f, flipped),
                               });
        var sphere = harness.Add(Sphere(new Vector3(0.3f, 3, 0.2f)));
        harness.Step(3f);

        Assert.True(harness.Sim.TryGetBodyPosition(sphere.Id, out var position));
        Assert.InRange(position.Y, 0.45f, 0.56f);
    }

    [Fact]
    public void StaticMeshInstancesAreScaledAndShareTheirShape()
    {
        using var harness = new Harness();
        var floors = new PhysicsBody
                         {
                             Id = Guid.NewGuid(),
                             Shape = PhysicsShapeType.Mesh,
                             Kind = PhysicsBodyKind.Static,
                             MeshTriangles = QuadTriangles(1f, false),
                         };
        SetInstances(floors, 3, index => new Vector3(index * 100f, 0, 0));
        for (var index = 0; index < 3; index++)
            floors.Instances[index].Scale = new Vector3(10f);

        harness.Bodies.Add(floors);

        // Three units from the centre is off the 1-unit quad and on the scaled one.
        var sphere = harness.Add(Sphere(new Vector3(203f, 3, 0)));
        harness.Step(3f);

        Assert.Equal(1, harness.Sim.SharedShapeCount);
        Assert.True(harness.Sim.TryGetBodyPosition(sphere.Id, out var position));
        Assert.InRange(position.Y, 0.45f, 0.56f);
    }

    [Fact]
    public void DynamicMeshBodyIsSimulatedAsItsHull()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var body = harness.Add(new PhysicsBody
                                   {
                                       Id = Guid.NewGuid(),
                                       Shape = PhysicsShapeType.Mesh,
                                       Kind = PhysicsBodyKind.Dynamic,
                                       Position = new Vector3(0, 3, 0),
                                       MeshTriangles = CubeTriangles(1f),
                                   });
        harness.Step(3f);

        Assert.True(harness.Sim.TryGetBodyPosition(body.Id, out var position));
        Assert.InRange(position.Y, 0.45f, 0.56f);
    }

    [Fact]
    public void MeshWithoutTrianglesFallsBackToABox()
    {
        using var harness = new Harness();
        harness.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Mesh, Kind = PhysicsBodyKind.Static });
        harness.Bodies.Add(new PhysicsBody
                               {
                                   Id = Guid.NewGuid(),
                                   Shape = PhysicsShapeType.Mesh,
                                   Kind = PhysicsBodyKind.Static,
                                   MeshTriangles = [Vector3.Zero, Vector3.Zero, Vector3.Zero],
                               });
        harness.IncludeStatics = true;
        harness.Step(0.1f);

        Assert.Equal(2, harness.BodyCount);
    }

    [Fact]
    public void BodiesOfTheSameGroupPassThroughEachOther()
    {
        Assert.True(RunHeadOnSpheres(groupA: 3, groupB: 3), "same non-zero group must not collide");
        Assert.False(RunHeadOnSpheres(groupA: 3, groupB: 4), "different groups collide");
        Assert.False(RunHeadOnSpheres(groupA: 0, groupB: 0), "group 0 collides with everything");
    }

    /// <summary>Two spheres fly at each other; returns whether the left one ended up right of the right one.</summary>
    private static bool RunHeadOnSpheres(int groupA, int groupB)
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var left = harness.Add(Sphere(new Vector3(-2, 0, 0), 0.5f, new Vector3(4, 0, 0)));
        var right = harness.Add(Sphere(new Vector3(2, 0, 0), 0.5f, new Vector3(-4, 0, 0)));
        left.CollisionGroup = groupA;
        right.CollisionGroup = groupB;
        harness.Step(1.5f);

        harness.Sim.TryGetBodyPosition(left.Id, out var leftPosition);
        harness.Sim.TryGetBodyPosition(right.Id, out var rightPosition);
        return leftPosition.X > rightPosition.X;
    }

    [Fact]
    public void DirectionalForceAcceleratesABody()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var sphere = harness.Add(Sphere(Vector3.Zero));
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Direction = new Vector3(5, 0, 0), Strength = 2f });
        harness.Step(1f);

        // The direction is normalized, so the strength alone is the acceleration: x = a t² / 2.
        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.InRange(position.X, 0.9f, 1.1f);
        Assert.Equal(0f, position.Y, 0.001f);
    }

    [Fact]
    public void ScaleByMassMakesHeavyBodiesReactLess()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var light = harness.Add(Sphere(Vector3.Zero));
        var heavy = harness.Add(Sphere(new Vector3(0, 10, 0)));
        heavy.Mass = 4f;
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Direction = Vector3.UnitX, Strength = 2f, ScaleByMass = true });
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(light.Id, out var lightPosition);
        harness.Sim.TryGetBodyPosition(heavy.Id, out var heavyPosition);
        Assert.Equal(4f, lightPosition.X / heavyPosition.X, 0.1f);
    }

    [Fact]
    public void RadialForcePullsInsideItsRadiusOnly()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var near = harness.Add(Sphere(new Vector3(2, 0, 0)));
        var far = harness.Add(Sphere(new Vector3(20, 0, 0)));
        harness.Forces.Add(new PhysicsForce
                               {
                                   Id = Guid.NewGuid(),
                                   Type = PhysicsForceType.Radial,
                                   Position = Vector3.Zero,
                                   Strength = 3f,
                                   Radius = 5f,
                                   Falloff = 0f,
                               });
        harness.Step(0.5f);

        harness.Sim.TryGetBodyPosition(near.Id, out var nearPosition);
        harness.Sim.TryGetBodyPosition(far.Id, out var farPosition);
        Assert.True(nearPosition.X < 1.8f, "a body inside the radius is pulled towards the centre");
        Assert.Equal(20f, farPosition.X, 0.0001f);
    }

    [Fact]
    public void VortexForcePushesAroundItsAxis()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var sphere = harness.Add(Sphere(new Vector3(2, 0, 0)));
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Vortex, Direction = Vector3.UnitY, Strength = 2f });
        harness.Step(0.25f);

        // Y cross X is -Z: the first push is tangential, not towards or away from the axis.
        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.True(position.Z < -0.03f);
        Assert.Equal(0f, position.Y, 0.001f);
    }

    [Fact]
    public void FieldForceSamplesTheFieldAtEachBody()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var left = harness.Add(Sphere(new Vector3(-5, 0, 0)));
        var right = harness.Add(Sphere(new Vector3(5, 0, 0)));

        // Pushes up on the right side only.
        var field = new VectorField((in FieldSample sample) => sample.Position.X > 0 ? Vector3.UnitY : Vector3.Zero);
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Field, Field = field, Strength = 2f });
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(left.Id, out var leftPosition);
        harness.Sim.TryGetBodyPosition(right.Id, out var rightPosition);
        Assert.Equal(0f, leftPosition.Y, 0.0001f);
        Assert.InRange(rightPosition.Y, 0.9f, 1.1f);
    }

    [Fact]
    public void ThrowingOrNonFiniteFieldDoesNotBreakTheWorld()
    {
        using var harness = new Harness();
        var sphere = harness.Add(Sphere(new Vector3(0, 100, 0)));
        harness.Forces.Add(new PhysicsForce
                               {
                                   Id = Guid.NewGuid(),
                                   Type = PhysicsForceType.Field,
                                   Field = new VectorField((in FieldSample _) => throw new InvalidOperationException("graph code failed")),
                               });
        harness.Forces.Add(new PhysicsForce
                               {
                                   Id = Guid.NewGuid(),
                                   Type = PhysicsForceType.Field,
                                   Field = new VectorField((in FieldSample _) => new Vector3(float.NaN)),
                               });
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Field, Field = null });
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Strength = float.NaN });
        harness.Step(1f);

        // Gravity still works and nothing was poisoned.
        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.InRange(position.Y, 94f, 96f);
    }

    [Fact]
    public void ImpulseFiresOncePerTriggerAndNotOnFirstSight()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var sphere = harness.Add(Sphere(Vector3.Zero));
        var impulse = new PhysicsForce { Id = Guid.NewGuid(), Direction = Vector3.UnitX, Strength = 3f, IsImpulse = true, TriggerCount = 7 };
        harness.Forces.Add(impulse);
        harness.Step(0.5f);

        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.Equal(0f, position.X, 0.0001f);

        impulse.TriggerCount++;
        harness.Step(1f);
        harness.Sim.TryGetBodyPosition(sphere.Id, out position);

        // One velocity change of 3 m/s, held for a second - not 3 m/s added every step.
        Assert.InRange(position.X, 2.8f, 3.1f);
    }

    [Fact]
    public void ForceOnlyAffectsItsCollisionGroup()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var inGroup = harness.Add(Sphere(Vector3.Zero));
        var other = harness.Add(Sphere(new Vector3(0, 10, 0)));
        inGroup.CollisionGroup = 2;
        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Direction = Vector3.UnitX, Strength = 2f, CollisionGroup = 2 });
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(inGroup.Id, out var movedPosition);
        harness.Sim.TryGetBodyPosition(other.Id, out var otherPosition);
        Assert.True(movedPosition.X > 0.5f);
        Assert.Equal(0f, otherPosition.X, 0.0001f);
    }

    [Fact]
    public void ForceMovesABodyThatHasGoneToSleep()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(400, 1, 400), PhysicsBodyKind.Static));
        var box = harness.Add(Box(new Vector3(0, 0.5f, 0), Vector3.One));
        box.Friction = 0f;
        harness.Step(6f);
        harness.Sim.TryGetBodyPosition(box.Id, out var resting);

        harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Direction = Vector3.UnitX, Strength = 4f });
        harness.Step(1f);
        harness.Sim.TryGetBodyPosition(box.Id, out var pushed);

        Assert.True(pushed.X > resting.X + 0.5f, "a continuous force has to wake the body it pushes");
    }

    [Fact]
    public void FallOntoTheGroundIsReportedAsOneContact()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        harness.Bodies.Add(Sphere(new Vector3(2, 5, 3)));
        harness.Step(4f);

        Assert.Single(harness.AllContacts);
        var contact = harness.AllContacts[0];

        // Falling 4.5 m to the surface: v = sqrt(2 g h), about 9.4 m/s.
        Assert.InRange(contact.Speed, 8.5f, 10f);
        Assert.Equal(2f, contact.Position.X, 0.1f);
        Assert.Equal(3f, contact.Position.Z, 0.1f);
        Assert.InRange(contact.Position.Y, -0.3f, 0.3f);
        Assert.True(MathF.Abs(contact.Normal.Y) > 0.9f);
    }

    [Fact]
    public void ContactsSurviveASecondUpdateInTheSameFrameAndClearInTheNext()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        harness.Bodies.Add(Sphere(new Vector3(0, 0.51f, 0), 0.5f, new Vector3(0, -5, 0)));
        harness.Step(FrameSec);
        Assert.Single(harness.Sim.Contacts);

        // The operator updates once for each output that is pulled.
        harness.TickAgain();
        Assert.Single(harness.Sim.Contacts);

        harness.Step(FrameSec);
        Assert.Empty(harness.Sim.Contacts);
    }

    [Fact]
    public void RestingAndSlowContactsAreNotReported()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));

        // Starts touching the ground: it never closes faster than a resting contact does.
        harness.Bodies.Add(Box(new Vector3(0, 0.5f, 0), Vector3.One));
        harness.Step(3f);

        Assert.Empty(harness.AllContacts);
    }

    [Fact]
    public void ContactThresholdFiltersWeakImpacts()
    {
        using var harness = new Harness();
        harness.Settings.ContactSpeedThreshold = 20f;
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        harness.Bodies.Add(Sphere(new Vector3(0, 5, 0)));
        harness.Step(4f);

        Assert.Empty(harness.AllContacts);
    }

    [Fact]
    public void BouncingBallReportsEachImpact()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var ball = harness.Add(Sphere(new Vector3(0, 5, 0)));
        ball.Bounciness = 0.8f;
        harness.Step(5f);

        Assert.True(harness.AllContacts.Count >= 3, $"expected several bounces, got {harness.AllContacts.Count}");
        Assert.True(harness.AllContacts[0].Speed > harness.AllContacts[^1].Speed, "each bounce is weaker than the first");
    }

    [Fact]
    public void WriteContactsPadsUnusedPointsAndKeepsTheStrongest()
    {
        using var harness = new Harness();
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(400, 1, 400), PhysicsBodyKind.Static));

        // A hair above the ground at different speeds: three impacts in the same step.
        harness.Bodies.Add(Sphere(new Vector3(0, 0.51f, 0), 0.5f, new Vector3(0, -3, 0)));
        harness.Bodies.Add(Sphere(new Vector3(5, 0.51f, 0), 0.5f, new Vector3(0, -6, 0)));
        harness.Bodies.Add(Sphere(new Vector3(10, 0.51f, 0), 0.5f, new Vector3(0, -9, 0)));

        var seen = 0;
        var roomy = new Point[5];
        var tight = new Point[1];
        for (var frame = 0; frame < 30 && seen == 0; frame++)
        {
            harness.Step(FrameSec);
            if (harness.Sim.Contacts.Count < 3)
                continue;

            seen = harness.Sim.WriteContacts(roomy);
            harness.Sim.WriteContacts(tight);
        }

        Assert.Equal(3, seen);
        Assert.True(roomy[0].F1 > 0f);
        Assert.Equal(Vector3.One, roomy[2].Scale);
        Assert.Equal(Vector3.Zero, roomy[3].Scale);
        Assert.Equal(0f, roomy[4].F1);
        Assert.InRange(tight[0].F1, 8.5f, 9.5f);
        Assert.Equal(10f, tight[0].Position.X, 0.1f);
    }

    [Fact]
    public void ChainModeKeepsConsecutiveInstancesTogether()
    {
        float MiddleGap(bool chained)
        {
            using var harness = new Harness();
            harness.Settings.Gravity = Vector3.Zero;
            var beads = Sphere(Vector3.Zero, 0.2f);
            SetInstances(beads, 6, index => new Vector3(index, 0, 0));
            harness.Bodies.Add(beads);

            // Pulls the left half left and the right half right: the row tears in the middle.
            var field = new VectorField((in FieldSample sample) => sample.Position.X < 2.5f ? -Vector3.UnitX : Vector3.UnitX);
            harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Field, Field = field, Strength = 3f });
            if (chained)
                harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = beads, Mode = PhysicsJointMode.Chain });

            harness.Step(1f);
            if (chained)
                Assert.Equal(5, harness.Sim.ConstraintCount);

            var points = harness.ReadPoints();
            return Vector3.Distance(points[2].Position, points[3].Position);
        }

        Assert.True(MiddleGap(false) > 3f, "without the chain the halves drift apart");
        Assert.InRange(MiddleGap(true), 0.95f, 1.2f);
    }

    [Fact]
    public void WorldModeHoldsEveryInstanceWhereItStarts()
    {
        using var harness = new Harness();
        var beads = Sphere(Vector3.Zero, 0.2f);
        SetInstances(beads, 4, index => new Vector3(index * 2f, 5, 0));
        harness.Bodies.Add(beads);
        harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = beads, Mode = PhysicsJointMode.World });
        harness.Step(2f);

        Assert.Equal(4, harness.Sim.ConstraintCount);
        var points = harness.ReadPoints();
        for (var index = 0; index < points.Length; index++)
            Assert.True(Vector3.Distance(points[index].Position, new Vector3(index * 2f, 5, 0)) < 0.05f, "a world joint holds its body against gravity");
    }

    [Fact]
    public void InstanceIndexPinsARopeByOneLinkOnly()
    {
        using var harness = new Harness();
        var beads = Sphere(Vector3.Zero, 0.2f);
        beads.CollisionGroup = 1;
        SetInstances(beads, 6, index => new Vector3(index * 0.5f, 5, 0));
        harness.Bodies.Add(beads);
        harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = beads, Mode = PhysicsJointMode.Chain });
        var pin = new PhysicsJoint { Id = Guid.NewGuid(), BodyA = beads, Mode = PhysicsJointMode.World, InstanceIndex = 0 };
        harness.Joints.Add(pin);
        harness.Step(4f);

        // Five links plus one pin; the rope swings down and hangs below its first bead.
        Assert.Equal(6, harness.Sim.ConstraintCount);
        var points = harness.ReadPoints();
        Assert.True(Vector3.Distance(points[0].Position, new Vector3(0, 5, 0)) < 0.1f, "the pinned bead stays put");
        Assert.True(points[5].Position.Y < 3.2f, $"the free end must hang down, is at {points[5].Position}");
        Assert.InRange(Vector3.Distance(points[4].Position, points[5].Position), 0.4f, 0.65f);

        // An index past the end pins the last bead instead.
        pin.InstanceIndex = 999;
        harness.Step(FrameSec);
        Assert.Equal(6, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void InstanceIndexLinksOneInstanceToASingleBody()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var anchor = harness.Add(Sphere(new Vector3(0, 3, 0), 0.2f));
        anchor.Kind = PhysicsBodyKind.Kinematic;
        var beads = Sphere(Vector3.Zero, 0.2f);
        SetInstances(beads, 4, index => new Vector3(index, 0, 0));
        harness.Bodies.Add(beads);
        // A distance servo, because a ball socket with no offsets would pull the bead into the anchor and
        // the contact between them would be counted as a constraint too.
        var joint = new PhysicsJoint
                        {
                            Id = Guid.NewGuid(),
                            BodyA = anchor,
                            BodyB = beads,
                            InstanceIndex = 2,
                            Type = PhysicsJointType.DistanceServo,
                            TargetDistance = 3f,
                        };
        harness.Joints.Add(joint);
        harness.Step(0.2f);
        Assert.Equal(1, harness.Sim.ConstraintCount);

        joint.InstanceIndex = -1;
        harness.Step(FrameSec);
        Assert.Equal(4, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void EveryDescriptionIsToldWhereItsBodiesAre()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var ground = Box(new Vector3(0, -5, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static);
        var beads = Sphere(Vector3.Zero, 0.2f);
        SetInstances(beads, 5, index => new Vector3(index * 2f, 0, 0));
        var single = Sphere(new Vector3(0, 9, 0));
        var disabled = Sphere(new Vector3(0, 20, 0));
        disabled.Enabled = false;
        harness.Bodies.AddRange([ground, beads, disabled, single]);
        harness.Step(0.1f);

        var all = new Point[harness.Sim.RenderCount(true)];
        harness.Sim.WritePoints(all, true);
        harness.Sim.PublishBodyRanges(all, new BufferWithViews());

        Assert.Equal(7, all.Length);
        Assert.Same(all, beads.PublishedPoints);
        Assert.Equal((0, 1), (ground.PublishedStart, ground.PublishedCount));
        Assert.Equal((1, 5), (beads.PublishedStart, beads.PublishedCount));
        Assert.Equal(0, disabled.PublishedCount);
        Assert.Equal((6, 1), (single.PublishedStart, single.PublishedCount));
        Assert.Equal(8f, all[beads.PublishedStart + 4].Position.X, 0.001f);
        Assert.Equal(9f, all[single.PublishedStart].Position.Y, 0.001f);

        // A description that shrinks must not keep pointing past its bodies.
        beads.InstanceCount = 2;
        harness.Step(FrameSec);
        all = new Point[harness.Sim.RenderCount(true)];
        harness.Sim.WritePoints(all, true);
        harness.Sim.PublishBodyRanges(all, new BufferWithViews());
        Assert.Equal((1, 2), (beads.PublishedStart, beads.PublishedCount));
        Assert.Equal((3, 1), (single.PublishedStart, single.PublishedCount));
    }

    [Fact]
    public void HingeMotorSpinsTheBodyAtItsSpeed()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var axle = harness.Add(Box(Vector3.Zero, new Vector3(0.2f)));
        axle.Kind = PhysicsBodyKind.Kinematic;
        var wheel = harness.Add(Box(Vector3.Zero, new Vector3(2f, 0.2f, 0.2f)));
        axle.CollisionGroup = 1;
        wheel.CollisionGroup = 1;
        var joint = new PhysicsJoint { Id = Guid.NewGuid(), BodyA = axle, BodyB = wheel, Type = PhysicsJointType.Hinge, HingeAxisA = Vector3.UnitZ, HingeAxisB = Vector3.UnitZ, MotorSpeed = 2f };
        harness.Joints.Add(joint);
        harness.Step(1f);

        Assert.Equal(2, harness.Sim.ConstraintCount);

        // Two radians in a second around Z: the bar's long axis, +X, has turned by about that much.
        var turned = Vector3.Transform(Vector3.UnitX, harness.ReadPoints()[1].Orientation);
        var angle = MathF.Abs(MathF.Atan2(turned.Y, turned.X));
        Assert.InRange(angle, 1.7f, 2.2f);
        Assert.Equal(0f, turned.Z, 0.05f);

        // Turning the motor off removes its constraint and leaves the hinge.
        joint.MotorSpeed = 0f;
        harness.Step(FrameSec);
        Assert.Equal(1, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void AutoAnchorPutsOneJointAtEveryInstance()
    {
        using var harness = new Harness();
        var hub = harness.Add(Box(new Vector3(0, 5, 0), new Vector3(0.4f)));
        hub.Kind = PhysicsBodyKind.Kinematic;
        hub.CollisionGroup = 1;
        var bobs = Sphere(Vector3.Zero, 0.2f);
        bobs.CollisionGroup = 1;
        Vector3[] starts = [new(-2, 5, 0), new(2, 5, 0), new(0, 5, 3)];
        SetInstances(bobs, 3, index => starts[index]);
        harness.Bodies.Add(bobs);

        // A ball socket at each bob's own centre welds its position to the hub without moving it.
        harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = hub, BodyB = bobs, AutoAnchor = true });
        harness.Step(2f);

        Assert.Equal(3, harness.Sim.ConstraintCount);
        var points = harness.ReadPoints();
        for (var index = 0; index < 3; index++)
            Assert.True(Vector3.Distance(points[index + 1].Position, starts[index]) < 0.05f, $"bob {index} should hang where it started, is at {points[index + 1].Position}");
    }

    [Fact]
    public void SwingLimitStopsALimbFromFoldingBack()
    {
        float FinalAngle(float limitDegrees)
        {
            using var harness = new Harness();
            var shoulder = harness.Add(Box(new Vector3(0, 5, 0), new Vector3(0.3f)));
            shoulder.Kind = PhysicsBodyKind.Kinematic;
            shoulder.CollisionGroup = 1;

            // An arm sticking straight up from the shoulder, which gravity wants to swing down by 180 degrees.
            var arm = harness.Add(Box(new Vector3(0.05f, 6, 0), new Vector3(0.2f, 1.6f, 0.2f)));
            arm.CollisionGroup = 1;
            arm.AngularDamping = 4f;
            arm.LinearDamping = 1f;
            harness.Joints.Add(new PhysicsJoint
                                   {
                                       Id = Guid.NewGuid(),
                                       BodyA = shoulder,
                                       BodyB = arm,
                                       LocalOffsetA = Vector3.Zero,
                                       LocalOffsetB = new Vector3(-0.05f, -1f, 0),
                                       HingeAxisA = Vector3.UnitY,
                                       HingeAxisB = Vector3.UnitY,
                                       SwingLimit = limitDegrees,
                                   });
            harness.Step(10f);

            var armUp = Vector3.Transform(Vector3.UnitY, harness.ReadPoints()[1].Orientation);
            return MathF.Acos(Math.Clamp(armUp.Y, -1f, 1f)) * 180f / MathF.PI;
        }

        Assert.True(FinalAngle(0f) > 150f, "without a limit the arm ends up hanging down");
        Assert.InRange(FinalAngle(40f), 25f, 48f);
    }

    [Fact]
    public void WorldWeldAlsoHoldsTheOrientation()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var box = harness.Add(Box(Vector3.Zero, Vector3.One));
        box.AngularVelocity = new Vector3(0, 5, 0);
        harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = box, Mode = PhysicsJointMode.World, Type = PhysicsJointType.Weld });
        harness.Step(2f);

        Assert.Equal(2, harness.Sim.ConstraintCount);
        var orientation = harness.ReadPoints()[0].Orientation;
        Assert.True(MathF.Abs(orientation.W) > 0.99f, "the spin must be stopped and the body turned back");

        // Removing the joint removes both constraints.
        harness.Joints.Clear();
        harness.Step(FrameSec);
        Assert.Equal(0, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void NearbyModeLinksOnlyInstancesWithinTheDistance()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var beads = Sphere(Vector3.Zero, 0.2f);

        // Two tight pairs far from each other, and one loner.
        Vector3[] positions = [new(0, 0, 0), new(1, 0, 0), new(50, 0, 0), new(50, 1, 0), new(-80, 0, 0)];
        SetInstances(beads, positions.Length, index => positions[index]);
        harness.Bodies.Add(beads);
        var joint = new PhysicsJoint { Id = Guid.NewGuid(), BodyA = beads, Mode = PhysicsJointMode.Nearby, LinkDistance = 1.5f };
        harness.Joints.Add(joint);
        harness.Step(0.2f);
        Assert.Equal(2, harness.Sim.ConstraintCount);

        // Unchanged inputs must not rebuild or duplicate anything.
        harness.Step(0.5f);
        Assert.Equal(2, harness.Sim.ConstraintCount);

        joint.LinkDistance = 0.5f;
        harness.Step(FrameSec);
        Assert.Equal(0, harness.Sim.ConstraintCount);

        joint.LinkDistance = float.NaN;
        harness.Step(FrameSec);
        Assert.Equal(0, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void NearbyModeLinksAGridIntoANet()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var beads = Sphere(Vector3.Zero, 0.2f);
        SetInstances(beads, 16, index => new Vector3(index % 4, index / 4, 0));
        harness.Bodies.Add(beads);
        harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = beads, Mode = PhysicsJointMode.Nearby, LinkDistance = 1.1f });
        harness.Step(0.2f);

        // A 4x4 grid has 3*4 horizontal and 3*4 vertical neighbours; diagonals are 1.41 apart.
        Assert.Equal(24, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void PairModeLinksInstanceToInstanceAndOneToMany()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var anchors = Sphere(Vector3.Zero, 0.2f);
        anchors.Kind = PhysicsBodyKind.Kinematic;
        SetInstances(anchors, 3, index => new Vector3(index * 4f, 5, 0));
        var bobs = Sphere(Vector3.Zero, 0.2f);
        SetInstances(bobs, 3, index => new Vector3(index * 4f, 3, 0));
        harness.Bodies.Add(anchors);
        harness.Bodies.Add(bobs);
        var joint = new PhysicsJoint { Id = Guid.NewGuid(), BodyA = anchors, BodyB = bobs, Type = PhysicsJointType.DistanceServo, TargetDistance = 2f };
        harness.Joints.Add(joint);
        harness.Step(0.2f);
        Assert.Equal(3, harness.Sim.ConstraintCount);

        // One single anchor for all three.
        var hub = harness.Add(Sphere(new Vector3(4, 8, 0), 0.2f));
        hub.Kind = PhysicsBodyKind.Kinematic;
        joint.BodyA = hub;
        harness.Step(0.2f);
        Assert.Equal(3, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void RemovingInstancesDropsTheirJointsWithoutLeaking()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var beads = Sphere(Vector3.Zero, 0.2f);
        SetInstances(beads, 6, index => new Vector3(index, 0, 0));
        harness.Bodies.Add(beads);
        harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = beads, Mode = PhysicsJointMode.Chain });
        harness.Step(0.2f);
        Assert.Equal(5, harness.Sim.ConstraintCount);

        beads.InstanceCount = 3;
        harness.Step(0.2f);
        Assert.Equal(2, harness.Sim.ConstraintCount);

        beads.InstanceCount = 6;
        harness.Step(0.2f);
        Assert.Equal(5, harness.Sim.ConstraintCount);

        harness.Bodies.Remove(beads);
        harness.Step(0.2f);
        Assert.Equal(0, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void ManyInstancesSettleIntoAPile()
    {
        using var harness = new Harness();
        harness.Sim.StepBudgetSec = 10f;
        harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(400, 1, 400), PhysicsBodyKind.Static));
        var boxes = Box(Vector3.Zero, new Vector3(0.5f));
        SetInstances(boxes, 500, index => new Vector3(index % 10 - 5, 1 + index / 100 * 1.5f, index / 10 % 10 - 5));
        harness.Bodies.Add(boxes);
        harness.Step(4f);

        Assert.Equal(500, harness.BodyCount);
        var points = harness.ReadPoints();
        foreach (var point in points)
        {
            Assert.True(float.IsFinite(point.Position.Y));
            Assert.True(point.Position.Y > -0.1f, "nothing may fall through the ground");
        }
    }

    [Fact]
    public void InstancedRunsReplayBitIdentically()
    {
        Vector3[] Run()
        {
            using var harness = new Harness();
            harness.Settings.Multithreaded = false;
            harness.Settings.Deterministic = true;
            harness.Sim.StepBudgetSec = 10f;
            harness.Bodies.Add(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
            var pieces = Hull(CubePoints(Vector3.Zero, 0.6f), Vector3.Zero);
            SetInstances(pieces, 40, index => new Vector3(index % 4 * 0.5f, 1 + index / 4, index % 3 * 0.4f));
            harness.Bodies.Add(pieces);
            harness.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = pieces, Mode = PhysicsJointMode.Nearby, LinkDistance = 0.6f });
            harness.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Vortex, Strength = 1f });
            harness.Step(2f);

            var points = harness.ReadPoints();
            var positions = new Vector3[points.Length];
            for (var index = 0; index < points.Length; index++)
                positions[index] = points[index].Position;

            return positions;
        }

        Assert.Equal(Run(), Run());
    }

    private static void SetInstances(PhysicsBody body, int count, Func<int, Vector3> position)
    {
        body.Instances = new PhysicsBodyInstance[count];
        body.InstanceCount = count;
        for (var index = 0; index < count; index++)
        {
            body.Instances[index] = new PhysicsBodyInstance
                                        {
                                            Position = position(index),
                                            Orientation = Quaternion.Identity,
                                            Scale = Vector3.One,
                                            Color = Vector4.One,
                                            HullIndex = -1,
                                        };
        }
    }

    private static PhysicsBody Box(Vector3 position, Vector3 size, PhysicsBodyKind kind = PhysicsBodyKind.Dynamic)
    {
        return new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Box, Kind = kind, Position = position, Size = size };
    }

    private static PhysicsBody Sphere(Vector3 position, float radius = 0.5f, Vector3 velocity = default)
    {
        return new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Sphere, Position = position, Radius = radius, LinearVelocity = velocity };
    }

    private static PhysicsBody Hull(Vector3[] points, Vector3 pivot)
    {
        return new PhysicsBody
                   {
                       Id = Guid.NewGuid(),
                       Shape = PhysicsShapeType.ConvexHull,
                       Hulls = [new PhysicsHull { Points = points, Pivot = pivot }],
                   };
    }

    /// <summary>The eight corners of a cube with the given edge length around a centre.</summary>
    private static Vector3[] CubePoints(Vector3 center, float size)
    {
        var half = size * 0.5f;
        var points = new Vector3[8];
        for (var index = 0; index < 8; index++)
        {
            points[index] = center + new Vector3((index & 1) == 0 ? -half : half,
                                                 (index & 2) == 0 ? -half : half,
                                                 (index & 4) == 0 ? -half : half);
        }

        return points;
    }

    /// <summary>A square in the XZ plane at height 0, as two triangles.</summary>
    private static Vector3[] QuadTriangles(float halfSize, bool flipped)
    {
        var a = new Vector3(-halfSize, 0, -halfSize);
        var b = new Vector3(halfSize, 0, -halfSize);
        var c = new Vector3(halfSize, 0, halfSize);
        var d = new Vector3(-halfSize, 0, halfSize);
        return flipped ? [a, b, c, a, c, d] : [a, c, b, a, d, c];
    }

    private static Vector3[] CubeTriangles(float size)
    {
        var p = CubePoints(Vector3.Zero, size);
        int[] indices =
            [
                0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1,
                2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3,
            ];
        var triangles = new Vector3[indices.Length];
        for (var index = 0; index < indices.Length; index++)
            triangles[index] = p[indices[index]];

        return triangles;
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

        public readonly List<PhysicsBody> Bodies = new();
        public readonly List<PhysicsJoint> Joints = new();
        public readonly List<PhysicsForce> Forces = new();

        /// <summary>Every contact reported since the harness was created.</summary>
        public readonly List<PhysicsContact> AllContacts = new();

        public bool IncludeStatics { get; set; }

        public int BodyCount => Sim.RenderCount(IncludeStatics);

        public PhysicsBody Add(PhysicsBody body)
        {
            Bodies.Add(body);
            return body;
        }

        public Point[] ReadPoints()
        {
            var points = new Point[Sim.RenderCount(IncludeStatics)];
            Sim.WritePoints(points, IncludeStatics);
            return points;
        }

        public void Step(float seconds, float frameSeconds = FrameSec)
        {
            var frames = Math.Max(1, (int)Math.Round(seconds / frameSeconds));
            for (var frame = 0; frame < frames; frame++)
            {
                _time += frameSeconds;
                Tick();
            }
        }

        /// <summary>Updates again without advancing the clock.</summary>
        public void TickAgain() => Sim.Update(Settings, _time, Bodies, Joints, Forces);

        public void Dispose() => Sim.Dispose();

        private void Tick()
        {
            Sim.Update(Settings, _time, Bodies, Joints, Forces);
            AllContacts.AddRange(Sim.Contacts);
        }

        private double _time;
    }
}
