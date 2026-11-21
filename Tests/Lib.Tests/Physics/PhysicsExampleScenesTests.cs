using System.Numerics;
using Lib.physics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.DataTypes.Volume.Tools;
using Xunit;

namespace Lib.Tests.Physics;

/// <summary>
/// The physics of the example graphs under Operators/Examples/Symbols/Lib/physics, rebuilt with the same numbers, plus
/// set-ups that earlier examples showed and that still guard against regressions. The graphs themselves need an editor to run; these keep their set-ups from regressing into a rope that
/// explodes, a wall that falls over by itself or bodies that leave through the floor.
/// </summary>
public class PhysicsExampleScenesTests
{
    private const float FrameSec = 1f / 60f;

    [Fact]
    public void BrickWallStandsUntilTheProjectileArrives()
    {
        using var scene = new Scene();
        scene.AddGround();
        var bricks = scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Box, Size = new Vector3(1f, 0.5f, 0.5f), Friction = 0.7f },
                                        Grid(10, 8, 1, new Vector3(1.02f, 0.5f, 1f), new Vector3(0, 2f, 0)));

        scene.Run(2f);
        var top = scene.Points()[79].Position;
        Assert.True(top.Y > 3.6f, $"the wall must stand on its own, top brick is at {top}");

        scene.Bodies.Add(new PhysicsBody
                             {
                                 Id = Guid.NewGuid(),
                                 Shape = PhysicsShapeType.Box,
                                 Size = new Vector3(2f),
                                 Mass = 60f,
                                 Position = new Vector3(0, 2f, 9f),
                                 LinearVelocity = new Vector3(0, 1.5f, -16f),
                             });
        scene.Run(3f);

        var moved = 0;
        var points = scene.Points();
        for (var index = 0; index < bricks.InstanceCount; index++)
        {
            Assert.True(points[index].Position.Y > -0.2f, "no brick may fall through the ground");
            if (MathF.Abs(points[index].Position.Z) > 0.5f)
                moved++;
        }

        Assert.True(moved > 15, $"the projectile should knock bricks out of the wall, moved {moved}");
    }

    [Fact]
    public void RopeHangsFromItsFirstLinkWithoutStretching()
    {
        using var scene = new Scene(substeps: 4, iterations: 8);
        var links = scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.13f, CollisionGroup = 1, LinearDamping = 0.1f },
                                       Line(24, new Vector3(-6f, 7f, 0), Vector3.UnitX, 6f));
        scene.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = links, Mode = PhysicsJointMode.Chain, Frequency = 60f });
        scene.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = links, Mode = PhysicsJointMode.World, InstanceIndex = 0, Frequency = 60f });
        scene.Run(6f);

        var points = scene.Points();
        Assert.True(Vector3.Distance(points[0].Position, new Vector3(-9f, 7f, 0)) < 0.1f, "the first link is pinned");
        var length = 0f;
        for (var index = 1; index < points.Length; index++)
        {
            Assert.True(float.IsFinite(points[index].Position.X));
            length += Vector3.Distance(points[index - 1].Position, points[index].Position);
        }

        Assert.InRange(length, 5.8f, 6.6f);
        Assert.True(points[^1].Position.Y < 2f, $"the free end swings down, is at {points[^1].Position}");
    }

    [Fact]
    public void NetDrapesOverTheSphereAndHoldsTogether()
    {
        using var scene = new Scene(substeps: 4, iterations: 8);
        scene.AddGround();
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Sphere, Radius = 1.6f, Position = new Vector3(0, 1.6f, 0) });
        var knots = scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.1f, Mass = 0.2f, CollisionGroup = 1, LinearDamping = 0.3f },
                                       Grid(14, 1, 14, new Vector3(0.32f, 1f, 0.32f), new Vector3(0, 4.5f, 0)));
        scene.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = knots, Mode = PhysicsJointMode.Nearby, LinkDistance = 0.36f, Frequency = 40f });
        scene.Run(6f);

        // 14 x 14 knots: 13 links per row and column.
        var points = scene.Points();
        var centre = points[6 * 14 + 6].Position;
        var corner = points[0].Position;
        Assert.InRange(centre.Y, 3.1f, 3.6f);
        Assert.True(corner.Y < centre.Y - 1f, $"the corners hang below the top of the sphere: centre {centre}, corner {corner}");
        Assert.InRange(Vector3.Distance(points[0].Position, points[1].Position), 0.24f, 0.42f);
        foreach (var point in points)
            Assert.True(point.Position.Y > -0.1f && float.IsFinite(point.Position.X));
    }

    [Fact]
    public void PlanetGathersItsMoons()
    {
        using var scene = new Scene(gravity: Vector3.Zero);
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Sphere, Radius = 2f });
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.2f, LinearVelocity = new Vector3(1.5f, 0, 0), LinearDamping = 0.05f },
                           Scatter(200, Vector3.Zero, new Vector3(14f), 7));
        scene.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Radial, Strength = 12f });
        scene.Run(12f);

        var near = 0;
        foreach (var point in scene.Points())
        {
            Assert.True(float.IsFinite(point.Position.X));
            if (point.Position.Length() < 4.5f)
                near++;
        }

        Assert.True(near > 150, $"most moons should have settled on the planet, {near} of 200 did");
    }

    [Fact]
    public void VortexLiftsAndSpinsTheBoxes()
    {
        using var scene = new Scene();
        scene.AddGround();
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Box, Size = new Vector3(0.3f), LinearDamping = 0.2f },
                           Scatter(300, new Vector3(0, 1f, 0), new Vector3(10f, 1.5f, 10f), 3));
        scene.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Vortex, Direction = Vector3.UnitY, Strength = 10f, Radius = 9f, Falloff = 0.5f });
        scene.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Radial, Strength = 5f, Radius = 12f, Falloff = 0f });
        scene.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Directional, Direction = Vector3.UnitY, Strength = 6f });
        scene.Run(6f);

        var fast = 0;
        foreach (var point in scene.Points())
        {
            Assert.True(float.IsFinite(point.Position.X) && point.Position.Y > -0.2f);
            Assert.True(point.Position.Length() < 60f, "the pull has to keep the boxes in the scene");
            if (point.F2 > 1f)
                fast++;
        }

        Assert.True(fast > 100, $"the vortex should keep the boxes moving, {fast} of 300 are");
    }

    [Fact]
    public void RainOfBallsReportsContacts()
    {
        using var scene = new Scene { ContactSpeed = 2f };
        scene.AddGround();
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.25f, Bounciness = 0.6f },
                           Scatter(60, new Vector3(0, 8f, 0), new Vector3(8f, 10f, 8f), 11));
        scene.Run(5f);

        Assert.True(scene.ContactTotal > 60, $"every ball lands at least once, saw {scene.ContactTotal} contacts");
        Assert.True(scene.ContactPeak <= 64, $"the default buffer of 64 should hold a frame's contacts, peak was {scene.ContactPeak}");
    }

    [Fact]
    public void FracturedBlockFallsAndComesApart()
    {
        using var scene = new Scene();
        scene.AddGround();

        // Stand-in for VoronoiFracture: a 2 m cube cut into 27 touching pieces, each a hull around its own pivot.
        var hulls = new List<PhysicsHull>();
        for (var index = 0; index < 27; index++)
        {
            var pivot = new Vector3(index % 3 - 1, index / 3 % 3 - 1, index / 9 - 1) * (2f / 3f);
            var corners = new Vector3[8];
            for (var corner = 0; corner < 8; corner++)
                corners[corner] = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1) / 3f;

            hulls.Add(new PhysicsHull { Points = corners, Pivot = pivot });
        }

        var pieces = new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.ConvexHull, Hulls = hulls.ToArray(), Position = new Vector3(0, 5f, 0), Friction = 0.6f };
        pieces.Orientation = Quaternion.CreateFromYawPitchRoll(0.4f, 0.5f, 0.2f);
        pieces.Instances = new PhysicsBodyInstance[27];
        pieces.InstanceCount = 27;
        for (var index = 0; index < 27; index++)
            pieces.Instances[index] = new PhysicsBodyInstance { Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, HullIndex = index };

        scene.Bodies.Add(pieces);
        scene.Run(FrameSec);
        var start = scene.Points();
        scene.Run(5f);
        var end = scene.Points();

        var spread = 0f;
        for (var index = 0; index < 27; index++)
        {
            Assert.True(end[index].Position.Y > -0.1f && end[index].Position.Y < 2.5f, $"piece {index} should lie on the ground, is at {end[index].Position}");
            spread = MathF.Max(spread, new Vector2(end[index].Position.X, end[index].Position.Z).Length());
        }

        Assert.True(spread > 1.6f, $"the block should break apart on impact, widest piece is {spread} from the centre");
        Assert.True(Vector3.Distance(start[13].Position, new Vector3(0, 5f, 0)) < 0.01f, "the middle piece starts at the block's position");
    }

    [Fact]
    public void BallsCollectOnAMeshColliderMadeFromAVolumeLikeSurface()
    {
        using var scene = new Scene();
        scene.AddGround();

        // Stand-in for VolumeToMesh: a shallow bowl as a triangle grid, static.
        const int resolution = 16;
        var triangles = new List<Vector3>();
        Vector3 Bowl(int x, int z)
        {
            var px = (x / (float)resolution - 0.5f) * 8f;
            var pz = (z / (float)resolution - 0.5f) * 8f;
            return new Vector3(px, 1f + (px * px + pz * pz) * 0.12f, pz);
        }

        for (var x = 0; x < resolution; x++)
        for (var z = 0; z < resolution; z++)
        {
            triangles.AddRange([Bowl(x, z), Bowl(x + 1, z), Bowl(x + 1, z + 1), Bowl(x, z), Bowl(x + 1, z + 1), Bowl(x, z + 1)]);
        }

        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = triangles.ToArray() });
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.18f },
                           Scatter(150, new Vector3(0, 7f, 0), new Vector3(3f, 6f, 3f), 5));
        scene.Run(6f);

        var caught = 0;
        foreach (var point in scene.Points())
        {
            if (point.Position.Y > 0.9f && new Vector2(point.Position.X, point.Position.Z).Length() < 4.5f)
                caught++;
        }

        Assert.True(caught > 140, $"the bowl should hold the balls, it holds {caught} of 150");
    }

    [Fact]
    public void CarDrivesOnItsFourMotorisedWheels()
    {
        using var scene = new Scene(substeps: 4, iterations: 8);
        scene.AddGround();
        var chassis = new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Box, Size = new Vector3(2f, 0.5f, 4f), Mass = 10f, Position = new Vector3(0, 1f, 0), CollisionGroup = 1 };
        scene.Bodies.Add(chassis);

        // GridPoints 2 x 1 x 2 with every point turned 90 degrees around Z: the wheel's axis, local Y, lies along world X.
        var wheels = scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Cylinder, Radius = 0.5f, Length = 0.4f, Mass = 2f, Friction = 1.5f, CollisionGroup = 1 },
                                        Grid(2, 1, 2, new Vector3(2.6f, 1f, 2.8f), new Vector3(0, 0.6f, 0)));
        for (var index = 0; index < wheels.InstanceCount; index++)
            wheels.Instances[index].Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

        // One joint for all four: anchored at each wheel, turning around the wheel's own axis.
        scene.Joints.Add(new PhysicsJoint
                             {
                                 Id = Guid.NewGuid(),
                                 BodyA = chassis,
                                 BodyB = wheels,
                                 Type = PhysicsJointType.Hinge,
                                 AutoAnchor = true,
                                 HingeAxisB = Vector3.UnitY,
                                 Frequency = 60f,
                                 MotorSpeed = 8f,
                             });

        scene.Run(4f);
        var body = scene.Points()[0];
        var up = Vector3.Transform(Vector3.UnitY, body.Orientation);
        Assert.True(body.Position.Z > 6f, $"the car should have driven off along +Z, is at {body.Position}");
        Assert.True(MathF.Abs(body.Position.X) < 2.5f, $"and roughly straight, is at {body.Position}");
        Assert.InRange(body.Position.Y, 0.7f, 1.3f);
        Assert.True(up.Y > 0.95f, "the chassis stays level");
    }

    [Fact]
    public void MotorisedPaddleSweepsTheBalls()
    {
        using var scene = new Scene();
        scene.AddGround();
        // The paddle reaches almost to the floor, so a ball cannot be wedged underneath it.
        var hub = new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Kinematic, Shape = PhysicsShapeType.Cylinder, Radius = 0.3f, Length = 0.9f, Position = new Vector3(0, 0.55f, 0), CollisionGroup = 1 };
        var paddle = new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Box, Size = new Vector3(9f, 0.9f, 0.4f), Mass = 30f, Position = new Vector3(0, 0.55f, 0), CollisionGroup = 1 };
        scene.Bodies.Add(hub);
        scene.Bodies.Add(paddle);
        scene.Joints.Add(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = hub, BodyB = paddle, Type = PhysicsJointType.Hinge, Frequency = 60f, MotorSpeed = 2f });
        // Damping stands in for rolling resistance; without it the balls the paddle flings out roll off the floor.
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.4f, LinearDamping = 0.8f },
                           Scatter(80, new Vector3(0, 1.5f, 0), new Vector3(9f, 1f, 9f), 9));
        scene.Run(6f);

        var points = scene.Points();
        Assert.True(Vector3.Distance(points[0].Position, new Vector3(0, 0.55f, 0)) < 0.01f, "the hub is kinematic and stays put");
        Assert.True(Vector3.Distance(points[1].Position, new Vector3(0, 0.55f, 0)) < 0.15f, $"the paddle turns on the spot, is at {points[1].Position}");
        Assert.True(MathF.Abs(Vector3.Transform(Vector3.UnitY, points[1].Orientation).Y) > 0.98f, "and stays flat");

        var swept = 0;
        for (var index = 2; index < points.Length; index++)
        {
            Assert.True(points[index].Position.Y > -0.1f && float.IsFinite(points[index].Position.X), $"ball {index} left the scene: {points[index].Position}");
            if (new Vector2(points[index].Position.X, points[index].Position.Z).Length() > 4.6f)
                swept++;
        }

        Assert.True(swept > 40, $"the paddle should push the balls out of its circle, {swept} of 80 are outside");
    }

    /// <summary>One ragdoll part: its place relative to the figure's feet, and its collider.</summary>
    private readonly record struct Part(string Name, PhysicsShapeType Shape, Vector3 Offset, Vector3 Size, float Radius, float Length, float Mass);

    private static readonly Part[] RagdollParts =
        [
            new("Torso", PhysicsShapeType.Box, new Vector3(0, 1.45f, 0), new Vector3(0.5f, 0.7f, 0.28f), 0, 0, 6f),
            new("Head", PhysicsShapeType.Sphere, new Vector3(0, 2.0f, 0), default, 0.16f, 0, 1f),
            new("UpperArmL", PhysicsShapeType.Capsule, new Vector3(-0.34f, 1.55f, 0), default, 0.07f, 0.3f, 1f),
            new("UpperArmR", PhysicsShapeType.Capsule, new Vector3(0.34f, 1.55f, 0), default, 0.07f, 0.3f, 1f),
            new("LowerArmL", PhysicsShapeType.Capsule, new Vector3(-0.34f, 1.15f, 0), default, 0.06f, 0.3f, 0.8f),
            new("LowerArmR", PhysicsShapeType.Capsule, new Vector3(0.34f, 1.15f, 0), default, 0.06f, 0.3f, 0.8f),
            new("UpperLegL", PhysicsShapeType.Capsule, new Vector3(-0.15f, 0.8f, 0), default, 0.1f, 0.4f, 2f),
            new("UpperLegR", PhysicsShapeType.Capsule, new Vector3(0.15f, 0.8f, 0), default, 0.1f, 0.4f, 2f),
            new("LowerLegL", PhysicsShapeType.Capsule, new Vector3(-0.15f, 0.3f, 0), default, 0.09f, 0.4f, 1.5f),
            new("LowerLegR", PhysicsShapeType.Capsule, new Vector3(0.15f, 0.3f, 0), default, 0.09f, 0.4f, 1.5f),
        ];

    /// <summary>Ball sockets by part index: A, its anchor, B, its anchor, and the swing limit in degrees.</summary>
    private static readonly (int A, Vector3 AnchorA, int B, Vector3 AnchorB, float Limit)[] RagdollJoints =
        [
            (0, new Vector3(0, 0.37f, 0), 1, new Vector3(0, -0.18f, 0), 40f),
            (0, new Vector3(-0.34f, 0.3f, 0), 2, new Vector3(0, 0.2f, 0), 120f),
            (0, new Vector3(0.34f, 0.3f, 0), 3, new Vector3(0, 0.2f, 0), 120f),
            (2, new Vector3(0, -0.2f, 0), 4, new Vector3(0, 0.2f, 0), 100f),
            (3, new Vector3(0, -0.2f, 0), 5, new Vector3(0, 0.2f, 0), 100f),
            (0, new Vector3(-0.15f, -0.4f, 0), 6, new Vector3(0, 0.25f, 0), 70f),
            (0, new Vector3(0.15f, -0.4f, 0), 7, new Vector3(0, 0.25f, 0), 70f),
            (6, new Vector3(0, -0.25f, 0), 8, new Vector3(0, 0.25f, 0), 80f),
            (7, new Vector3(0, -0.25f, 0), 9, new Vector3(0, 0.25f, 0), 80f),
        ];

    [Fact]
    public void RagdollsFallInAHeapAndStayInOnePiece()
    {
        using var scene = new Scene(substeps: 4, iterations: 8);
        scene.AddGround();
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Sphere, Radius = 1.5f, Position = new Vector3(0, 1.5f, 0) });

        // Every part is one description with a body per ragdoll; the joints then pair them up ragdoll by ragdoll.
        var spawn = Scatter(6, new Vector3(0, 6f, 0), new Vector3(5f, 4f, 1.5f), 21);
        var parts = new PhysicsBody[RagdollParts.Length];
        for (var index = 0; index < RagdollParts.Length; index++)
        {
            var part = RagdollParts[index];
            parts[index] = scene.AddInstanced(new PhysicsBody
                                                  {
                                                      Shape = part.Shape,
                                                      Position = part.Offset,
                                                      Size = part.Size,
                                                      Radius = part.Radius,
                                                      Length = part.Length,
                                                      Mass = part.Mass,
                                                      CollisionGroup = 1,
                                                      AngularDamping = 0.5f,
                                                  }, spawn);
        }

        foreach (var joint in RagdollJoints)
        {
            scene.Joints.Add(new PhysicsJoint
                                 {
                                     Id = Guid.NewGuid(),
                                     BodyA = parts[joint.A],
                                     BodyB = parts[joint.B],
                                     LocalOffsetA = joint.AnchorA,
                                     LocalOffsetB = joint.AnchorB,
                                     SwingLimit = joint.Limit,
                                     Frequency = 40f,
                                 });
        }

        scene.Run(FrameSec);
        var start = scene.Points();
        scene.Run(6f);
        var end = scene.Points();

        // Points are grouped by description: part p of ragdoll r is at p * 6 + r.
        Assert.Equal(60, end.Length);
        Assert.True(Vector3.Distance(start[0].Position, spawn[0] + RagdollParts[0].Offset) < 0.05f, "a part starts at its spawn point plus its offset");
        for (var ragdoll = 0; ragdoll < 6; ragdoll++)
        {
            var torso = end[ragdoll].Position;
            Assert.True(torso.Y < 4f, $"ragdoll {ragdoll} should have fallen, torso at {torso}");
            for (var part = 0; part < RagdollParts.Length; part++)
            {
                var position = end[part * 6 + ragdoll].Position;
                Assert.True(float.IsFinite(position.X) && position.Y > -0.1f, $"part {RagdollParts[part].Name} of ragdoll {ragdoll} is at {position}");
                Assert.True(Vector3.Distance(position, torso) < 1.6f, $"{RagdollParts[part].Name} of ragdoll {ragdoll} came off: {Vector3.Distance(position, torso)} from the torso");
            }
        }
    }

    [Fact]
    public void BallsLandOnTheBunnyLoadedFromAVdbFile()
    {
        var path = FindExampleAsset("volumes/bunny.vdb");
        if (path == null)
            return; // The asset ships with the Examples package; a checkout without it has nothing to test here.

        // LoadVdb -> VolumeResample -> VolumeToMesh -> static RigidBody, the route PhysicsVolumesExample takes.
        var bunny = VolumeDense.Resample(VdbFile.Load(path).GetFloatGrid(), 0.5f);
        var triangles = PhysicsGeometry.BuildTriangles(VolumeToMesh.Extract(bunny, 0f));
        Assert.InRange(triangles.Length / 3, 10_000, 120_000);

        using var scene = new Scene();
        scene.AddGround(new Vector3(400f, 0.5f, 400f), -0.75f);
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = triangles, Friction = 0.5f });
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.6f },
                           Scatter(300, new Vector3(-3f, 42f, 0f), new Vector3(22f, 14f, 16f), 9));
        scene.Run(8f);

        // The bunny is all slopes, so most balls roll off it. What shows the collider works is that none ends up
        // inside the level set and that many were thrown clear of the column they fell in.
        var sampler = new VdbSampler(bunny);
        var inside = 0;
        var deflected = 0;
        foreach (var point in scene.Points())
        {
            var position = point.Position;
            Assert.True(float.IsFinite(position.X) && position.Y > -0.6f, $"a ball left through the floor: {position}");
            if (sampler.SampleWorld(position) < -0.4f)
                inside++;

            if (MathF.Abs(position.X + 3f) > 12f || MathF.Abs(position.Z) > 9f)
                deflected++;
        }

        Assert.True(inside == 0, $"{inside} balls ended up inside the bunny");
        Assert.True(deflected > 40, $"the bunny should throw balls out of their column, {deflected} of 300 were");
    }

    [Fact]
    public void BallsCollectInABowlCarvedOutOfAVolume()
    {
        // VolumePrimitive box minus VolumePrimitive sphere -> VolumeCsg -> VolumeToMesh, the route PhysicsVolumesExample takes.
        var block = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(8f, 3f, 8f), 0.12f, new Vector3(0, 1.5f, 0));
        var ball = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(7f, 7f, 7f), 0.12f, new Vector3(0, 4.6f, 0));
        var carved = VolumeCsg.Combine(block, ball, VolumeCsg.Operation.Difference);
        var triangles = PhysicsGeometry.BuildTriangles(VolumeToMesh.Extract(carved, 0f));
        Assert.True(triangles.Length > 3000);

        using var scene = new Scene();
        scene.AddGround();
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = triangles });
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.2f },
                           Scatter(200, new Vector3(0, 8f, 0), new Vector3(5f, 6f, 5f), 4));
        scene.Run(7f);

        var inBowl = 0;
        foreach (var point in scene.Points())
        {
            var fromCentre = new Vector2(point.Position.X, point.Position.Z).Length();
            if (fromCentre < 3.4f && point.Position.Y > 1f && point.Position.Y < 3.2f)
                inBowl++;
        }

        Assert.True(inBowl > 170, $"the carved bowl should hold the balls, it holds {inBowl} of 200");
    }

    [Fact]
    public void ABowlColliderBuiltInTheBackgroundCatchesTheBallsWithoutStallingAFrame()
    {
        // As in the editor: the mesh collider's tree is built on a worker while the world keeps stepping.
        var block = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(8f, 3f, 8f), 0.12f, new Vector3(0, 1.5f, 0));
        var ball = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(7f, 7f, 7f), 0.12f, new Vector3(0, 4.6f, 0));
        var triangles = PhysicsGeometry.BuildTriangles(VolumeToMesh.Extract(VolumeCsg.Combine(block, ball, VolumeCsg.Operation.Difference), 0f));

        // A first build compiles Bepu's tree builder; this scene measures the steady cost of a new collider.
        using (var warmUp = new Scene())
        {
            warmUp.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = triangles });
            warmUp.Run(FrameSec);
        }

        using var scene = new Scene { BuildMeshesInBackground = true };
        scene.AddGround();
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = triangles });
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.2f },
                           Scatter(200, new Vector3(0, 8f, 0), new Vector3(5f, 6f, 5f), 4));

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        scene.Run(FrameSec);
        var firstFrameMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // The test steps faster than real time; the first half second plays at the editor's pace, while the balls
        // are still falling towards the bowl, so the build has the time it would have there.
        for (var frame = 0; frame < 30; frame++)
        {
            Thread.Sleep(16);
            scene.Run(FrameSec);
        }

        scene.Run(7f);

        var inBowl = 0;
        foreach (var point in scene.Points())
        {
            var fromCentre = new Vector2(point.Position.X, point.Position.Z).Length();
            if (fromCentre < 3.4f && point.Position.Y > 1f && point.Position.Y < 3.2f)
                inBowl++;
        }

        Assert.True(inBowl > 170, $"the carved bowl should hold the balls once its collider is built, it holds {inBowl} of 200");
        FrameTimes.Record("Mesh collider (72k triangles) built in the background: first frame", firstFrameMs);
    }

    [Fact]
    public void DebrisIsDrawnOntoAVolumeByItsOwnGradient()
    {
        // One torus volume, used twice: meshed as the collider, and through
        // VolumeToField's vector output as a force. The negated gradient points at the surface from outside.
        var torus = VolumePrimitives.Create(VolumePrimitives.Shape.Torus, new Vector3(6f, 1.6f, 1f), 0.2f, Vector3.Zero, 24);
        var triangles = PhysicsGeometry.BuildTriangles(VolumeToMesh.Extract(torus, 0f));
        var sampler = new VdbVectorSampler(torus);
        var bounds = torus.ActiveWorldBBox();
        var pull = new VectorField((in FieldSample sample) =>
                                   {
                                       var p = sample.Position;
                                       return p.X < bounds.Min.X || p.X > bounds.Max.X || p.Y < bounds.Min.Y || p.Y > bounds.Max.Y || p.Z < bounds.Min.Z || p.Z > bounds.Max.Z
                                                  ? Vector3.Zero
                                                  : sampler.SampleWorld(p);
                                   });

        using var scene = new Scene(gravity: Vector3.Zero);
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = triangles });
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Box, Size = new Vector3(0.25f), LinearDamping = 0.6f },
                           Scatter(300, Vector3.Zero, new Vector3(11f, 11f, 7f), 6));
        scene.Forces.Add(new PhysicsForce { Id = Guid.NewGuid(), Type = PhysicsForceType.Field, Field = pull, Strength = -9f });
        scene.Run(10f);

        // On the surface means close to the ring of radius 3 in the XY plane, at about the tube radius of 0.8.
        var onSurface = 0;
        foreach (var point in scene.Points())
        {
            Assert.True(float.IsFinite(point.Position.X));
            var toRing = new Vector2(new Vector2(point.Position.X, point.Position.Y).Length() - 3f, point.Position.Z).Length();
            if (toRing < 1.5f)
                onSurface++;
        }

        Assert.True(onSurface > 200, $"most debris should settle on the torus, {onSurface} of 300 did");
    }

    [Fact]
    public void FallenBallsCanBeSkinnedWithAVolume()
    {
        // PhysicsWorld points -> VolumeFromPoints -> VolumeToMesh.
        using var scene = new Scene();
        scene.AddGround();
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.3f },
                           Scatter(40, new Vector3(0, 3f, 0), new Vector3(2f, 4f, 2f), 2));
        scene.Run(4f);

        var points = scene.Points();
        var positions = new Vector3[points.Length];
        for (var index = 0; index < points.Length; index++)
            positions[index] = points[index].Position;

        var density = PointsToVolume.Splat(positions, null, 0.2f, 0.5f, 1f, PointsToVolume.Falloff.Smoothstep, seed: 0);
        var skin = VolumeToMesh.Extract(density, 0.35f);

        Assert.True(skin.FaceCount > 300, $"the density of 40 balls should give a surface, got {skin.FaceCount} faces");
    }

    /// <summary>Walks up from the test binaries to the repository and returns an Examples asset, or null.</summary>
    private static string? FindExampleAsset(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Operators", "Examples", "Assets", relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>GridPoints with SizeMode 0: cells spaced by <paramref name="spacing"/> and centred on <paramref name="center"/>.</summary>
    private static Vector3[] Grid(int countX, int countY, int countZ, Vector3 spacing, Vector3 center)
    {
        var points = new Vector3[countX * countY * countZ];
        for (var index = 0; index < points.Length; index++)
        {
            var cell = new Vector3(index % countX, index / countX % countY, index / (countX * countY) % countZ);
            var half = new Vector3(countX - 1, countY - 1, countZ - 1) * 0.5f;
            points[index] = (cell - half) * spacing + center;
        }

        return points;
    }

    /// <summary>LinePoints with its default pivot of 0.5: evenly spaced and centred.</summary>
    private static Vector3[] Line(int count, Vector3 center, Vector3 direction, float length)
    {
        var points = new Vector3[count];
        for (var index = 0; index < count; index++)
            points[index] = center + direction * length * (index / (float)(count - 1) - 0.5f);

        return points;
    }

    [Fact]
    public void RingsSlideDownThePostsTheyLandOver()
    {
        // VolumePrimitive torus -> VolumeToMesh -> dynamic RigidBody at scattered points, as in RigidBodyExample.
        var torus = LayFlat(VolumeToMesh.Extract(VolumePrimitives.Create(VolumePrimitives.Shape.Torus, new Vector3(1.7f, 0.36f, 1f), 0.04f, Vector3.Zero, 3), 0f));
        var decomposed = PhysicsGeometry.BuildHulls(torus, merge: true, PhysicsGeometry.HullSplit.Concave, 8);
        Assert.NotNull(decomposed[0].ConvexParts);

        var (onPostsDecomposed, ringedDecomposed) = TossRings(decomposed);
        var (onPostsHull, ringedHull) = TossRings(PhysicsGeometry.BuildHulls(torus, merge: true));

        // A single hull fills the hole, so no ring can end up around a post; split into hulls, rings slide down the posts.
        Assert.Equal(0, ringedHull);
        Assert.True(onPostsDecomposed <= onPostsHull, $"{onPostsDecomposed} decomposed rings rest on top of a post");
        Assert.True(ringedDecomposed >= 3, $"only {ringedDecomposed} of 36 rings ended up around a post");
    }

    /// <summary>Turns the ring from the XY plane onto the floor, as TransformGeometry does in the example.</summary>
    private static MeshGeometry LayFlat(MeshGeometry geometry)
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        for (var index = 0; index < geometry.Positions.Length; index++)
            geometry.Positions[index] = Vector3.Transform(geometry.Positions[index], rotation);

        return geometry;
    }

    /// <summary>Returns how many rings came to rest on top of a post, and how many around one, below its top.</summary>
    private static (int OnPosts, int Ringed) TossRings(PhysicsHull[] ring)
    {
        var posts = Scatter(12, new Vector3(0f, 1.25f, 0f), new Vector3(5f, 0.01f, 5f), 11);
        using var scene = new Scene();
        scene.AddGround();
        var postBody = scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Cylinder, Kind = PhysicsBodyKind.Static, Radius = 0.12f, Length = 2.5f }, posts);
        var rings = scene.AddInstanced(new PhysicsBody
                                           {
                                               Shape = PhysicsShapeType.ConvexHull, Hulls = ring, Friction = 0.4f, MassFromDensity = true,
                                           },
                                       Scatter(36, new Vector3(0f, 9f, 0f), new Vector3(5f, 8f, 5f), 3));
        Assert.NotNull(postBody);
        scene.Run(8f);

        int onPosts = 0, ringed = 0;
        foreach (var point in scene.Points())
        {
            Assert.True(float.IsFinite(point.Position.X));
            foreach (var post in posts)
            {
                // Around the post: lying roughly flat with the post inside the hole, whose radius is 0.67 against the post's 0.12.
                var distance = new Vector2(point.Position.X - post.X, point.Position.Z - post.Z).Length();
                var isFlat = MathF.Abs(Vector3.Transform(Vector3.UnitY, point.Orientation).Y) > 0.7f;
                if (distance > 0.55f || !isFlat)
                    continue;

                if (point.Position.Y > 2.4f)
                    onPosts++;
                else
                    ringed++;
                break;
            }
        }

        Assert.Equal(36, rings.InstanceCount);
        return (onPosts, ringed);
    }

    [Fact]
    public void KinematicBlobShovesTheBodiesItMovesThrough()
    {
        // PhysicsSdfFieldExample: the moving SDF blob collides as a Kinematic sphere that follows the blob's motion.
        using var scene = new Scene();
        scene.AddGround();
        var boxes = scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Box, Size = new Vector3(0.5f), Friction = 0.5f },
                                       Grid(8, 1, 3, new Vector3(0.8f, 0.5f, 0.8f), new Vector3(0, 0.3f, 0)));
        var blob = new PhysicsBody
                       {
                           Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Kinematic, Shape = PhysicsShapeType.Sphere, Radius = 1.4f,
                           Position = new Vector3(-6f, 0.9f, 0),
                       };
        scene.Bodies.Add(blob);
        scene.Run(1f);

        // 2.6 m/s, the blob's fastest stretch in the example.
        for (var frame = 0; frame < 280; frame++)
        {
            blob.Position = new Vector3(-6f + frame * (2.6f * FrameSec), 0.9f, 0);
            scene.Run(FrameSec);
        }

        scene.Run(0.5f);

        var points = scene.Points();
        var moved = 0;
        for (var index = 0; index < boxes.InstanceCount; index++)
        {
            var position = points[index].Position;
            Assert.True(float.IsFinite(position.X) && position.Y > -0.3f, $"a box left through the floor: {position}");
            Assert.True(Vector3.Distance(position, blob.Position) > 1.4f, $"a box is inside the blob: {position}");
            if (MathF.Abs(position.Z) > 1.2f || position.X > 3.2f)
                moved++;
        }

        Assert.True(moved > 6, $"the blob should push boxes out of its way, moved {moved} of {boxes.InstanceCount}");

        // Moved by velocity, the blob still ends exactly where its animation puts it, and stops there.
        var blobPosition = points[boxes.InstanceCount].Position;
        Assert.True(Vector3.Distance(blobPosition, blob.Position) < 0.02f, $"the blob is at {blobPosition}, its input at {blob.Position}");
    }

    [Fact]
    public void BallTipsOverTheWholeDominoRow()
    {
        // The domino run of PhysicsWorldExample: 15 dominoes from a point row and a ball thrown at the first one.
        using var scene = new Scene();
        scene.AddGround();
        var dominoes = scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Box, Size = new Vector3(0.15f, 1f, 0.6f), Friction = 0.5f },
                                          Grid(15, 1, 1, new Vector3(0.6f, 1f, 1f), new Vector3(0, 0.5f, 1.5f)));
        scene.Bodies.Add(new PhysicsBody
                             {
                                 Id = Guid.NewGuid(), Shape = PhysicsShapeType.Sphere, Radius = 0.3f, Position = new Vector3(-6.5f, 1.5f, 1.5f),
                                 LinearVelocity = new Vector3(5f, 0.8f, 0f),
                             });
        scene.Run(1f);
        Assert.True(Vector3.Transform(Vector3.UnitY, scene.Points()[14].Orientation).Y > 0.95f, "the last domino stands until the wave arrives");
        scene.Run(5f);

        var points = scene.Points();
        var fallen = 0;
        for (var index = 0; index < dominoes.InstanceCount; index++)
        {
            if (Vector3.Transform(Vector3.UnitY, points[index].Orientation).Y < 0.8f)
                fallen++;
        }

        Assert.True(fallen >= 14, $"the ball should knock over the whole row, {fallen} of 15 fell");
    }

    [Fact]
    public void BunnyAndCarvedBowlCatchTheBalls()
    {
        var path = FindExampleAsset("volumes/bunny.vdb");
        if (path == null)
            return; // The asset ships with the Examples package; a checkout without it has nothing to test here.

        // PhysicsVolumesExample: the coarse bunny scaled by 0.12 and moved left, a bowl carved from a block on the right.
        var bunny = VolumeToMesh.Extract(VolumeDense.Resample(VdbFile.Load(path).GetFloatGrid(), 0.5f), 0f);
        var bunnyTriangles = PhysicsGeometry.BuildTriangles(bunny);
        for (var index = 0; index < bunnyTriangles.Length; index++)
            bunnyTriangles[index] = bunnyTriangles[index] * 0.12f + new Vector3(-4.5f, 0, 0);

        var block = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(6f, 2.2f, 6f), 0.12f, new Vector3(4.5f, 1.1f, 0));
        var carver = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(5.4f), 0.12f, new Vector3(4.5f, 3.6f, 0));
        var bowlTriangles = PhysicsGeometry.BuildTriangles(VolumeToMesh.Extract(VolumeCsg.Combine(block, carver, VolumeCsg.Operation.Difference), 0f));

        using var scene = new Scene();
        scene.AddGround();
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = bunnyTriangles });
        scene.Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Shape = PhysicsShapeType.Mesh, MeshTriangles = bowlTriangles });
        scene.AddInstanced(new PhysicsBody { Shape = PhysicsShapeType.Sphere, Radius = 0.2f }, Scatter(260, new Vector3(0, 9f, 0), new Vector3(14f, 6f, 5f), 9));
        scene.Run(6f);

        // The bunny is all slopes, so balls roll off it; what shows its collider works is that none ends up inside its body.
        int insideBunny = 0, inBowl = 0;
        foreach (var point in scene.Points())
        {
            Assert.True(float.IsFinite(point.Position.X) && point.Position.Y > -0.1f, $"a ball left through the floor: {point.Position}");
            if (point.Position.X > -5.6f && point.Position.X < -4.2f && MathF.Abs(point.Position.Z) < 0.6f && point.Position.Y < 1.4f)
                insideBunny++;
            if (new Vector2(point.Position.X - 4.5f, point.Position.Z).Length() < 2.7f && point.Position.Y > 0.5f)
                inBowl++;
        }

        Assert.True(inBowl > 40, $"the bowl should hold balls, it holds {inBowl}");
        Assert.True(insideBunny == 0, $"{insideBunny} balls ended up inside the bunny");
    }

    /// <summary>Random points in a box, like ScatterPointsInVolume without a geometry.</summary>
    private static Vector3[] Scatter(int count, Vector3 center, Vector3 size, int seed)
    {
        var random = new Random(seed);
        var points = new Vector3[count];
        for (var index = 0; index < count; index++)
            points[index] = center + (new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle()) - new Vector3(0.5f)) * size;

        return points;
    }

    private sealed class Scene(int substeps = 2, int iterations = 4, Vector3? gravity = null,
                               [System.Runtime.CompilerServices.CallerMemberName] string name = "") : IDisposable
    {
        public readonly List<PhysicsBody> Bodies = new();
        public readonly List<PhysicsJoint> Joints = new();
        public readonly List<PhysicsForce> Forces = new();
        public float ContactSpeed = 1f;
        public bool BuildMeshesInBackground;
        public int ContactTotal;
        public int ContactPeak;

        public void AddGround(Vector3? size = null, float centreY = -0.25f)
        {
            Bodies.Add(new PhysicsBody { Id = Guid.NewGuid(), Kind = PhysicsBodyKind.Static, Position = new Vector3(0, centreY, 0), Size = size ?? new Vector3(60f, 0.5f, 60f) });
        }

        public PhysicsBody AddInstanced(PhysicsBody body, Vector3[] positions)
        {
            body.Id = Guid.NewGuid();
            body.InstanceCount = positions.Length;
            body.Instances = new PhysicsBodyInstance[positions.Length];
            for (var index = 0; index < positions.Length; index++)
                body.Instances[index] = new PhysicsBodyInstance { Position = positions[index], Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, HullIndex = -1 };

            Bodies.Add(body);
            return body;
        }

        public void Run(float seconds)
        {
            var settings = new PhysicsWorldSettings
                               {
                                   Gravity = gravity ?? new Vector3(0, -9.81f, 0),
                                   ContactStiffness = 30f,
                                   FixedRate = 60f,
                                   Substeps = substeps,
                                   SolverIterations = iterations,
                                   TimeScale = 1f,
                                   Multithreaded = true,
                                   ContactSpeedThreshold = ContactSpeed,
                                   BuildMeshesInBackground = BuildMeshesInBackground,
                               };
            var frames = Math.Max(1, (int)MathF.Round(seconds / FrameSec));
            for (var frame = 0; frame < frames; frame++)
            {
                _time += FrameSec;
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                _simulation.Update(settings, _time, Bodies, Joints, Forces);
                FrameTimes.Record(name, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                                  GC.GetAllocatedBytesForCurrentThread() - allocated);
                ContactTotal += _simulation.Contacts.Count;
                ContactPeak = Math.Max(ContactPeak, _simulation.Contacts.Count);
            }
        }

        public Point[] Points()
        {
            var points = new Point[_simulation.RenderCount(false)];
            _simulation.WritePoints(points, false);
            return points;
        }

        public void Dispose() => _simulation.Dispose();

        // No step budget: a loaded test machine must not change what the scene does.
        private readonly PhysicsSimulation _simulation = new(new object()) { StepBudgetSec = 100f };
        private double _time;
    }
}
