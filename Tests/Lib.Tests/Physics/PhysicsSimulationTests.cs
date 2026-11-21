using System.Numerics;
using Lib.physics;
using T3.Core.DataTypes;
using Xunit;

namespace Lib.Tests.Physics;

/// <summary>
/// Drives the real <see cref="PhysicsSimulation"/> against Bepu with a scripted clock. Poses are read
/// from the simulation directly as well as through the point-writing path the operator uploads to the GPU.
/// </summary>
public class PhysicsSimulationTests
{
    private const float FrameSec = 1f / 60f;

    [Fact]
    public void BoxFallsAndRestsOnStaticGround()
    {
        using var harness = new Harness();
        var ground = harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var box = harness.AddBody(Box(new Vector3(0, 3, 0), Vector3.One));
        harness.Step(4f);

        Assert.True(harness.Sim.TryGetBodyPosition(box.Id, out var position));
        Assert.InRange(position.Y, 0.45f, 0.55f);
        Assert.True(harness.Sim.TryGetBodyPosition(ground.Id, out var groundPosition));
        Assert.Equal(-0.5f, groundPosition.Y);
    }

    [Fact]
    public void BodyWithoutSupportFollowsFreeFall()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.InRange(position.Y, 100f - 0.5f * 9.81f - 0.4f, 100f - 0.5f * 9.81f + 0.4f);
    }

    [Fact]
    public void NonFiniteInputsCreateAPlaceholderInsteadOfPoisoningTheWorld()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var broken = Box(Vector3.One, Vector3.One);
        broken.Position = new Vector3(float.NaN, 3f, 0f);
        broken.Size = new Vector3(float.NaN);
        broken.Mass = float.NaN;
        broken.Friction = float.NaN;
        var healthy = harness.AddBody(Sphere(new Vector3(2f, 3f, 0f)));
        harness.AddBody(broken);
        harness.Step(0.5f);

        // The NaN description must not poison the healthy body, and must itself be a finite
        // placeholder instead of a NaN body nothing can collide with.
        harness.Sim.TryGetBodyPosition(healthy.Id, out var healthyPosition);
        Assert.True(float.IsFinite(healthyPosition.Y));
        harness.Sim.TryGetBodyPosition(broken.Id, out var brokenPosition);
        Assert.True(float.IsFinite(brokenPosition.Y));
    }

    [Fact]
    public void UnchangedDescriptorDoesNotTeleportBodyBack()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(0.5f);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var early);
        harness.Step(0.5f);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var late);

        Assert.True(late.Y < early.Y - 1f, "body should keep falling while its description stays the same");
    }

    [Fact]
    public void ChangedDescriptorPositionMovesBody()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(0.5f);

        sphere.Position = new Vector3(5, 50, 0);
        harness.Step(FrameSec);

        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.Equal(5f, position.X, 0.01f);
        Assert.InRange(position.Y, 49.9f, 50f);
    }

    [Fact]
    public void SimTimeFollowsFixedStepsOfTheTimeline()
    {
        using var harness = new Harness();
        harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(1f);

        Assert.InRange(harness.Sim.SimTime, 0.95f, 1.02f);
    }

    [Fact]
    public void PausedFreezesSimTimeAndBodies()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(0.5f);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var before);
        var simTimeBefore = harness.Sim.SimTime;

        harness.Settings.Paused = true;
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(sphere.Id, out var after);
        Assert.Equal(before, after);
        Assert.Equal(simTimeBefore, harness.Sim.SimTime);
    }

    [Fact]
    public void SlowFrameCannotBuildUpSimulationDebt()
    {
        using var harness = new Harness();
        harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(FrameSec);
        var simTimeBefore = harness.Sim.SimTime;

        // 0.3 s is below the rewind threshold but far above what one frame may simulate.
        harness.Step(0.3f, 0.3f);

        Assert.InRange(harness.Sim.SimTime - simTimeBefore, 0f, 0.13f);
    }

    [Fact]
    public void TimeScaleZeroFreezesSimulation()
    {
        using var harness = new Harness();
        harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(0.25f);
        var simTimeBefore = harness.Sim.SimTime;

        harness.Settings.TimeScale = 0f;
        harness.Step(1f);

        Assert.Equal(simTimeBefore, harness.Sim.SimTime);
    }

    [Fact]
    public void RewindingTheTimelineRestartsFromTheDescriptions()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(2f);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var fallen);
        Assert.True(fallen.Y < 90f);

        harness.JumpTo(0.5);

        harness.Sim.TryGetBodyPosition(sphere.Id, out var restarted);
        Assert.Equal(100f, restarted.Y, 0.01f);
        Assert.True(harness.Sim.SimTime < 0.05f);
    }

    [Fact]
    public void ForwardJumpBeyondThresholdRestartsAndSmallJumpDoesNot()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(1f);
        harness.JumpTo(harness.Time + 0.3);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var afterSmallJump);
        Assert.True(afterSmallJump.Y < 99f, "a jump under half a second keeps the simulation");

        harness.JumpTo(harness.Time + 5);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var afterLargeJump);
        Assert.Equal(100f, afterLargeJump.Y, 0.01f);
    }

    [Fact]
    public void ResetRebuildsOnRisingEdgeOnly()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(1f);

        harness.Settings.Reset = true;
        harness.Step(FrameSec);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var afterReset);
        Assert.Equal(100f, afterReset.Y, 0.05f);

        // Held high, the simulation must keep running instead of restarting every frame.
        harness.Step(1f);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var whileHeld);
        Assert.True(whileHeld.Y < 98f);
    }

    [Fact]
    public void DisabledBodyIsRemovedAndCanReturn()
    {
        using var harness = new Harness();
        var kept = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        var toggled = harness.AddBody(Sphere(new Vector3(5, 100, 0)));
        harness.Step(FrameSec);
        Assert.Equal(2, harness.BodyCount);

        toggled.Enabled = false;
        harness.Step(FrameSec);
        Assert.Equal(1, harness.BodyCount);
        Assert.False(harness.Sim.TryGetBodyPosition(toggled.Id, out _));
        Assert.True(harness.Sim.TryGetBodyPosition(kept.Id, out _));

        toggled.Enabled = true;
        harness.Step(FrameSec);
        Assert.Equal(2, harness.BodyCount);
        Assert.True(harness.Sim.TryGetBodyPosition(toggled.Id, out _));
    }

    [Fact]
    public void DisconnectedBodyIsRemoved()
    {
        using var harness = new Harness();
        harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        var removed = harness.AddBody(Sphere(new Vector3(5, 100, 0)));
        harness.Step(FrameSec);

        harness.RemoveBody(removed);
        harness.Step(FrameSec);

        Assert.Equal(1, harness.BodyCount);
        Assert.False(harness.Sim.TryGetBodyPosition(removed.Id, out _));
    }

    [Fact]
    public void StaticBodiesAreOnlyCountedWithIncludeStatics()
    {
        using var harness = new Harness();
        harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        harness.AddBody(Box(new Vector3(0, 3, 0), Vector3.One));
        harness.Step(FrameSec);
        Assert.Equal(1, harness.BodyCount);

        harness.IncludeStatics = true;
        harness.Step(FrameSec);
        Assert.Equal(2, harness.BodyCount);
    }

    [Fact]
    public void ChangingShapeReplacesTheBodyUnderTheSameId()
    {
        using var harness = new Harness();
        var ground = harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var body = harness.AddBody(Box(new Vector3(0, 0.5f, 0), Vector3.One));
        harness.Step(1f);

        body.Shape = PhysicsShapeType.Sphere;
        body.Radius = 1f;
        body.Position = new Vector3(0, 3, 0);
        harness.Step(3f);

        Assert.True(harness.Sim.TryGetBodyPosition(body.Id, out var position));
        Assert.InRange(position.Y, 0.9f, 1.1f);
        Assert.True(harness.Sim.TryGetBodyPosition(ground.Id, out _));
    }

    [Theory]
    [InlineData(PhysicsShapeType.Box)]
    [InlineData(PhysicsShapeType.Sphere)]
    [InlineData(PhysicsShapeType.Capsule)]
    [InlineData(PhysicsShapeType.Cylinder)]
    public void EveryShapeComesToRestOnTheGround(PhysicsShapeType shape)
    {
        using var harness = new Harness();
        harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var body = harness.AddBody(new PhysicsBody { Id = Guid.NewGuid(), Shape = shape, Position = new Vector3(0, 3, 0) });
        harness.Step(4f);

        Assert.True(harness.Sim.TryGetBodyPosition(body.Id, out var position));
        Assert.InRange(position.Y, 0.2f, 1.2f);
        Assert.InRange(Math.Abs(position.X) + Math.Abs(position.Z), 0f, 1f);
    }

    [Fact]
    public void KinematicBodyMovesByItsVelocityAndIgnoresGravity()
    {
        using var harness = new Harness();
        var mover = harness.AddBody(new PhysicsBody
                                        {
                                            Id = Guid.NewGuid(),
                                            Kind = PhysicsBodyKind.Kinematic,
                                            Position = new Vector3(0, 10, 0),
                                            LinearVelocity = new Vector3(2, 0, 0),
                                        });
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(mover.Id, out var position);
        Assert.InRange(position.X, 1.9f, 2.05f);
        Assert.Equal(10f, position.Y, 0.001f);
    }

    [Fact]
    public void LinearDampingSlowsABody()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var free = harness.AddBody(Sphere(Vector3.Zero, velocity: new Vector3(10, 0, 0)));
        var damped = harness.AddBody(Sphere(new Vector3(0, 0, 10), velocity: new Vector3(10, 0, 0)));
        damped.LinearDamping = 5f;
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(free.Id, out var freePosition);
        harness.Sim.TryGetBodyPosition(damped.Id, out var dampedPosition);
        Assert.InRange(freePosition.X, 9.8f, 10.2f);
        Assert.True(dampedPosition.X < freePosition.X * 0.5f);
    }

    [Fact]
    public void GravityScaleZeroFloatsAndNegativeRises()
    {
        using var harness = new Harness();
        var floating = harness.AddBody(Sphere(new Vector3(0, 50, 0)));
        floating.GravityScale = 0f;
        var rising = harness.AddBody(Sphere(new Vector3(10, 50, 0)));
        rising.GravityScale = -1f;
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(floating.Id, out var floatingPosition);
        harness.Sim.TryGetBodyPosition(rising.Id, out var risingPosition);
        Assert.InRange(floatingPosition.Y, 49.9f, 50.1f);
        Assert.True(risingPosition.Y > 50f + 3f);
    }

    [Fact]
    public void BallSocketKeepsAPendulumAtItsLength()
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        harness.AddJoint(new PhysicsJoint
                             {
                                 Id = Guid.NewGuid(),
                                 BodyA = anchor,
                                 BodyB = bob,
                                 LocalOffsetA = Vector3.Zero,
                                 LocalOffsetB = new Vector3(-2, 0, 0),
                                 Frequency = 60f,
                             });

        var lowestY = float.MaxValue;
        var shortestDistance = float.MaxValue;
        var longestDistance = 0f;
        for (var frame = 0; frame < 90; frame++)
        {
            harness.Step(FrameSec);
            harness.Sim.TryGetBodyPosition(bob.Id, out var position);
            var distance = Vector3.Distance(position, new Vector3(0, 5, 0));
            lowestY = Math.Min(lowestY, position.Y);
            shortestDistance = Math.Min(shortestDistance, distance);
            longestDistance = Math.Max(longestDistance, distance);
        }

        // A two meter pendulum released sideways passes through its lowest point at y = 3.
        Assert.InRange(lowestY, 2.9f, 3.4f);
        Assert.InRange(shortestDistance, 1.8f, 2.2f);
        Assert.InRange(longestDistance, 1.8f, 2.2f);
    }

    [Theory]
    [InlineData(PhysicsJointType.BallSocket)]
    [InlineData(PhysicsJointType.Hinge)]
    [InlineData(PhysicsJointType.Weld)]
    [InlineData(PhysicsJointType.DistanceServo)]
    public void EveryJointTypeCreatesExactlyOneConstraintAndSurvivesStepping(PhysicsJointType type)
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var other = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        harness.AddJoint(new PhysicsJoint
                             {
                                 Id = Guid.NewGuid(),
                                 BodyA = anchor,
                                 BodyB = other,
                                 Type = type,
                                 LocalOffsetB = type switch
                                                    {
                                                        PhysicsJointType.Weld => new Vector3(2, 0, 0),
                                                        PhysicsJointType.DistanceServo => Vector3.Zero,
                                                        _ => new Vector3(-2, 0, 0),
                                                    },
                                 TargetDistance = 2f,
                             });
        harness.Step(1f);

        Assert.Equal(1, harness.Sim.ConstraintCount);
        harness.Sim.TryGetBodyPosition(other.Id, out var position);
        Assert.False(float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z));

        // Free fall for a second would put the body more than five meters from the anchor.
        Assert.True(Vector3.Distance(position, new Vector3(0, 5, 0)) < 3f);
    }

    [Fact]
    public void UnchangedJointIsNotRecreatedEveryFrame()
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        harness.AddJoint(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = anchor, BodyB = bob, LocalOffsetB = new Vector3(-2, 0, 0) });
        harness.Step(3f);

        Assert.Equal(1, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void ChangedJointSettingsReplaceTheConstraintWithoutDuplicating()
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        var joint = harness.AddJoint(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = anchor, BodyB = bob, LocalOffsetB = new Vector3(-2, 0, 0) });
        harness.Step(0.5f);

        joint.Frequency = 10f;
        harness.Step(0.5f);

        Assert.Equal(1, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void DisconnectedJointReleasesTheBody()
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        var joint = harness.AddJoint(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = anchor, BodyB = bob, LocalOffsetB = new Vector3(-2, 0, 0) });
        harness.Step(0.5f);
        Assert.Equal(1, harness.Sim.ConstraintCount);

        harness.RemoveJoint(joint);
        harness.Step(2f);

        Assert.Equal(0, harness.Sim.ConstraintCount);
        harness.Sim.TryGetBodyPosition(bob.Id, out var position);
        Assert.True(position.Y < 5f - 8f, "without the joint the bob falls freely");
    }

    [Fact]
    public void WeldJointHoldsAnOffsetPairTogetherUnderOpposingVelocities()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var left = harness.AddBody(Sphere(new Vector3(0, 0, 0), velocity: new Vector3(3, 0, 0)));
        var right = harness.AddBody(Sphere(new Vector3(2, 0, 0), velocity: new Vector3(-3, 0, 0)));
        harness.AddJoint(new PhysicsJoint
                             {
                                 Id = Guid.NewGuid(),
                                 BodyA = left,
                                 BodyB = right,
                                 Type = PhysicsJointType.Weld,
                                 LocalOffsetB = new Vector3(2, 0, 0),
                                 Frequency = 60f,
                             });

        harness.Step(0.5f);

        harness.Sim.TryGetBodyPosition(left.Id, out var leftPosition);
        harness.Sim.TryGetBodyPosition(right.Id, out var rightPosition);

        // Free bodies closing at 6 m/s would have met in the middle; the weld is what keeps the
        // offset, and LocalOffsetB is the second body's origin in the first one's frame.
        Assert.InRange(rightPosition.X - leftPosition.X, 1.95f, 2.05f);
        Assert.InRange(Math.Abs(rightPosition.Y - leftPosition.Y), 0f, 0.01f);
    }

    [Fact]
    public void RemovingAnUnrelatedBodyKeepsTheJointOnce()
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        var bystander = harness.AddBody(Sphere(new Vector3(20, 50, 0)));
        harness.AddJoint(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = anchor, BodyB = bob, LocalOffsetB = new Vector3(-2, 0, 0) });
        harness.Step(0.5f);

        bystander.Enabled = false;
        harness.Step(0.5f);

        Assert.Equal(1, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void RemovingAJointPartnerDropsTheJointWithoutError()
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        harness.AddJoint(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = anchor, BodyB = bob, LocalOffsetB = new Vector3(-2, 0, 0) });
        harness.Step(0.5f);

        bob.Enabled = false;
        harness.Step(0.5f);

        Assert.Equal(0, harness.Sim.ConstraintCount);
        Assert.Equal(1, harness.BodyCount);
    }

    [Fact]
    public void JointToStaticBodyIsIgnored()
    {
        using var harness = new Harness();
        var wall = harness.AddBody(Box(new Vector3(0, 5, 0), Vector3.One, PhysicsBodyKind.Static));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        harness.AddJoint(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = wall, BodyB = bob });
        harness.Step(0.5f);

        Assert.Equal(0, harness.Sim.ConstraintCount);
    }

    [Fact]
    public void ChangingSolverConfigurationRebuildsWithoutLosingBodies()
    {
        using var harness = new Harness();
        var anchor = harness.AddBody(Box(new Vector3(0, 5, 0), new Vector3(0.2f, 0.2f, 0.2f), PhysicsBodyKind.Kinematic));
        var bob = harness.AddBody(Sphere(new Vector3(2, 5, 0), radius: 0.3f));
        harness.AddJoint(new PhysicsJoint { Id = Guid.NewGuid(), BodyA = anchor, BodyB = bob, LocalOffsetB = new Vector3(-2, 0, 0) });
        harness.Step(0.5f);

        harness.Settings.Substeps = 4;
        harness.Step(0.5f);

        Assert.Equal(2, harness.BodyCount);
        Assert.Equal(1, harness.Sim.ConstraintCount);
        Assert.True(harness.Sim.TryGetBodyPosition(bob.Id, out _));
    }

    [Fact]
    public void DeterministicSingleThreadedRunsReplayBitIdentically()
    {
        var first = RunStack();
        var second = RunStack();

        Assert.Equal(first, second);

        static Vector3[] RunStack()
        {
            using var harness = new Harness();
            harness.Settings.Deterministic = true;
            harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
            var bodies = new PhysicsBody[6];
            for (var index = 0; index < bodies.Length; index++)
            {
                bodies[index] = harness.AddBody(Box(new Vector3(0.1f * index, 0.5f + index * 1.1f, 0), Vector3.One));
            }

            harness.Step(3f);

            var positions = new Vector3[bodies.Length];
            for (var index = 0; index < bodies.Length; index++)
            {
                harness.Sim.TryGetBodyPosition(bodies[index].Id, out positions[index]);
            }

            return positions;
        }
    }

    [Theory]
    [InlineData(0f, false, -0.05f, 0.1f)]
    [InlineData(0.5f, false, 0.7f, 1.7f)]
    [InlineData(1f, false, 3.5f, 5.2f)]
    [InlineData(1f, true, 3.5f, 5.2f)]
    public void ReboundHeightFollowsBounciness(float bounciness, bool multithreaded, float minRise, float maxRise)
    {
        using var harness = new Harness();
        harness.Settings.Multithreaded = multithreaded;
        harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var ball = Sphere(new Vector3(0, 5, 0), radius: 0.5f);
        ball.Bounciness = bounciness;
        harness.AddBody(ball);

        // Free fall to the floor takes about 0.95 s; sample the rebound after it.
        harness.Step(1.2f);
        var apex = 0f;
        for (var frame = 0; frame < 90; frame++)
        {
            harness.Step(FrameSec);
            harness.Sim.TryGetBodyPosition(ball.Id, out var position);
            apex = Math.Max(apex, position.Y);
        }

        // The ball rests with its center at 0.5 and fell 4.5 m.
        Assert.InRange(apex - 0.5f, minRise, maxRise);
    }

    [Fact]
    public void BouncyBodyAtRestStaysAtRest()
    {
        using var harness = new Harness();
        harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var ball = Sphere(new Vector3(0, 0.5f, 0), radius: 0.5f);
        ball.Bounciness = 1f;
        harness.AddBody(ball);
        harness.Step(3f);

        harness.Sim.TryGetBodyPosition(ball.Id, out var position);
        Assert.InRange(position.Y, 0.48f, 0.52f);
    }

    [Fact]
    public void BounceBetweenTwoDynamicBodiesConservesMomentum()
    {
        using var harness = new Harness();
        harness.Settings.Gravity = Vector3.Zero;
        var left = Sphere(new Vector3(-3, 0, 0), velocity: new Vector3(4, 0, 0));
        var right = Sphere(new Vector3(3, 0, 0), velocity: new Vector3(-4, 0, 0));
        left.Bounciness = 1f;
        right.Bounciness = 1f;
        harness.AddBody(left);
        harness.AddBody(right);
        harness.Step(1.5f);

        // Equal masses colliding head on with full bounce swap directions.
        harness.Sim.TryGetBodyPosition(left.Id, out var leftPosition);
        harness.Sim.TryGetBodyPosition(right.Id, out var rightPosition);
        Assert.True(leftPosition.X < -0.5f, $"left body should have rebounded, is at {leftPosition.X}");
        Assert.True(rightPosition.X > 0.5f, $"right body should have rebounded, is at {rightPosition.X}");
    }

    [Fact]
    public void StepBudgetLimitsHowMuchOneUpdateSimulates()
    {
        using var harness = new Harness();
        harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(FrameSec);
        harness.Sim.StepBudgetSec = 0f;
        var simTimeBefore = harness.Sim.SimTime;

        harness.Step(0.1f, 0.1f);

        // Six fixed steps were due; with no budget left only the mandatory one runs and the rest is dropped.
        Assert.InRange(harness.Sim.SimTime - simTimeBefore, 0.01f, 0.02f);
    }

    [Fact]
    public void DeterministicMultithreadedBouncingReplaysBitIdentically()
    {
        var first = RunPile();
        var second = RunPile();

        Assert.Equal(first, second);

        static Vector3[] RunPile()
        {
            using var harness = new Harness();
            harness.Settings.Deterministic = true;
            harness.Settings.Multithreaded = true;
            harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
            var balls = new PhysicsBody[40];
            for (var index = 0; index < balls.Length; index++)
            {
                var ball = Sphere(new Vector3((index % 5) * 0.9f - 1.8f, 2f + (index / 5) * 0.9f, (index % 3) * 0.2f), radius: 0.4f);
                ball.Bounciness = 0.8f;
                balls[index] = harness.AddBody(ball);
            }

            harness.Step(4f);

            var positions = new Vector3[balls.Length];
            for (var index = 0; index < balls.Length; index++)
            {
                harness.Sim.TryGetBodyPosition(balls[index].Id, out positions[index]);
            }

            return positions;
        }
    }

    [Fact]
    public void WritePointsSkipsStaticsUnlessRequested()
    {
        using var harness = new Harness();
        harness.AddBody(Box(new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), PhysicsBodyKind.Static));
        var box = harness.AddBody(Box(new Vector3(0, 100, 0), Vector3.One));
        harness.Step(0.25f);
        var points = new Point[2];

        Assert.Equal(1, harness.Sim.WritePoints(points, includeStatics: false));
        harness.Sim.TryGetBodyPosition(box.Id, out var position);
        Assert.Equal(position.Y, points[0].Position.Y, 0.05f);

        Assert.Equal(2, harness.Sim.WritePoints(points, includeStatics: true));
    }

    [Fact]
    public void WritePointsScaleDescribesTheCollider()
    {
        using var harness = new Harness();
        harness.AddBody(Box(new Vector3(0, 100, 0), new Vector3(1, 2, 3)));
        harness.AddBody(Sphere(new Vector3(10, 100, 0), radius: 0.75f));
        harness.AddBody(new PhysicsBody
                            {
                                Id = Guid.NewGuid(), Shape = PhysicsShapeType.Capsule, Position = new Vector3(20, 100, 0), Radius = 0.5f, Length = 2f,
                            });
        harness.AddBody(new PhysicsBody
                            {
                                Id = Guid.NewGuid(), Shape = PhysicsShapeType.Cylinder, Position = new Vector3(30, 100, 0), Radius = 0.5f, Length = 2f,
                            });
        harness.Step(FrameSec);
        var points = new Point[4];

        Assert.Equal(4, harness.Sim.WritePoints(points, includeStatics: false));
        Assert.Equal(new Vector3(1, 2, 3), points[0].Scale);
        Assert.Equal(new Vector3(1.5f), points[1].Scale);
        Assert.Equal(new Vector3(1, 3, 1), points[2].Scale);
        Assert.Equal(new Vector3(1, 2, 1), points[3].Scale);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InterpolationBlendsBetweenFixedSteps(bool interpolate)
    {
        using var harness = new Harness();
        harness.Settings.FixedRate = 30f;
        harness.Settings.Interpolate = interpolate;
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(0.5f);
        var points = new Point[1];

        var largestLead = 0f;
        for (var frame = 0; frame < 20; frame++)
        {
            harness.Step(FrameSec);
            harness.Sim.WritePoints(points, includeStatics: false);
            harness.Sim.TryGetBodyPosition(sphere.Id, out var simulated);
            largestLead = Math.Max(largestLead, points[0].Position.Y - simulated.Y);
        }

        // A falling body is rendered between its previous and its current step, which is above the current one.
        if (interpolate)
        {
            Assert.True(largestLead > 0.02f);
        }
        else
        {
            Assert.True(largestLead < 0.0001f);
        }
    }

    [Fact]
    public void NonFiniteFixedRateFallsBackToTheDefault()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Settings.FixedRate = float.NaN;
        harness.Step(1f);

        // A NaN rate would compare as changed every frame and build a NaN step size; the fallback
        // keeps the world stepping at its default rate instead of rebuilding forever.
        Assert.InRange(harness.Sim.SimTime, 0.95f, 1.02f);
        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.True(position.Y < 99f);
    }

    [Fact]
    public void NonFiniteGravityKeepsTheLastValidValue()
    {
        using var harness = new Harness();
        var sphere = harness.AddBody(Sphere(new Vector3(0, 100, 0)));
        harness.Step(0.1f);

        // NaN would propagate through the integrator into every body and never recover.
        harness.Settings.Gravity = new Vector3(float.NaN, float.NaN, float.NaN);
        harness.Step(1f);

        harness.Sim.TryGetBodyPosition(sphere.Id, out var position);
        Assert.False(float.IsNaN(position.Y), "a NaN gravity must not poison body positions");
        Assert.InRange(position.Y, 93.5f, 95f);
    }

    private static PhysicsBody Box(Vector3 position, Vector3 size, PhysicsBodyKind kind = PhysicsBodyKind.Dynamic)
    {
        return new PhysicsBody { Id = Guid.NewGuid(), Shape = PhysicsShapeType.Box, Kind = kind, Position = position, Size = size };
    }

    private static PhysicsBody Sphere(Vector3 position, float radius = 0.5f, Vector3 velocity = default)
    {
        return new PhysicsBody
                   {
                       Id = Guid.NewGuid(),
                       Shape = PhysicsShapeType.Sphere,
                       Position = position,
                       Radius = radius,
                       LinearVelocity = velocity,
                   };
    }

    /// <summary>
    /// Stands in for the operator: feeds the simulation the same lists and settings the world gathers from its
    /// inputs, with a clock the test controls.
    /// </summary>
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

        public bool IncludeStatics { get; set; }

        public double Time { get; private set; }

        public int BodyCount => Sim.RenderCount(IncludeStatics);

        public PhysicsBody AddBody(PhysicsBody body)
        {
            _bodies.Add(body);
            return body;
        }

        public void RemoveBody(PhysicsBody body) => _bodies.Remove(body);

        public PhysicsJoint AddJoint(PhysicsJoint joint)
        {
            _joints.Add(joint);
            return joint;
        }

        public void RemoveJoint(PhysicsJoint joint) => _joints.Remove(joint);

        /// <summary>Advances the clock in render-sized frames.</summary>
        public void Step(float seconds, float frameSeconds = FrameSec)
        {
            var frames = Math.Max(1, (int)Math.Round(seconds / frameSeconds));
            for (var frame = 0; frame < frames; frame++)
            {
                Time += frameSeconds;
                Tick();
            }
        }

        public void JumpTo(double seconds)
        {
            Time = seconds;
            Tick();
        }

        public void Dispose()
        {
            Sim.Dispose();
        }

        private void Tick()
        {
            Sim.Update(Settings, Time, _bodies, _joints);
        }

        private readonly List<PhysicsBody> _bodies = new();
        private readonly List<PhysicsJoint> _joints = new();
    }
}
