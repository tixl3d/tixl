using System;
using System.Numerics;

namespace T3.Core.DataTypes;

public enum PhysicsShapeType
{
    Box = 0,
    Sphere = 1,
    Capsule = 2,
    Cylinder = 3,

    /// <summary>Convex hull of the connected geometry (one per geometry part). Falls back to Box without geometry.</summary>
    ConvexHull = 4,

    /// <summary>Triangle mesh of the connected geometry. Static and kinematic bodies only; a dynamic Mesh body is simulated like <see cref="Auto"/>.</summary>
    Mesh = 5,

    /// <summary>
    /// Picks the collider from what is connected: a box without geometry; with geometry the exact triangles for static
    /// and kinematic bodies, and for dynamic bodies a convex hull, split into several hulls where the shape is concave.
    /// </summary>
    Auto = 6,

    /// <summary>Every geometry part is split into convex hulls that follow its concave shape, even where one hull would do.</summary>
    ConvexDecomposition = 7,
}

public enum PhysicsBodyKind
{
    Dynamic = 0,
    Static = 1,
    Kinematic = 2,
}

/// <summary>One body of an instanced <see cref="PhysicsBody"/>: its placement and per-instance look.</summary>
public struct PhysicsBodyInstance
{
    public Vector3 Position;
    public Quaternion Orientation;
    public Vector3 Scale;
    public Vector4 Color;

    /// <summary>Index into <see cref="PhysicsBody.Hulls"/>, or -1 for the descriptor's primitive / first hull.</summary>
    public int HullIndex;
}

/// <summary>
/// Collision geometry of one piece of a body, in the space of the body it belongs to: a single convex hull, or a
/// compound of several when the piece is concave or made of several geometry parts.
/// </summary>
public sealed class PhysicsHull
{
    /// <summary>Hull candidate points relative to <see cref="Pivot"/>: every point of the piece.</summary>
    public Vector3[] Points = [];

    /// <summary>Where the piece sits in its source geometry. The published pose is the pose of this pivot,
    /// which is what <c>GeometryToMeshChunks</c> expects for its chunks.</summary>
    public Vector3 Pivot;

    /// <summary>
    /// The convex hulls the piece is simulated as, each a point set relative to <see cref="Pivot"/>. Null or a single
    /// entry means the piece is the one hull around <see cref="Points"/>.
    /// </summary>
    public Vector3[][] ConvexParts;

    /// <summary>Enclosed volume at scale one, the sum over <see cref="ConvexParts"/>; 0 when unknown. Drives density-based mass.</summary>
    public float Volume;

    public bool IsCompound => ConvexParts is { Length: > 1 };
}

/// <summary>
/// Declarative description of one rigid body. Produced by a body operator and consumed by
/// <c>Lib.physics.PhysicsWorld</c>; it carries no Bepu state, so the world can rebuild its
/// simulation without the graph knowing about physics engine types.
/// </summary>
public sealed class PhysicsBody
{
    /// <summary>Stable per operator instance, so the world keeps a body across frames even
    /// though the operator writes a fresh description each evaluation.</summary>
    public Guid Id;

    public PhysicsShapeType Shape = PhysicsShapeType.Box;
    public PhysicsBodyKind Kind = PhysicsBodyKind.Dynamic;

    public Vector3 Position;
    public Quaternion Orientation = Quaternion.Identity;

    /// <summary>Full extents for <see cref="PhysicsShapeType.Box"/>.</summary>
    public Vector3 Size = Vector3.One;

    public float Radius = 0.5f;

    /// <summary>Length of the cylindrical section for capsules and cylinders.</summary>
    public float Length = 1f;

    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;

    public float Mass = 1f;

    /// <summary>When set, a dynamic body weighs <see cref="Density"/> times its collider volume and <see cref="Mass"/> is only the fallback
    /// for a collider without volume.</summary>
    public bool MassFromDensity;

    /// <summary>Mass per cubic unit, used with <see cref="MassFromDensity"/>.</summary>
    public float Density = 1f;

    public float Friction = 0.5f;
    public float Bounciness;
    public float LinearDamping;
    public float AngularDamping;
    public float GravityScale = 1f;
    public bool Enabled = true;

    /// <summary>Bodies sharing the same non-zero group never collide with each other.</summary>
    public int CollisionGroup;

    /// <summary>
    /// When set, this description stands for <see cref="InstanceCount"/> bodies instead of one.
    /// Each instance is placed relative to <see cref="Position"/> / <see cref="Orientation"/>.
    /// The array may be longer than the count; it is owned by the producing operator.
    /// </summary>
    public PhysicsBodyInstance[] Instances;

    public int InstanceCount;

    /// <summary>Convex pieces for <see cref="PhysicsShapeType.ConvexHull"/>. Replaced, never mutated, when the source geometry changes.</summary>
    public PhysicsHull[] Hulls;

    /// <summary>Triangle list (three points per triangle) for <see cref="PhysicsShapeType.Mesh"/>. Replaced, never mutated.</summary>
    public Vector3[] MeshTriangles;

    public bool IsInstanced => Instances != null;

    /// <summary>
    /// The geometry the collision data was taken from, so the simulated bodies can be handed back as geometry.
    /// Null for a primitive body.
    /// </summary>
    public MeshGeometry SourceGeometry;

    /// <summary>True when every body is one part of <see cref="SourceGeometry"/>; false when every body is the whole of it.</summary>
    public bool SourcePerPart;

    /// <summary>
    /// Written by the world after each update: where this description's bodies are in the world's pose points
    /// (statics included). Lets an operator draw one description's bodies with their own mesh.
    /// </summary>
    public Point[] PublishedPoints;

    public int PublishedStart;
    public int PublishedCount;

    /// <summary>The world buffer the Published* fields were written against. Consumers compare it with the
    /// world buffer they pulled, so a body feeding two worlds is never drawn with the other world's poses.</summary>
    public BufferWithViews PublishedBuffer;

    public void CopyFrom(PhysicsBody other)
    {
        Shape = other.Shape;
        Kind = other.Kind;
        Position = other.Position;
        Orientation = other.Orientation;
        Size = other.Size;
        Radius = other.Radius;
        Length = other.Length;
        LinearVelocity = other.LinearVelocity;
        AngularVelocity = other.AngularVelocity;
        Mass = other.Mass;
        MassFromDensity = other.MassFromDensity;
        Density = other.Density;
        Friction = other.Friction;
        Bounciness = other.Bounciness;
        LinearDamping = other.LinearDamping;
        AngularDamping = other.AngularDamping;
        GravityScale = other.GravityScale;
        Enabled = other.Enabled;
        CollisionGroup = other.CollisionGroup;
        Hulls = other.Hulls;
        MeshTriangles = other.MeshTriangles;
    }
}

public enum PhysicsJointType
{
    BallSocket = 0,
    Hinge = 1,
    Weld = 2,
    DistanceServo = 3,
}

public enum PhysicsJointMode
{
    /// <summary>Body A to body B. With instanced bodies, instance i of A to instance i of B.</summary>
    Pair = 0,

    /// <summary>Consecutive instances of body A: 0-1, 1-2, ... Anchored halfway between each pair.</summary>
    Chain = 1,

    /// <summary>Every two instances of body A closer than <see cref="PhysicsJoint.LinkDistance"/>.</summary>
    Nearby = 2,

    /// <summary>Each instance of body A is held at the world position it starts at.</summary>
    World = 3,
}

/// <summary>
/// Declarative description of one constraint between two bodies.
/// </summary>
public sealed class PhysicsJoint
{
    public Guid Id;
    public PhysicsBody BodyA;
    public PhysicsBody BodyB;
    public PhysicsJointType Type = PhysicsJointType.BallSocket;

    public Vector3 LocalOffsetA;
    public Vector3 LocalOffsetB;
    public Vector3 HingeAxisA = Vector3.UnitY;
    public Vector3 HingeAxisB = Vector3.UnitY;

    /// <summary>Spring frequency in Hz. Higher is stiffer.</summary>
    public float Frequency = 30f;

    /// <summary>1 = critically damped, lower values add springiness.</summary>
    public float DampingRatio = 1f;

    public float TargetDistance;
    public bool Enabled = true;

    public PhysicsJointMode Mode = PhysicsJointMode.Pair;

    /// <summary>Largest centre distance that still links two instances in <see cref="PhysicsJointMode.Nearby"/>.</summary>
    public float LinkDistance = 1f;

    /// <summary>
    /// Pair and World: restricts the joint to one instance of an instanced body, so a rope can be pinned by its first
    /// link only. Negative means every instance; an index past the end means the last instance.
    /// </summary>
    public int InstanceIndex = -1;

    /// <summary>Hinge only: drives the pair around the hinge axis at this speed in radians per second. 0 leaves the hinge free.</summary>
    public float MotorSpeed;

    /// <summary>BallSocket only: the largest angle in degrees between <see cref="HingeAxisA"/> and <see cref="HingeAxisB"/>,
    /// which stops a limb from folding back. 0 means no limit.</summary>
    public float SwingLimit;

    /// <summary>
    /// Pair mode: places the joint at body B's starting position (plus <see cref="LocalOffsetB"/>) and works out A's
    /// anchor and axis from where the two bodies start. One joint then fits every instance, such as four wheels on
    /// one chassis. <see cref="HingeAxisB"/> is the axis; <see cref="LocalOffsetA"/> and <see cref="HingeAxisA"/> are ignored.
    /// </summary>
    public bool AutoAnchor;
}

public enum PhysicsForceType
{
    /// <summary>Constant push along <see cref="PhysicsForce.Direction"/>.</summary>
    Directional = 0,

    /// <summary>Towards (positive strength) or away from <see cref="PhysicsForce.Position"/>.</summary>
    Radial = 1,

    /// <summary>Swirl around the axis through <see cref="PhysicsForce.Position"/> along <see cref="PhysicsForce.Direction"/>.</summary>
    Vortex = 2,

    /// <summary>Sampled from <see cref="PhysicsForce.Field"/> at each body's position.</summary>
    Field = 3,
}

/// <summary>
/// Declarative description of a force acting on the dynamic bodies of a world.
/// </summary>
public sealed class PhysicsForce
{
    public Guid Id;
    public PhysicsForceType Type = PhysicsForceType.Directional;
    public Vector3 Position;
    public Vector3 Direction = Vector3.UnitY;

    /// <summary>Acceleration in m/s² for continuous forces, velocity change in m/s for impulses.</summary>
    public float Strength = 1f;

    /// <summary>Reach around <see cref="Position"/>; 0 or less means unlimited. Ignored by Directional and Field.</summary>
    public float Radius;

    /// <summary>0 keeps full strength inside the radius, 1 fades linearly to zero at its edge.</summary>
    public float Falloff = 1f;

    /// <summary>Divides the effect by each body's mass, so heavy bodies react less.</summary>
    public bool ScaleByMass;

    /// <summary>Applied once per <see cref="TriggerCount"/> change instead of continuously.</summary>
    public bool IsImpulse;

    /// <summary>Incremented by the producer every time the impulse should fire.</summary>
    public int TriggerCount;

    /// <summary>Only bodies of this collision group are affected; 0 affects all.</summary>
    public int CollisionGroup;

    public VectorField Field;
    public bool Enabled = true;
}
