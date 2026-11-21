using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using T3.Core.DataTypes;
using T3.Core.Logging;

namespace Lib.physics;

/// <summary>Per-evaluation world parameters, already read from the operator's inputs.</summary>
internal struct PhysicsWorldSettings
{
    public Vector3 Gravity;
    public float ContactStiffness;
    public float FixedRate;
    public int Substeps;
    public int SolverIterations;
    public float TimeScale;
    public bool Multithreaded;
    public bool Deterministic;
    public bool Interpolate;
    public bool Paused;
    public bool Reset;

    /// <summary>Closing speed in m/s from which an impact is reported as a contact. 0 or less uses the default of 1.</summary>
    public float ContactSpeedThreshold;

    /// <summary>
    /// Build triangle-mesh colliders on a worker thread. Until one is ready its body is a tiny placeholder, so
    /// leave this off where every frame must be exact: deterministic runs, rendering to a file, tests.
    /// </summary>
    public bool BuildMeshesInBackground;
}

/// <summary>One impact reported by the last <see cref="PhysicsSimulation.Update"/>.</summary>
internal struct PhysicsContact
{
    public Vector3 Position;

    /// <summary>Points from the first body of the pair towards the second.</summary>
    public Vector3 Normal;

    /// <summary>Closing speed along the normal in m/s.</summary>
    public float Speed;

    /// <summary>The two collidables, so an impact that spans several steps is reported once.</summary>
    internal ulong PairKey;
}

/// <summary>
/// Owns one Bepu simulation and turns the declarative PhysicsBody / PhysicsJoint descriptions
/// produced by the surrounding graph into rigid-body state.
///
/// Time is handled with a fixed-step accumulator: the render rate never enters Bepu, poses are
/// interpolated between the last two fixed steps, and a timeline jump or rewind rebuilds the
/// simulation instead of trying to step backwards. It has no dependency on the operator or GPU
/// types, so it can be driven by a scripted clock.
/// </summary>
internal sealed class PhysicsSimulation : IDisposable
{
    public PhysicsSimulation(object logSource)
    {
        _logSource = logSource;
    }

    /// <summary>Simulated seconds since the last rebuild.</summary>
    public float SimTime => _simTime;

    /// <summary>Number of live Bepu constraints, which differs from the joint count if a constraint leaks or is added twice.</summary>
    public int ConstraintCount => _simulation?.Solver.CountConstraints() ?? 0;

    /// <summary>Wall-clock time one <see cref="Update"/> may spend stepping before the remaining simulated time is dropped.
    /// At least one step always runs. Hitting the limit makes a replay differ from an unloaded machine's, so it only matters for scenes too heavy to run in real time.</summary>
    public float StepBudgetSec { get; set; } = 0.012f;

    public int RenderCount(bool includeStatics) => includeStatics ? _renderOrder.Count : _dynamicRenderCount;

    /// <summary>
    /// Tells every description of the last update where its bodies are in <paramref name="allPoints"/>, which must
    /// have been filled by <see cref="WritePoints"/> with statics included.
    /// </summary>
    public void PublishBodyRanges(Point[] allPoints, BufferWithViews pointsBuffer)
    {
        foreach (var (descriptor, start, count) in _bodyRanges)
        {
            descriptor.PublishedPoints = allPoints;
            descriptor.PublishedStart = start;
            descriptor.PublishedCount = allPoints == null ? 0 : Math.Max(0, Math.Min(count, allPoints.Length - start));
            descriptor.PublishedBuffer = pointsBuffer;
        }
    }

    /// <summary>Impacts of the steps run by the last <see cref="Update"/>, in a deterministic order. Empty on frames that ran no step.</summary>
    public IReadOnlyList<PhysicsContact> Contacts => _contacts;

    /// <summary>Number of live Bepu shapes, which stays below the body count when instances share a hull or mesh.</summary>
    public int SharedShapeCount => _sharedShapes.Count;

    /// <summary>Simulated position of a body at the latest fixed step, without render interpolation.</summary>
    public bool TryGetBodyPosition(Guid bodyId, out Vector3 position)
    {
        if (_bodies.TryGetValue(bodyId, out var entry))
        {
            position = entry.CurrentPose.Position;
            return true;
        }

        position = default;
        return false;
    }

    public void Update(in PhysicsWorldSettings settings, double nowSeconds, List<PhysicsBody> bodies, List<PhysicsJoint> joints,
                       List<PhysicsForce> forces = null)
    {
        _frameBodies = bodies;
        _frameJoints = joints;
        _frameForces = forces;
        _buildMeshesInBackground = settings.BuildMeshesInBackground;
        _trackedKinematics.Clear();

        var contactStiffness = Math.Clamp(FiniteOr(settings.ContactStiffness, 30f), 5f, 120f);
        var rate = Math.Clamp(FiniteOr(settings.FixedRate, 60f), 10f, 240f);
        var substeps = Math.Clamp(settings.Substeps, 1, 8);
        var iterations = Math.Clamp(settings.SolverIterations, 1, 32);
        var timeScale = Math.Clamp(FiniteOr(settings.TimeScale, 1f), 0f, 4f);
        var resetTriggered = settings.Reset && !_wasResetPressed;
        _wasResetPressed = settings.Reset;

        // Gravity and stiffness are read through the callback's settings reference, so they can
        // change without rebuilding; the solver layout below is baked into the simulation.
        // A NaN from an upstream graph would otherwise poison every body for the rest of the
        // session, and a NaN rate would read as "changed" every frame and rebuild the world.
        if (IsFinite(settings.Gravity))
            _settings.Gravity = settings.Gravity;

        _settings.ContactFrequency = contactStiffness;
        _settings.ContactSpeedThreshold = float.IsFinite(settings.ContactSpeedThreshold) && settings.ContactSpeedThreshold > 0f
                                              ? settings.ContactSpeedThreshold
                                              : 1f;

        var configChanged = rate != _fixedRate
                            || substeps != _substeps
                            || iterations != _solverIterations
                            || settings.Multithreaded != _multithreaded
                            || settings.Deterministic != _deterministic;

        var delta = double.IsNaN(_lastTimeSeconds) ? 0 : nowSeconds - _lastTimeSeconds;
        var timeJumped = delta < -0.001 || delta > MaxTimeJump;
        _lastTimeSeconds = nowSeconds;

        // Contacts belong to the frame they happened in. The world evaluates once per pulled output, so a second
        // update at the same time must leave them for whoever reads the contact buffer after the pose buffer.
        if (delta != 0)
            _contacts.Clear();

        // The simulated time this frame will roughly cover; kinematic bodies driven by their position use it to
        // turn the move into a velocity. Zero when nothing advances, so those bodies are placed directly.
        _frameDelta = resetTriggered || _simulation == null || configChanged || timeJumped || settings.Paused
                          ? 0f
                          : (float)(Math.Clamp(delta, 0, MaxFrameDelta) * timeScale);

        if (resetTriggered || _simulation == null || configChanged || timeJumped)
        {
            RebuildSimulation(rate, substeps, iterations, settings.Multithreaded, settings.Deterministic);
            ReconcileBodies();
            ReconcileJoints();
            _accumulator = 0;
            _simTime = 0;
        }
        else
        {
            ReconcileBodies();
            ReconcileJoints();

            if (!settings.Paused)
                _accumulator += Math.Clamp(delta, 0, MaxFrameDelta) * timeScale;
        }

        PrepareForces(settings.Paused);

        var dt = 1.0 / _fixedRate;
        var maxSteps = Math.Max(1, (int)MathF.Ceiling(MaxFrameDelta / (float)dt));

        // Position-driven kinematic bodies get the velocity that reaches their target over exactly the steps
        // this frame will take. With no step this frame they keep their velocity and are not moved anyway.
        var plannedSteps = Math.Min((int)(_accumulator / dt), maxSteps);
        if (plannedSteps > 0)
        {
            var simulated = (float)(plannedSteps * dt);
            for (var index = 0; index < _trackedKinematics.Count; index++)
                TrackKinematicPose(_trackedKinematics[index].Entry, _trackedKinematics[index].Target, simulated);
        }

        _trackedKinematics.Clear();
        var steps = 0;
        var stepStart = Stopwatch.GetTimestamp();
        while (_accumulator >= dt && steps < maxSteps)
        {
            Step((float)dt);
            _accumulator -= dt;
            steps++;

            // Stepping runs on the evaluation thread, so a heavy scene must slow the simulation down instead of the editor.
            if (Stopwatch.GetElapsedTime(stepStart).TotalSeconds > StepBudgetSec)
                break;
        }

        // Never let a slow frame build a debt that turns into slow motion forever.
        if (_accumulator > dt)
            _accumulator = dt;

        _interpolationAlpha = settings.Interpolate ? (float)Math.Clamp(_accumulator / dt, 0.0, 1.0) : 1f;
    }

    /// <summary>Writes the interpolated pose of every rendered body and returns how many were written.</summary>
    public int WritePoints(Point[] points, bool includeStatics)
    {
        var count = Math.Min(RenderCount(includeStatics), points.Length);
        var writeIndex = 0;

        for (var i = 0; i < _renderOrder.Count && writeIndex < count; i++)
        {
            var entry = _renderOrder[i];
            if (entry.IsStatic && !includeStatics)
                continue;

            var pose = InterpolatePose(entry.PreviousPose, entry.CurrentPose, _interpolationAlpha);
            points[writeIndex++] = new Point
                                       {
                                           // A hull body is simulated around its centre of mass but published at its pivot,
                                           // so the piece it stands for is drawn where the source geometry had it.
                                           Position = pose.Position - Vector3.Transform(entry.LocalCenter, pose.Orientation),
                                           Orientation = pose.Orientation,
                                           Scale = entry.RenderScale,
                                           Color = entry.Color,
                                           F2 = entry.Speed,
                                       };
        }

        return writeIndex;
    }

    /// <summary>
    /// Writes the impacts of the last update, strongest first when there are more than fit. Unused points get a zero
    /// scale and F1 = 0, so a fixed-size buffer draws and emits nothing for them. Returns the number of real contacts written.
    /// </summary>
    public int WriteContacts(Point[] points)
    {
        var count = Math.Min(_contacts.Count, points.Length);
        if (_contacts.Count > points.Length)
        {
            _contactOrder.Clear();
            _contactOrder.AddRange(_contacts);
            _contactOrder.Sort(static (a, b) => b.Speed.CompareTo(a.Speed));
        }

        var source = _contacts.Count > points.Length ? _contactOrder : _contacts;
        for (var index = 0; index < count; index++)
        {
            var contact = source[index];
            points[index] = new Point
                                {
                                    Position = contact.Position,
                                    Orientation = OrientationAlong(contact.Normal),
                                    Scale = Vector3.One,
                                    F1 = contact.Speed,
                                    F2 = 1f,
                                };
        }

        for (var index = count; index < points.Length; index++)
        {
            points[index] = new Point
                                {
                                    Scale = Vector3.Zero,
                                    F1 = 0f,
                                    F2 = 0f,
                                };
        }

        return count;
    }

    /// <summary>Drops the Bepu simulation and every body; the next <see cref="Update"/> builds a fresh one.</summary>
    public void Release()
    {
        ReleaseSimulation();
    }

    public void Dispose()
    {
        ReleaseSimulation();
        _dispatcher?.Dispose();
        _dispatcher = null;
    }

    private void RebuildSimulation(float rate, int substeps, int iterations, bool multithreaded, bool deterministic)
    {
        ReleaseSimulation();

        _fixedRate = rate;
        _substeps = substeps;
        _solverIterations = iterations;
        _multithreaded = multithreaded;
        _deterministic = deterministic;

        if (multithreaded && _dispatcher == null)
            _dispatcher = new ThreadDispatcher(Math.Max(1, Environment.ProcessorCount - 1));
        else if (!multithreaded && _dispatcher != null)
        {
            // Single-threaded mode never passes the dispatcher to Timestep, so keeping its worker
            // threads alive would leak them for as long as the operator exists.
            _dispatcher.Dispose();
            _dispatcher = null;
        }

        _pool = new BufferPool();
        _materials.Clear();

        // Bepu reports worker indices below the dispatcher's thread count, or 0 without a dispatcher.
        _bounceRequests = new List<BounceRequest>[multithreaded ? _dispatcher.ThreadCount : 1];
        _contactRequests = new List<PhysicsContact>[_bounceRequests.Length];
        for (var index = 0; index < _bounceRequests.Length; index++)
        {
            _bounceRequests[index] = new List<BounceRequest>();
            _contactRequests[index] = new List<PhysicsContact>();
        }

        _simulation = Simulation.Create(_pool,
                                       new NarrowPhaseCallbacks
                                           {
                                               Materials = _materials,
                                               Settings = _settings,
                                               BounceRequests = _bounceRequests,
                                               ContactRequests = _contactRequests,
                                           },
                                       new PoseIntegratorCallbacks { Settings = _settings },
                                       new SolveDescription(_solverIterations, _substeps),
                                       new DefaultTimestepper());
        _simulation.Deterministic = deterministic;
    }

    private void ReleaseSimulation()
    {
        _simulation?.Dispose();
        _simulation = null;

        _pool?.Clear();
        _pool = null;

        foreach (var shared in _sharedShapes.Values)
            shared.OwnPool?.Clear();

        // A build still running owns its pool until it finishes; release the pool then.
        foreach (var build in _meshBuilds.Values)
        {
            var pool = build.Pool;
            build.Task.ContinueWith(_ => pool.Clear(), TaskScheduler.Default);
        }

        _meshBuilds.Clear();
        _abandonedMeshes.Clear();

        _bodies.Clear();
        _joints.Clear();
        _renderOrder.Clear();
        _dynamicRenderCount = 0;
        _materials.Clear();
        _sharedShapes.Clear();
        _nearbyLinks.Clear();
        _contacts.Clear();
        _previousContactPairs.Clear();
    }

    private void ReconcileBodies()
    {
        _seenBodies.Clear();
        _renderOrder.Clear();
        _bodyRanges.Clear();
        _dynamicRenderCount = 0;

        foreach (var descriptor in _frameBodies)
        {
            // Whatever this description adds to the render order is its range, however many bodies that is.
            if (_bodyRanges.Count > 0)
                CloseBodyRange();

            _bodyRanges.Add((descriptor, _renderOrder.Count, 0));

            var id = ResolveId(descriptor);
            var isInstanced = descriptor.IsInstanced;
            if (!isInstanced && HullFor(descriptor, -1) == null)
            {
                ReconcileBody(descriptor, id, new InstanceInfo(Vector3.One, Vector4.One, null));
                continue;
            }

            // An instanced description is expanded into one plain description per instance, so every
            // body goes through the same create / update / remove path as a hand-placed one. A single
            // hull body takes the same route as one identity instance, which places it at its pivot.
            var instances = isInstanced ? descriptor.Instances : SingleInstance;
            var count = isInstanced ? Math.Min(descriptor.InstanceCount, instances.Length) : 1;
            var baseOrientation = NormalizeOrientation(descriptor.Orientation);
            for (var index = 0; index < count; index++)
            {
                ref readonly var instance = ref instances[index];
                var scale = SanitizeScale(instance.Scale);
                var hull = HullFor(descriptor, instance.HullIndex);

                _instanceBody.CopyFrom(descriptor);
                _instanceBody.Orientation = baseOrientation * NormalizeOrientation(instance.Orientation);
                _instanceBody.Position = descriptor.Position + Vector3.Transform(instance.Position, baseOrientation);
                if (hull != null)
                    _instanceBody.Position += Vector3.Transform(hull.Pivot * scale, _instanceBody.Orientation);

                _instanceBody.Size = descriptor.Size * scale;
                _instanceBody.Radius = descriptor.Radius * MathF.Max(scale.X, scale.Z);
                _instanceBody.Length = descriptor.Length * scale.Y;

                ReconcileBody(_instanceBody, isInstanced ? DeriveId(id, index) : id, new InstanceInfo(scale, instance.Color, hull));
            }
        }

        if (_bodyRanges.Count > 0)
            CloseBodyRange();

        _removeBodies.Clear();
        foreach (var pair in _bodies)
        {
            if (!_seenBodies.Contains(pair.Key))
                _removeBodies.Add(pair.Key);
        }

        foreach (var id in _removeBodies)
        {
            RemoveBody(_bodies[id]);
            _bodies.Remove(id);
        }
    }

    private void CloseBodyRange()
    {
        var (descriptor, start, _) = _bodyRanges[^1];
        _bodyRanges[^1] = (descriptor, start, _renderOrder.Count - start);
    }

    private void ReconcileBody(PhysicsBody descriptor, Guid id, in InstanceInfo info)
    {
        _seenBodies.Add(id);

        // CreateBody and UpdateBody read pose, shape, mass and material fields raw; a NaN from an animated
        // input would poison the created collider, mass or contact material with nothing to catch it. The
        // fallbacks mirror the UpdateBody guards and keep the body a usable placeholder until it recovers.
        descriptor = FiniteDescriptor(descriptor);

        if (!_bodies.TryGetValue(id, out var entry))
        {
            if (!descriptor.Enabled)
                return;

            entry = CreateBody(descriptor, id, info);
            if (entry == null)
                return;

            _bodies.Add(id, entry);
        }
        else
        {
            if (!descriptor.Enabled)
            {
                RemoveBody(entry);
                _bodies.Remove(id);
                return;
            }

            entry = UpdateBody(entry, descriptor, id, info);
            if (entry == null)
            {
                _bodies.Remove(id);
                return;
            }

            _bodies[id] = entry;
        }

        entry.Friction = descriptor.Friction;
        entry.Bounciness = descriptor.Bounciness;
        entry.LinearDamping = Math.Max(0f, descriptor.LinearDamping);
        entry.AngularDamping = Math.Max(0f, descriptor.AngularDamping);
        entry.GravityScale = descriptor.GravityScale;
        entry.Group = descriptor.CollisionGroup;
        entry.Color = info.Color;

        if (entry.IsStatic)
            _materials.SetStatic(entry.StaticHandle.Value, entry.Friction, entry.Bounciness, entry.Group);
        else
            _materials.SetBody(entry.Handle.Value, entry.Friction, entry.Bounciness, entry.Group);

        _renderOrder.Add(entry);
        if (!entry.IsStatic)
            _dynamicRenderCount++;
    }

    /// <summary>The convex piece an instance uses, or null when the body is a primitive or a mesh.</summary>
    private static PhysicsHull HullFor(PhysicsBody descriptor, int hullIndex)
    {
        if (EffectiveShape(descriptor) != PhysicsShapeType.ConvexHull)
            return null;

        var hulls = descriptor.Hulls;
        if (hulls == null || hulls.Length == 0)
            return null;

        return hulls[(uint)hullIndex < (uint)hulls.Length ? hullIndex : 0];
    }

    /// <summary>
    /// The collider a description is built as. A triangle mesh has no volume to give a dynamic body mass, so a dynamic Mesh
    /// body is simulated as its hulls; a decomposition is a hull body whose pieces carry several convex parts.
    /// </summary>
    private static PhysicsShapeType EffectiveShape(PhysicsBody descriptor)
    {
        switch (descriptor.Shape)
        {
            case PhysicsShapeType.Mesh:
                return descriptor.Kind == PhysicsBodyKind.Dynamic ? PhysicsShapeType.ConvexHull : PhysicsShapeType.Mesh;

            case PhysicsShapeType.ConvexDecomposition:
                return PhysicsShapeType.ConvexHull;

            case PhysicsShapeType.Auto:
                if (descriptor.Kind != PhysicsBodyKind.Dynamic && descriptor.MeshTriangles != null)
                    return PhysicsShapeType.Mesh;

                return descriptor.Hulls != null || descriptor.MeshTriangles != null ? PhysicsShapeType.ConvexHull : PhysicsShapeType.Box;

            default:
                return descriptor.Shape;
        }
    }

    /// <summary>
    /// The mass of a dynamic body: its density times the collider volume when it is asked to weigh by volume, otherwise
    /// the fixed mass. A collider without volume, such as a flat hull that fell back to a box, keeps the fixed mass.
    /// </summary>
    private static float MassOf(PhysicsBody descriptor, float volume)
    {
        if (descriptor.MassFromDensity && float.IsFinite(descriptor.Density) && descriptor.Density > 0f && volume > 1e-9f)
            return MathF.Max(0.0001f, descriptor.Density * volume);

        // A NaN mass would poison the solver for every body it touches; the default matches PhysicsBody.Mass.
        return MathF.Max(0.0001f, FiniteOr(descriptor.Mass, 1f));
    }

    /// <summary>Volume of a primitive collider; 0 for hulls and meshes, whose volume comes from their pieces.</summary>
    private static float PrimitiveVolume(PhysicsBody descriptor)
    {
        var radius = MathF.Max(0.001f, descriptor.Radius);
        var length = MathF.Max(0.001f, descriptor.Length);
        switch (EffectiveShape(descriptor))
        {
            case PhysicsShapeType.Sphere:
                return 4f / 3f * MathF.PI * radius * radius * radius;
            case PhysicsShapeType.Capsule:
                return MathF.PI * radius * radius * (length + 4f / 3f * radius);
            case PhysicsShapeType.Cylinder:
                return MathF.PI * radius * radius * length;
            case PhysicsShapeType.Box:
                var size = Vector3.Max(descriptor.Size, new Vector3(0.001f));
                return size.X * size.Y * size.Z;
            default:
                return 0f;
        }
    }

    private static Vector3 SanitizeScale(Vector3 scale)
    {
        return new Vector3(SanitizeScale(scale.X), SanitizeScale(scale.Y), SanitizeScale(scale.Z));
    }

    /// <summary>A zero or NaN scale would build a degenerate shape; the sign is dropped because Bepu shapes cannot mirror.</summary>
    private static float SanitizeScale(float value)
    {
        return float.IsFinite(value) && MathF.Abs(value) > 0.0001f ? MathF.Abs(value) : 1f;
    }

    /** True when every field the create/update paths read raw is finite; a NaN would poison the body. */
    private static bool DescriptorIsFinite(PhysicsBody d)
    {
        return IsFinite(d.Position)
               && float.IsFinite(d.Orientation.X + d.Orientation.Y + d.Orientation.Z + d.Orientation.W)
               && IsFinite(d.Size) && float.IsFinite(d.Radius) && float.IsFinite(d.Length)
               && float.IsFinite(d.Mass) && float.IsFinite(d.Density)
               && IsFinite(d.LinearVelocity) && IsFinite(d.AngularVelocity)
               && float.IsFinite(d.LinearDamping) && float.IsFinite(d.AngularDamping)
               && float.IsFinite(d.Friction) && float.IsFinite(d.Bounciness) && float.IsFinite(d.GravityScale);
    }

    /// <summary>
    /// Replaces non-finite pose, shape, mass and material inputs with placeholders the way UpdateBody does,
    /// so a NaN from an animated input cannot poison the created collider, mass or contact material.
    /// </summary>
    private PhysicsBody FiniteDescriptor(PhysicsBody descriptor)
    {
        if (DescriptorIsFinite(descriptor))
            return descriptor;

        var clean = _finiteBody;
        clean.CopyFrom(descriptor);
        if (!IsFinite(clean.Position))
            clean.Position = Vector3.Zero;
        if (!float.IsFinite(clean.Orientation.X + clean.Orientation.Y + clean.Orientation.Z + clean.Orientation.W))
            clean.Orientation = Quaternion.Identity;
        if (!IsFinite(clean.Size))
            clean.Size = Vector3.Zero;
        clean.Radius = FiniteOr(clean.Radius, 0f);
        clean.Length = FiniteOr(clean.Length, 0f);
        clean.Mass = FiniteOr(clean.Mass, 1f);
        clean.Density = FiniteOr(clean.Density, 1f);
        if (!IsFinite(clean.LinearVelocity))
            clean.LinearVelocity = Vector3.Zero;
        if (!IsFinite(clean.AngularVelocity))
            clean.AngularVelocity = Vector3.Zero;
        clean.LinearDamping = MathF.Max(0f, FiniteOr(clean.LinearDamping, 0f));
        clean.AngularDamping = MathF.Max(0f, FiniteOr(clean.AngularDamping, 0f));
        clean.Friction = FiniteOr(clean.Friction, 0.5f);
        clean.Bounciness = FiniteOr(clean.Bounciness, 0f);
        clean.GravityScale = FiniteOr(clean.GravityScale, 1f);
        return clean;
    }

    /// <summary>Stable id of one instance of an instanced description: the same index maps to the same body every frame.</summary>
    private static Guid DeriveId(Guid id, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        var mixed = (uint)index * 2654435761u + 0x9E3779B9u;
        bytes[0] ^= (byte)mixed;
        bytes[1] ^= (byte)(mixed >> 8);
        bytes[2] ^= (byte)(mixed >> 16);
        bytes[3] ^= (byte)(mixed >> 24);
        bytes[12] ^= (byte)index;
        bytes[13] ^= (byte)(index >> 8);
        bytes[14] ^= (byte)(index >> 16);
        bytes[15] ^= (byte)(index >> 24);
        return new Guid(bytes);
    }

    private BodyEntry UpdateBody(BodyEntry entry, PhysicsBody descriptor, Guid id, in InstanceInfo info)
    {
        var shapeKey = ShapeKey.From(descriptor, info);
        if (!entry.ShapeKey.Equals(shapeKey) || IsPendingMeshReady(entry))
        {
            // Shape or mass changed: Bepu can't morph a collidable, so replace the body.
            RemoveBody(entry);
            return CreateBody(descriptor, id, info);
        }

        var orientation = NormalizeOrientation(descriptor.Orientation);
        var descriptorPosition = descriptor.Position;

        // A NaN from an animated input would poison the body and everything it touches; the body stays where it is.
        var poseIsFinite = IsFinite(descriptorPosition) && float.IsFinite(orientation.X + orientation.Y + orientation.Z + orientation.W);
        var tracksInputPose = poseIsFinite && entry.Kind == PhysicsBodyKind.Kinematic && !entry.IsStatic && _frameDelta > 0f
                              && descriptor.LinearVelocity == Vector3.Zero && descriptor.AngularVelocity == Vector3.Zero;
        if (tracksInputPose)
        {
            // A kinematic body animated through its Position or Rotation is moved there by velocity rather than
            // placed: a teleported body has no speed, so what it runs into is only pushed out of the overlap and
            // ends up embedded in it. Tracking the target every frame also corrects for steps the frame skipped.
            var target = new RigidPose(descriptorPosition + Vector3.Transform(entry.LocalCenter, orientation), orientation);
            _trackedKinematics.Add((entry, target));
            entry.LastInputPosition = descriptorPosition;
            entry.LastInputOrientation = orientation;
        }
        else if (poseIsFinite && (descriptorPosition != entry.LastInputPosition || orientation != entry.LastInputOrientation))
        {
            var pose = new RigidPose(descriptorPosition + Vector3.Transform(entry.LocalCenter, orientation), orientation);
            ApplyPose(entry, pose);
            entry.LastInputPosition = descriptorPosition;
            entry.LastInputOrientation = orientation;
        }

        if (!entry.IsStatic && IsFinite(descriptor.LinearVelocity) && IsFinite(descriptor.AngularVelocity)
            && (descriptor.LinearVelocity != entry.LastInputLinearVelocity
                || descriptor.AngularVelocity != entry.LastInputAngularVelocity))
        {
            var reference = _simulation.Bodies[entry.Handle];
            reference.Velocity = new BodyVelocity(descriptor.LinearVelocity, descriptor.AngularVelocity);
            reference.Awake = true;
            entry.LastInputLinearVelocity = descriptor.LinearVelocity;
            entry.LastInputAngularVelocity = descriptor.AngularVelocity;
        }

        // A sleeping body ignores the pose integrator, so a gravity-scale change has to wake it
        // or the correction would only start once something else touches the body.
        if (entry.Kind == PhysicsBodyKind.Dynamic && MathF.Abs(descriptor.GravityScale - entry.GravityScale) > 0.0001f)
        {
            var reference = _simulation.Bodies[entry.Handle];
            reference.Awake = true;
        }

        return entry;
    }

    private BodyEntry CreateBody(PhysicsBody descriptor, Guid id, in InstanceInfo info)
    {
        var orientation = NormalizeOrientation(descriptor.Orientation);
        var pose = new RigidPose(descriptor.Position, orientation);
        var entry = new BodyEntry
                        {
                            Id = id,
                            Kind = descriptor.Kind,
                            IsStatic = descriptor.Kind == PhysicsBodyKind.Static,
                            ShapeKey = ShapeKey.From(descriptor, info),
                            PreviousPose = pose,
                            CurrentPose = pose,
                            LastInputPosition = descriptor.Position,
                            LastInputOrientation = orientation,
                            LastInputLinearVelocity = descriptor.LinearVelocity,
                            LastInputAngularVelocity = descriptor.AngularVelocity,
                            RenderScale = RenderScaleFor(descriptor),
                        };

        switch (EffectiveShape(descriptor))
        {
            case PhysicsShapeType.ConvexHull:
                if (TryCreateHullBody(entry, descriptor, pose, info))
                    break;

                // No usable geometry (none connected, or too flat to enclose a volume): keep the body alive as a box.
                CreateBodyWithShape(entry, descriptor, pose, FallbackBox(descriptor, info));
                break;

            case PhysicsShapeType.Mesh:
                if (TryCreateMeshBody(entry, descriptor, pose, info))
                    break;

                if (entry.PendingMesh.HasValue)
                {
                    // Too small to touch anything; replaced by the mesh once its worker build is done.
                    CreateBodyWithShape(entry, descriptor, pose, new Sphere(0.001f));
                    break;
                }

                CreateBodyWithShape(entry, descriptor, pose, FallbackBox(descriptor, info));
                break;

            case PhysicsShapeType.Sphere:
                CreateBodyWithShape(entry, descriptor, pose, new Sphere(MathF.Max(0.001f, descriptor.Radius)));
                break;

            case PhysicsShapeType.Capsule:
                CreateBodyWithShape(entry, descriptor, pose,
                                    new Capsule(MathF.Max(0.001f, descriptor.Radius), MathF.Max(0.001f, descriptor.Length)));
                break;

            case PhysicsShapeType.Cylinder:
                CreateBodyWithShape(entry, descriptor, pose,
                                    new Cylinder(MathF.Max(0.001f, descriptor.Radius), MathF.Max(0.001f, descriptor.Length)));
                break;

            default:
                CreateBodyWithShape(entry, descriptor, pose,
                                    new Box(MathF.Max(0.001f, descriptor.Size.X),
                                            MathF.Max(0.001f, descriptor.Size.Y),
                                            MathF.Max(0.001f, descriptor.Size.Z)));
                break;
        }

        return entry;
    }

    private void CreateBodyWithShape<TShape>(BodyEntry entry, PhysicsBody descriptor, RigidPose pose, TShape shape)
        where TShape : unmanaged, IConvexShape
    {
        if (descriptor.Kind == PhysicsBodyKind.Static)
        {
            entry.ShapeIndex = _simulation.Shapes.Add(shape);
            entry.StaticHandle = _simulation.Statics.Add(new StaticDescription(pose, entry.ShapeIndex));
            entry.StaticPose = pose;
            return;
        }

        var velocity = new BodyVelocity(descriptor.LinearVelocity, descriptor.AngularVelocity);
        var description = descriptor.Kind == PhysicsBodyKind.Kinematic
                              ? BodyDescription.CreateConvexKinematic(pose, velocity, _simulation.Shapes, shape)
                              : BodyDescription.CreateConvexDynamic(pose, velocity, MassOf(descriptor, PrimitiveVolume(descriptor)), _simulation.Shapes, shape);

        entry.Handle = _simulation.Bodies.Add(description);
        entry.ShapeIndex = description.Collidable.Shape;
    }

    private static Box FallbackBox(PhysicsBody descriptor, in InstanceInfo info)
    {
        var size = descriptor.Size;
        return new Box(MathF.Max(0.001f, size.X), MathF.Max(0.001f, size.Y), MathF.Max(0.001f, size.Z));
    }

    /// <summary>
    /// Builds, or reuses, the convex hull of a piece and adds the body at the hull's centre of mass.
    /// Instances that use the same piece at the same scale share one Bepu shape.
    /// </summary>
    private bool TryCreateHullBody(BodyEntry entry, PhysicsBody descriptor, RigidPose pivotPose, in InstanceInfo info)
    {
        if (info.Hull is { IsCompound: true } piece && TryCreateCompoundBody(entry, descriptor, pivotPose, info, piece))
            return true;

        var points = info.Hull?.Points;
        object source = info.Hull;
        if (points == null && descriptor.MeshTriangles != null)
        {
            // A dynamic Mesh body arrives here without pieces: wrap the whole triangle soup.
            points = descriptor.MeshTriangles;
            source = descriptor.MeshTriangles;
        }

        if (points == null || points.Length < 4)
            return false;

        var key = new SharedShapeKey(source, info.Scale, false);
        if (!_sharedShapes.TryGetValue(key, out var shared))
        {
            var scaled = new Vector3[points.Length];
            for (var index = 0; index < points.Length; index++)
            {
                var point = points[index] * info.Scale;
                if (!IsFinite(point))
                    return false;

                scaled[index] = point;
            }

            if (!EnclosesVolume(scaled))
                return false;

            ConvexHull hull;
            Vector3 center;
            try
            {
                hull = new ConvexHull(scaled, _pool, out center);
            }
            catch (Exception)
            {
                // Bepu's hull builder rejects some degenerate inputs by throwing; the caller falls back to a box.
                return false;
            }

            shared = new SharedShape
                         {
                             Index = _simulation.Shapes.Add(hull),
                             Center = center,
                             UnitInertia = hull.ComputeInertia(1f),
                             Activity = BodyDescription.GetDefaultActivity(hull),
                             Volume = PhysicsDecomposition.HullVolume(scaled, _pool),
                         };
            _sharedShapes.Add(key, shared);
        }

        return AddSharedHullBody(entry, descriptor, pivotPose, info, key, shared);
    }

    /// <summary>
    /// Builds, or reuses, the compound of a piece's convex parts: a concave or multi-part object that moves as one body.
    /// Every part weighs its share of the volume, so the centre of mass and the inertia follow the real shape.
    /// </summary>
    private bool TryCreateCompoundBody(BodyEntry entry, PhysicsBody descriptor, RigidPose pivotPose, in InstanceInfo info, PhysicsHull piece)
    {
        var key = new SharedShapeKey(piece, info.Scale, false);
        if (!_sharedShapes.TryGetValue(key, out var shared))
        {
            var convexParts = piece.ConvexParts;
            var hulls = new List<(ConvexHull Hull, Vector3 Center, float Volume)>(convexParts.Length);
            var totalVolume = 0f;
            foreach (var part in convexParts)
            {
                if (part == null || part.Length < 4)
                    continue;

                var scaled = new Vector3[part.Length];
                var isValid = true;
                for (var index = 0; index < part.Length && isValid; index++)
                {
                    scaled[index] = part[index] * info.Scale;
                    isValid = IsFinite(scaled[index]);
                }

                if (!isValid || !EnclosesVolume(scaled))
                    continue;

                var volume = PhysicsDecomposition.HullVolume(scaled, _pool);
                if (volume <= 0f)
                    continue;

                try
                {
                    var hull = new ConvexHull(scaled, _pool, out var center);
                    hulls.Add((hull, center, volume));
                    totalVolume += volume;
                }
                catch (Exception)
                {
                    // A part Bepu cannot wrap is dropped; the remaining parts still describe the object.
                }
            }

            if (hulls.Count < 2 || totalVolume <= 0f)
            {
                foreach (var (hull, _, _) in hulls)
                    hull.Dispose(_pool);

                return false;
            }

            using var builder = new CompoundBuilder(_pool, _simulation.Shapes, hulls.Count);
            foreach (var (hull, center, volume) in hulls)
                builder.Add(hull, new RigidPose(center), volume / totalVolume);

            builder.BuildDynamicCompound(out var children, out var unitInertia, out var compoundCenter);
            var compound = new Compound(children);
            shared = new SharedShape
                         {
                             Index = _simulation.Shapes.Add(compound),
                             Center = compoundCenter,
                             UnitInertia = unitInertia,
                             Activity = new BodyActivityDescription(0.01f),
                             Volume = totalVolume,
                             IsCompound = true,
                         };
            _sharedShapes.Add(key, shared);
        }

        return AddSharedHullBody(entry, descriptor, pivotPose, info, key, shared);
    }

    /// <summary>Adds the body of a shared hull or compound shape at its centre of mass.</summary>
    private bool AddSharedHullBody(BodyEntry entry, PhysicsBody descriptor, RigidPose pivotPose, in InstanceInfo info, SharedShapeKey key, SharedShape shared)
    {
        shared.ReferenceCount++;
        entry.SharedShape = key;
        entry.ShapeIndex = shared.Index;
        entry.LocalCenter = shared.Center;
        entry.RenderScale = info.Scale;

        var pose = new RigidPose(pivotPose.Position + Vector3.Transform(shared.Center, pivotPose.Orientation), pivotPose.Orientation);
        entry.PreviousPose = pose;
        entry.CurrentPose = pose;

        if (descriptor.Kind == PhysicsBodyKind.Static)
        {
            entry.StaticHandle = _simulation.Statics.Add(new StaticDescription(pose, shared.Index));
            entry.StaticPose = pose;
            return true;
        }

        var velocity = new BodyVelocity(descriptor.LinearVelocity, descriptor.AngularVelocity);
        var collidable = new CollidableDescription(shared.Index);
        if (descriptor.Kind == PhysicsBodyKind.Kinematic)
        {
            entry.Handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, velocity, collidable, shared.Activity));
            return true;
        }

        // Inertia scales linearly with mass, so the shared shape stores it for a mass of one.
        var mass = MassOf(descriptor, shared.Volume);
        var inertia = shared.UnitInertia;
        inertia.InverseMass /= mass;
        inertia.InverseInertiaTensor.XX /= mass;
        inertia.InverseInertiaTensor.YX /= mass;
        inertia.InverseInertiaTensor.YY /= mass;
        inertia.InverseInertiaTensor.ZX /= mass;
        inertia.InverseInertiaTensor.ZY /= mass;
        inertia.InverseInertiaTensor.ZZ /= mass;
        entry.Handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(pose, velocity, inertia, collidable, shared.Activity));
        return true;
    }

    /// <summary>Adds a static or kinematic triangle-mesh collider. Triangles are added with both windings so either side collides.</summary>
    private bool TryCreateMeshBody(BodyEntry entry, PhysicsBody descriptor, RigidPose pose, in InstanceInfo info)
    {
        var triangles = descriptor.MeshTriangles;
        if (triangles == null || triangles.Length < 3)
            return false;

        var key = new SharedShapeKey(triangles, info.Scale, true);
        if (!_sharedShapes.TryGetValue(key, out var shared))
        {
            if (!TryGetMesh(key, triangles, info.Scale, out var mesh, out var ownPool))
            {
                entry.PendingMesh = _meshBuilds.ContainsKey(key) ? key : null;
                return false;
            }

            shared = new SharedShape
                         {
                             Index = _simulation.Shapes.Add(mesh),
                             Activity = new BodyActivityDescription(0.01f),
                             OwnPool = ownPool,
                         };
            _sharedShapes.Add(key, shared);
        }

        shared.ReferenceCount++;
        entry.SharedShape = key;
        entry.ShapeIndex = shared.Index;
        entry.RenderScale = info.Scale;

        if (descriptor.Kind == PhysicsBodyKind.Static)
        {
            entry.StaticHandle = _simulation.Statics.Add(new StaticDescription(pose, shared.Index));
            entry.StaticPose = pose;
            return true;
        }

        var velocity = new BodyVelocity(descriptor.LinearVelocity, descriptor.AngularVelocity);
        entry.Handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, velocity, new CollidableDescription(shared.Index), shared.Activity));
        return true;
    }

    /// <summary>
    /// The Bepu mesh for these triangles: built right here, or in the background when the world allows it. A mesh of
    /// a hundred thousand triangles takes tens of milliseconds to build its tree, too long for the frame. A background
    /// build gets a buffer pool of its own, because the simulation's pool is not thread-safe; the shape keeps it.
    /// </summary>
    private bool TryGetMesh(SharedShapeKey key, Vector3[] triangles, Vector3 scale, out Mesh mesh, out BufferPool ownPool)
    {
        mesh = default;
        ownPool = null;
        if (!_buildMeshesInBackground)
            return TryBuildMesh(triangles, scale, _pool, out mesh);

        if (!_meshBuilds.TryGetValue(key, out var build))
        {
            // A mesh whose build failed repeatedly stays abandoned until the world rebuilds or the
            // description publishes a new triangles array, which gets a new shape key.
            if (_abandonedMeshes.Contains(key))
                return false;

            var pool = new BufferPool();
            build = new MeshBuild(pool, Task.Run(() => TryBuildMesh(triangles, scale, pool, out var built) ? built : (Mesh?)null));
            _meshBuilds.Add(key, build);
        }

        if (!build.Task.IsCompleted)
            return false;

        _meshBuilds.Remove(key);
        if (build.Task.Status != TaskStatus.RanToCompletion || build.Task.Result is not { } finished)
        {
            // A faulted build used to wedge the body as its fallback box forever with no warning. Retry a
            // few times so a transient failure recovers, then abandon with a warning so bad data cannot loop.
            build.Pool.Clear();
            if (build.Attempts < MaxMeshBuildAttempts)
            {
                var pool = new BufferPool();
                _meshBuilds.Add(key, new MeshBuild(pool, Task.Run(() => TryBuildMesh(triangles, scale, pool, out var built) ? built : (Mesh?)null)) { Attempts = build.Attempts + 1 });
                return false;
            }

            if (_abandonedMeshes.Add(key))
                Log.Warning("PhysicsWorld: a body's triangle mesh failed to build three times; it keeps its fallback shape until its geometry changes", _logSource);

            return false;
        }

        mesh = finished;
        ownPool = build.Pool;
        return true;
    }

    /// <summary>Triangles are added with both windings, so either side of the surface collides.</summary>
    private static bool TryBuildMesh(Vector3[] triangles, Vector3 scale, BufferPool pool, out Mesh mesh)
    {
        mesh = default;
        var triangleCount = triangles.Length / 3;
        var valid = 0;
        for (var index = 0; index < triangleCount; index++)
        {
            if (IsUsableTriangle(triangles[index * 3], triangles[index * 3 + 1], triangles[index * 3 + 2]))
                valid++;
        }

        if (valid == 0)
            return false;

        pool.Take<Triangle>(valid * 2, out var buffer);
        var write = 0;
        for (var index = 0; index < triangleCount; index++)
        {
            var a = triangles[index * 3];
            var b = triangles[index * 3 + 1];
            var c = triangles[index * 3 + 2];
            if (!IsUsableTriangle(a, b, c))
                continue;

            buffer[write++] = new Triangle(a, b, c);
            buffer[write++] = new Triangle(a, c, b);
        }

        mesh = new Mesh(buffer, scale, pool);
        return true;
    }

    /** True for a placeholder body whose mesh has finished building, so the body is rebuilt with it. */
    private bool IsPendingMeshReady(BodyEntry entry)
    {
        return entry.PendingMesh is { } key
               && (!_meshBuilds.TryGetValue(key, out var build) || build.Task.IsCompleted);
    }

    private static bool IsUsableTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        return IsFinite(a) && IsFinite(b) && IsFinite(c) && Vector3.Cross(b - a, c - a).LengthSquared() > 1e-12f;
    }

    /// <summary>True when the points span all three axes; a line or a flat sheet has no hull Bepu can build.</summary>
    private static bool EnclosesVolume(Vector3[] points)
    {
        var origin = points[0];
        var farthest = origin;
        var bestDistance = 0f;
        foreach (var point in points)
        {
            var distance = Vector3.DistanceSquared(point, origin);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                farthest = point;
            }
        }

        if (bestDistance < 1e-10f)
            return false;

        var axis = Vector3.Normalize(farthest - origin);
        var offLine = Vector3.Zero;
        var bestOffLine = 0f;
        foreach (var point in points)
        {
            var toPoint = point - origin;
            var rejected = toPoint - axis * Vector3.Dot(toPoint, axis);
            var distance = rejected.LengthSquared();
            if (distance > bestOffLine)
            {
                bestOffLine = distance;
                offLine = rejected;
            }
        }

        if (bestOffLine < 1e-10f)
            return false;

        var normal = Vector3.Normalize(Vector3.Cross(axis, offLine));
        var extent = MathF.Sqrt(bestDistance);
        foreach (var point in points)
        {
            if (MathF.Abs(Vector3.Dot(point - origin, normal)) > extent * 1e-4f)
                return true;
        }

        return false;
    }

    private void ReconcileJoints()
    {
        _seenJoints.Clear();

        foreach (var descriptor in _frameJoints)
        {
            if (!descriptor.Enabled || descriptor.BodyA == null)
                continue;

            var id = ResolveId(descriptor);
            var countA = InstanceCountOf(descriptor.BodyA);

            switch (descriptor.Mode)
            {
                case PhysicsJointMode.Chain:
                    for (var index = 0; index + 1 < countA; index++)
                    {
                        if (TryGetInstance(descriptor.BodyA, index, out var first) && TryGetInstance(descriptor.BodyA, index + 1, out var second))
                            ReconcileJoint(descriptor, DeriveId(id, index), first, second, JointAnchoring.Midpoint);
                    }

                    break;

                case PhysicsJointMode.Nearby:
                {
                    var links = NearbyLinksFor(descriptor, id, countA);
                    for (var index = 0; index < links.Count; index++)
                    {
                        var (indexA, indexB) = links[index];
                        if (TryGetInstance(descriptor.BodyA, indexA, out var first) && TryGetInstance(descriptor.BodyA, indexB, out var second))
                            ReconcileJoint(descriptor, DeriveId(DeriveId(id, indexA), indexB), first, second, JointAnchoring.Midpoint);
                    }

                    break;
                }

                case PhysicsJointMode.World:
                    if (descriptor.InstanceIndex >= 0)
                    {
                        var only = Math.Min(descriptor.InstanceIndex, countA - 1);
                        if (TryGetInstance(descriptor.BodyA, only, out var pinned))
                            ReconcileWorldJoint(descriptor, DeriveId(id, only), pinned);

                        break;
                    }

                    for (var index = 0; index < countA; index++)
                    {
                        if (TryGetInstance(descriptor.BodyA, index, out var body))
                            ReconcileWorldJoint(descriptor, DeriveId(id, index), body);
                    }

                    break;

                default:
                {
                    if (descriptor.BodyB == null)
                        continue;

                    // A single body on one side is shared by every instance of the other: many pendulums on one anchor.
                    var countB = InstanceCountOf(descriptor.BodyB);
                    var count = countA == 1 || countB == 1 ? Math.Max(countA, countB) : Math.Min(countA, countB);
                    var isInstanced = descriptor.BodyA.IsInstanced || descriptor.BodyB.IsInstanced;
                    var pairAnchoring = descriptor.AutoAnchor ? JointAnchoring.AtBodyB : JointAnchoring.Explicit;
                    if (descriptor.InstanceIndex >= 0)
                    {
                        // One joint only: each instanced side contributes the chosen instance, or its last one.
                        if (TryGetInstance(descriptor.BodyA, Math.Min(descriptor.InstanceIndex, countA - 1), out var onlyA)
                            && TryGetInstance(descriptor.BodyB, Math.Min(descriptor.InstanceIndex, countB - 1), out var onlyB))
                            ReconcileJoint(descriptor, isInstanced ? DeriveId(id, descriptor.InstanceIndex) : id, onlyA, onlyB, pairAnchoring);

                        break;
                    }

                    for (var index = 0; index < count; index++)
                    {
                        if (TryGetInstance(descriptor.BodyA, countA == 1 ? 0 : index, out var first)
                            && TryGetInstance(descriptor.BodyB, countB == 1 ? 0 : index, out var second))
                            ReconcileJoint(descriptor, isInstanced ? DeriveId(id, index) : id, first, second, pairAnchoring);
                    }

                    break;
                }
            }
        }

        _removeJoints.Clear();
        foreach (var pair in _joints)
        {
            if (!_seenJoints.Contains(pair.Key))
                _removeJoints.Add(pair.Key);
        }

        foreach (var id in _removeJoints)
        {
            RemoveJoint(_joints[id]);
            _joints.Remove(id);
        }

        _removeJoints.Clear();
        foreach (var pair in _nearbyLinks)
        {
            if (!pair.Value.Seen)
                _removeJoints.Add(pair.Key);

            pair.Value.Seen = false;
        }

        foreach (var id in _removeJoints)
            _nearbyLinks.Remove(id);
    }

    private static int InstanceCountOf(PhysicsBody body)
    {
        return body.IsInstanced ? Math.Min(body.InstanceCount, body.Instances.Length) : 1;
    }

    private bool TryGetInstance(PhysicsBody body, int index, out BodyEntry entry)
    {
        var id = ResolveId(body);
        return _bodies.TryGetValue(body.IsInstanced ? DeriveId(id, index) : id, out entry);
    }

    /// <summary>The pose a body was given by the graph, which stays put while the simulation moves the body away from it.</summary>
    private static RigidPose InputPose(BodyEntry entry)
    {
        return new RigidPose(entry.LastInputPosition + Vector3.Transform(entry.LocalCenter, entry.LastInputOrientation), entry.LastInputOrientation);
    }

    private void RemoveJoint(JointEntry joint)
    {
        _simulation.Solver.Remove(joint.Handle);
        if (joint.HasSecondHandle)
            _simulation.Solver.Remove(joint.SecondHandle);
    }

    private bool IsStaticJointPartner(BodyEntry body)
    {
        if (!body.IsStatic)
            return false;

        if (!_hasLoggedStaticJointWarning)
        {
            Log.Warning("PhysicsWorld: joints to static bodies are ignored. Use a Kinematic body as the anchor, or the joint's World mode.", _logSource);
            _hasLoggedStaticJointWarning = true;
        }

        return true;
    }

    /// <summary>Where a joint's anchors and axes come from.</summary>
    private enum JointAnchoring
    {
        /// <summary>The local offsets and axes of the description, as given.</summary>
        Explicit,

        /// <summary>Halfway between where the two bodies start, so a chain or a net holds its initial shape
        /// instead of pulling every pair of centres together. The axis is A's.</summary>
        Midpoint,

        /// <summary>At B's starting position, with B's axis: one description fits every B around a shared A.</summary>
        AtBodyB,
    }

    private void ReconcileJoint(PhysicsJoint descriptor, Guid id, BodyEntry bodyA, BodyEntry bodyB, JointAnchoring anchoring)
    {
        if (ReferenceEquals(bodyA, bodyB) || IsStaticJointPartner(bodyA) || IsStaticJointPartner(bodyB))
            return;

        _seenJoints.Add(id);
        var hash = JointHash(descriptor, bodyA.Handle, bodyB.Handle);
        if (anchoring != JointAnchoring.Explicit)
        {
            // Hashed by component: RigidPose is not IEquatable, so hashing it whole boxed both poses on every
            // joint and every frame.
            var poseA = InputPose(bodyA);
            var poseB = InputPose(bodyB);
            hash = HashCode.Combine(hash, poseA.Position, poseA.Orientation, poseB.Position, poseB.Orientation, anchoring);
        }

        if (_joints.TryGetValue(id, out var existing))
        {
            if (existing.Hash == hash)
                return;

            RemoveJoint(existing);
            _joints.Remove(id);
        }

        var handle = anchoring != JointAnchoring.Explicit
                         ? AddAnchoredConstraint(descriptor, bodyA, bodyB, anchoring)
                         : AddConstraint(descriptor, bodyA.Handle, bodyB.Handle);
        var entry = new JointEntry { Handle = handle, Hash = hash, BodyA = bodyA.Handle, BodyB = bodyB.Handle, HasBodyB = true };
        AddMotorOrLimit(descriptor, bodyA, bodyB, anchoring, entry);
        _joints.Add(id, entry);
    }

    /// <summary>The joint axis in each body's local space, for the chosen anchoring.</summary>
    private static void JointAxes(PhysicsJoint descriptor, BodyEntry bodyA, BodyEntry bodyB, JointAnchoring anchoring,
                                  out Vector3 axisA, out Vector3 axisB)
    {
        axisA = NormalizeAxis(descriptor.HingeAxisA);
        axisB = NormalizeAxis(descriptor.HingeAxisB);
        if (anchoring == JointAnchoring.Explicit)
            return;

        // One body's axis is taken as given and carried over to the other through the starting poses,
        // so the pair starts unstressed.
        var inverseA = Quaternion.Conjugate(InputPose(bodyA).Orientation);
        var inverseB = Quaternion.Conjugate(InputPose(bodyB).Orientation);
        if (anchoring == JointAnchoring.AtBodyB)
            axisA = NormalizeAxis(Vector3.Transform(Vector3.Transform(axisB, InputPose(bodyB).Orientation), inverseA));
        else
            axisB = NormalizeAxis(Vector3.Transform(Vector3.Transform(axisA, InputPose(bodyA).Orientation), inverseB));
    }

    /// <summary>A motor or a swing limit is a second Bepu constraint on the same pair, around the same axes as the joint itself.</summary>
    private void AddMotorOrLimit(PhysicsJoint descriptor, BodyEntry bodyA, BodyEntry bodyB, JointAnchoring anchoring, JointEntry entry)
    {
        JointAxes(descriptor, bodyA, bodyB, anchoring, out var axisA, out var axisB);
        var motorSpeed = FiniteOr(descriptor.MotorSpeed, 0f);
        if (descriptor.Type == PhysicsJointType.Hinge && motorSpeed != 0f)
        {
            entry.SecondHandle = _simulation.Solver.Add(bodyA.Handle, bodyB.Handle, new AngularAxisMotor
                                                                                        {
                                                                                            LocalAxisA = axisA,
                                                                                            TargetVelocity = motorSpeed,
                                                                                            Settings = new MotorSettings(float.MaxValue, MotorSoftness),
                                                                                        });
            entry.HasSecondHandle = true;
            return;
        }

        var swingLimit = FiniteOr(descriptor.SwingLimit, 0f);
        if (descriptor.Type == PhysicsJointType.BallSocket && swingLimit > 0f)
        {
            entry.SecondHandle = _simulation.Solver.Add(bodyA.Handle, bodyB.Handle, new SwingLimit
                                                                                        {
                                                                                            AxisLocalA = axisA,
                                                                                            AxisLocalB = axisB,
                                                                                            MaximumSwingAngle = Math.Clamp(swingLimit, 0.5f, 179f) * (MathF.PI / 180f),
                                                                                            SpringSettings = SpringFor(descriptor),
                                                                                        });
            entry.HasSecondHandle = true;
        }
    }

    private void ReconcileWorldJoint(PhysicsJoint descriptor, Guid id, BodyEntry body)
    {
        if (IsStaticJointPartner(body))
            return;

        _seenJoints.Add(id);
        var pose = InputPose(body);
        var hash = HashCode.Combine(JointHash(descriptor, body.Handle, body.Handle), pose.Position, pose.Orientation, 0x57_4F_52);

        if (_joints.TryGetValue(id, out var existing))
        {
            if (existing.Hash == hash)
                return;

            RemoveJoint(existing);
            _joints.Remove(id);
        }

        var spring = SpringFor(descriptor);
        var entry = new JointEntry
                        {
                            Hash = hash,
                            BodyA = body.Handle,
                            Handle = _simulation.Solver.Add(body.Handle, new OneBodyLinearServo
                                                                             {
                                                                                 LocalOffset = descriptor.LocalOffsetA,
                                                                                 Target = pose.Position + Vector3.Transform(descriptor.LocalOffsetA, pose.Orientation),
                                                                                 SpringSettings = spring,
                                                                                 ServoSettings = ServoSettings.Default,
                                                                             }),
                        };

        // Holding the anchor point leaves the body free to swing; a weld also pins its orientation.
        if (descriptor.Type == PhysicsJointType.Weld)
        {
            entry.SecondHandle = _simulation.Solver.Add(body.Handle, new OneBodyAngularServo
                                                                         {
                                                                             TargetOrientation = pose.Orientation,
                                                                             SpringSettings = spring,
                                                                             ServoSettings = ServoSettings.Default,
                                                                         });
            entry.HasSecondHandle = true;
        }

        _joints.Add(id, entry);
    }

    private ConstraintHandle AddAnchoredConstraint(PhysicsJoint descriptor, BodyEntry bodyA, BodyEntry bodyB, JointAnchoring anchoring)
    {
        var spring = SpringFor(descriptor);
        var poseA = InputPose(bodyA);
        var poseB = InputPose(bodyB);
        var inverseA = Quaternion.Conjugate(poseA.Orientation);
        var inverseB = Quaternion.Conjugate(poseB.Orientation);
        var anchor = anchoring == JointAnchoring.AtBodyB
                         ? poseB.Position + Vector3.Transform(descriptor.LocalOffsetB, poseB.Orientation)
                         : (poseA.Position + poseB.Position) * 0.5f;
        var localAnchorA = Vector3.Transform(anchor - poseA.Position, inverseA);
        var localAnchorB = Vector3.Transform(anchor - poseB.Position, inverseB);

        switch (descriptor.Type)
        {
            case PhysicsJointType.Hinge:
            {
                JointAxes(descriptor, bodyA, bodyB, anchoring, out var localAxisA, out var localAxisB);
                return _simulation.Solver.Add(bodyA.Handle, bodyB.Handle, new Hinge
                                                                              {
                                                                                  LocalOffsetA = localAnchorA,
                                                                                  LocalHingeAxisA = localAxisA,
                                                                                  LocalOffsetB = localAnchorB,
                                                                                  LocalHingeAxisB = localAxisB,
                                                                                  SpringSettings = spring,
                                                                              });
            }

            case PhysicsJointType.Weld:
                return _simulation.Solver.Add(bodyA.Handle, bodyB.Handle, new Weld
                                                                              {
                                                                                  LocalOffset = Vector3.Transform(poseB.Position - poseA.Position, inverseA),
                                                                                  LocalOrientation = Quaternion.Normalize(inverseA * poseB.Orientation),
                                                                                  SpringSettings = spring,
                                                                              });

            case PhysicsJointType.DistanceServo:
            {
                var distance = descriptor.TargetDistance > 0f ? descriptor.TargetDistance : Vector3.Distance(poseA.Position, poseB.Position);
                return _simulation.Solver.Add(bodyA.Handle, bodyB.Handle, new DistanceServo(Vector3.Zero, Vector3.Zero, distance, spring));
            }

            default:
                return _simulation.Solver.Add(bodyA.Handle, bodyB.Handle, new BallSocket
                                                                              {
                                                                                  LocalOffsetA = localAnchorA,
                                                                                  LocalOffsetB = localAnchorB,
                                                                                  SpringSettings = spring,
                                                                              });
        }
    }

    /// <summary>
    /// Pairs of instances that start closer than the link distance. Found with a uniform grid and cached until the
    /// instances or the distance change, because the search is the only part of a joint that is not O(joints).
    /// </summary>
    private List<(int, int)> NearbyLinksFor(PhysicsJoint descriptor, Guid id, int count)
    {
        var distance = float.IsFinite(descriptor.LinkDistance) ? MathF.Max(0f, descriptor.LinkDistance) : 0f;
        var hash = new HashCode();
        hash.Add(ResolveId(descriptor.BodyA));
        hash.Add(count);
        hash.Add(distance);
        for (var index = 0; index < count; index++)
        {
            if (TryGetInstance(descriptor.BodyA, index, out var entry))
                hash.Add(InputPose(entry).Position);
        }

        var hashCode = hash.ToHashCode();
        if (!_nearbyLinks.TryGetValue(id, out var cache))
        {
            cache = new NearbyLinks();
            _nearbyLinks.Add(id, cache);
        }

        cache.Seen = true;
        if (cache.Hash == hashCode && cache.IsValid)
            return cache.Pairs;

        cache.Hash = hashCode;
        cache.IsValid = true;
        cache.Pairs.Clear();
        if (distance <= 0f || count < 2)
            return cache.Pairs;

        _linkPositions.Clear();
        _linkCells.Clear();
        for (var index = 0; index < count; index++)
        {
            var hasBody = TryGetInstance(descriptor.BodyA, index, out var entry);
            var position = hasBody ? InputPose(entry).Position : new Vector3(float.NaN);
            _linkPositions.Add(position);
            if (!hasBody || !IsFinite(position))
                continue;

            var cell = CellOf(position, distance);
            if (!_linkCells.TryGetValue(cell, out var members))
            {
                members = new List<int>();
                _linkCells.Add(cell, members);
            }

            members.Add(index);
        }

        var distanceSquared = distance * distance;
        for (var index = 0; index < count && cache.Pairs.Count < MaxNearbyLinks; index++)
        {
            var position = _linkPositions[index];
            if (!IsFinite(position))
                continue;

            var (cellX, cellY, cellZ) = CellOf(position, distance);
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            for (var z = -1; z <= 1; z++)
            {
                if (!_linkCells.TryGetValue((cellX + x, cellY + y, cellZ + z), out var members))
                    continue;

                foreach (var other in members)
                {
                    // Each pair once, in index order, so the result does not depend on dictionary order.
                    if (other <= index || Vector3.DistanceSquared(position, _linkPositions[other]) > distanceSquared)
                        continue;

                    if (cache.Pairs.Count < MaxNearbyLinks)
                        cache.Pairs.Add((index, other));
                }
            }
        }

        if (cache.Pairs.Count >= MaxNearbyLinks && !_hasLoggedLinkLimitWarning)
        {
            Log.Warning($"PhysicsWorld: a Nearby joint found more than {MaxNearbyLinks} links; the rest are dropped. Lower LinkDistance.", _logSource);
            _hasLoggedLinkLimitWarning = true;
        }

        cache.Pairs.Sort();
        return cache.Pairs;
    }

    private static (int, int, int) CellOf(Vector3 position, float cellSize)
    {
        return ((int)MathF.Floor(position.X / cellSize), (int)MathF.Floor(position.Y / cellSize), (int)MathF.Floor(position.Z / cellSize));
    }

    private static SpringSettings SpringFor(PhysicsJoint descriptor)
    {
        return new SpringSettings(Math.Clamp(FiniteOr(descriptor.Frequency, 30f), 0.1f, 120f),
                                  Math.Clamp(FiniteOr(descriptor.DampingRatio, 1f), 0.01f, 2f));
    }

    private ConstraintHandle AddConstraint(PhysicsJoint descriptor, BodyHandle bodyA, BodyHandle bodyB)
    {
        var spring = SpringFor(descriptor);

        switch (descriptor.Type)
        {
            case PhysicsJointType.Hinge:
                return _simulation.Solver.Add(bodyA, bodyB, new Hinge
                                                                  {
                                                                      LocalOffsetA = descriptor.LocalOffsetA,
                                                                      LocalHingeAxisA = NormalizeAxis(descriptor.HingeAxisA),
                                                                      LocalOffsetB = descriptor.LocalOffsetB,
                                                                      LocalHingeAxisB = NormalizeAxis(descriptor.HingeAxisB),
                                                                      SpringSettings = spring,
                                                                  });

            case PhysicsJointType.Weld:
                return _simulation.Solver.Add(bodyA, bodyB, new Weld
                                                                  {
                                                                      LocalOffset = descriptor.LocalOffsetB,
                                                                      LocalOrientation = Quaternion.Identity,
                                                                      SpringSettings = spring,
                                                                  });

            case PhysicsJointType.DistanceServo:
                return _simulation.Solver.Add(bodyA, bodyB,
                                              new DistanceServo(descriptor.LocalOffsetA, descriptor.LocalOffsetB,
                                                                descriptor.TargetDistance, spring));

            default:
                return _simulation.Solver.Add(bodyA, bodyB, new BallSocket
                                                                  {
                                                                      LocalOffsetA = descriptor.LocalOffsetA,
                                                                      LocalOffsetB = descriptor.LocalOffsetB,
                                                                      SpringSettings = spring,
                                                                  });
        }
    }

    private void Step(float dt)
    {
        foreach (var entry in _bodies.Values)
            entry.PreviousPose = entry.CurrentPose;

        ApplyDampingAndGravityScale(dt);
        ApplyForces(dt);
        _simulation.Timestep(dt, _multithreaded ? _dispatcher : null);
        ApplyBounce();
        CollectContacts();

        foreach (var entry in _bodies.Values)
            ReadPose(entry);

        _simTime += dt;
    }

    /// <summary>Moves the impacts recorded by the worker threads during the last step into the frame's contact list.</summary>
    private void CollectContacts()
    {
        var start = _contacts.Count;
        _currentContactPairs.Clear();
        foreach (var requests in _contactRequests)
        {
            foreach (var contact in requests)
            {
                // A fast pair can still be closing in the step after it was first seen. It stays in the
                // set for as long as it keeps closing, so one impact - or one long push - is one contact.
                _currentContactPairs.Add(contact.PairKey);
                if (_previousContactPairs.Contains(contact.PairKey))
                    continue;

                if (_contacts.Count < MaxContactsPerUpdate)
                    _contacts.Add(contact);
            }

            requests.Clear();
        }

        (_previousContactPairs, _currentContactPairs) = (_currentContactPairs, _previousContactPairs);

        // Workers finish in any order; sorting the step's slice keeps the output stable between runs.
        if (_contacts.Count - start > 1)
            _contacts.Sort(start, _contacts.Count - start, ContactComparer.Instance);
    }

    /// <summary>
    /// Splits the frame's forces into continuous ones, applied every step, and impulses, applied once right here.
    /// An impulse fires when its trigger count differs from the one seen last frame; the first sighting only records it,
    /// so loading a graph or rebuilding the world never fires by itself.
    /// </summary>
    private void PrepareForces(bool paused)
    {
        _continuousForces.Clear();
        _impulseFrame++;
        if (_frameForces == null || _simulation == null)
            return;

        foreach (var force in _frameForces)
        {
            if (force == null)
                continue;

            if (!force.IsImpulse)
            {
                if (force.Enabled)
                    _continuousForces.Add(force);

                continue;
            }

            var id = ResolveId(force);
            var hasFired = _impulseCounts.TryGetValue(id, out var seen) && seen.TriggerCount != force.TriggerCount;
            _impulseCounts[id] = (force.TriggerCount, _impulseFrame);
            if (hasFired && force.Enabled && !paused)
                ApplyForce(force, 1f);
        }

        // Entries of forces that left the frame would otherwise accumulate forever, and the old blanket
        // Clear() past 4096 entries silenced every impulse once stale ids piled up. Drop what this frame did not see.
        foreach (var pair in _impulseCounts)
        {
            if (pair.Value.LastFrame != _impulseFrame)
                _staleImpulseIds.Add(pair.Key);
        }

        foreach (var id in _staleImpulseIds)
            _impulseCounts.Remove(id);

        _staleImpulseIds.Clear();
    }

    private void ApplyForces(float dt)
    {
        foreach (var force in _continuousForces)
            ApplyForce(force, dt);
    }

    /// <summary>Adds the velocity change of one force to every dynamic body it reaches. <paramref name="dt"/> is 1 for an impulse.</summary>
    private void ApplyForce(PhysicsForce force, float dt)
    {
        var strength = FiniteOr(force.Strength, 0f);
        if (strength == 0f || (force.Type == PhysicsForceType.Field && force.Field == null))
            return;

        var radius = FiniteOr(force.Radius, 0f);
        var falloff = Math.Clamp(FiniteOr(force.Falloff, 1f), 0f, 1f);
        var axis = NormalizeAxis(IsFinite(force.Direction) ? force.Direction : Vector3.UnitY);
        var origin = IsFinite(force.Position) ? force.Position : Vector3.Zero;

        foreach (var entry in _bodies.Values)
        {
            if (entry.IsStatic || entry.Kind != PhysicsBodyKind.Dynamic)
                continue;

            if (force.CollisionGroup != 0 && entry.Group != force.CollisionGroup)
                continue;

            var reference = _simulation.Bodies[entry.Handle];
            var position = reference.Pose.Position;
            Vector3 acceleration;

            switch (force.Type)
            {
                case PhysicsForceType.Radial:
                {
                    var toCenter = origin - position;
                    var distance = toCenter.Length();
                    if (distance < 1e-5f)
                        continue;

                    acceleration = toCenter / distance * (strength * Reach(distance, radius, falloff));
                    break;
                }

                case PhysicsForceType.Vortex:
                {
                    var fromAxis = position - origin;
                    fromAxis -= axis * Vector3.Dot(fromAxis, axis);
                    var distance = fromAxis.Length();
                    if (distance < 1e-5f)
                        continue;

                    acceleration = Vector3.Cross(axis, fromAxis / distance) * (strength * Reach(distance, radius, falloff));
                    break;
                }

                case PhysicsForceType.Field:
                    try
                    {
                        acceleration = force.Field.Sample(position) * strength;
                    }
                    catch (Exception e)
                    {
                        // A field is graph code; letting it throw here would tear down the whole world.
                        if (!_hasLoggedFieldForceError)
                        {
                            Log.Warning($"PhysicsWorld: a field force failed and is skipped: {e.Message}", _logSource);
                            _hasLoggedFieldForceError = true;
                        }

                        return;
                    }

                    break;

                default:
                    acceleration = axis * strength;
                    break;
            }

            if (force.ScaleByMass)
                acceleration *= reference.LocalInertia.InverseMass;

            var change = acceleration * dt;
            if (!IsFinite(change) || change.LengthSquared() < 1e-12f)
                continue;

            // A resting body ignores velocity changes until something wakes it; only a push that could
            // actually move it does, so a faint breeze does not keep the whole pile awake.
            if (!reference.Awake)
            {
                if (acceleration.LengthSquared() < MinWakeAcceleration * MinWakeAcceleration && dt < 1f)
                    continue;

                reference.Awake = true;
            }

            reference.Velocity.Linear += change;
        }
    }

    /// <summary>Strength multiplier at a distance from the force: 1 inside, fading by <paramref name="falloff"/> towards the radius, 0 beyond it.</summary>
    private static float Reach(float distance, float radius, float falloff)
    {
        if (radius <= 0f)
            return 1f;

        return distance >= radius ? 0f : 1f - falloff * distance / radius;
    }

    /// <summary>
    /// Adds the missing restitution impulse to pairs that were closing fast and that the solver has just
    /// brought to rest, so slow resting contacts and bodies still approaching are left alone.
    /// </summary>
    private void ApplyBounce()
    {
        _pendingBounces.Clear();
        foreach (var requests in _bounceRequests)
        {
            _pendingBounces.AddRange(requests);
            requests.Clear();
        }

        // Worker threads record in whatever order they finish; each request reads the velocities the previous ones
        // left behind, so a fixed order is what keeps Deterministic replays identical.
        if (_pendingBounces.Count > 1)
            _pendingBounces.Sort(CompareBounceRequests);

        ApplyPendingBounces();
    }

    private void ApplyPendingBounces()
    {
        for (var index = 0; index < _pendingBounces.Count; index++)
        {
            var request = _pendingBounces[index];
            var inverseMassA = 0f;
            var inverseMassB = 0f;
            var velocityA = Vector3.Zero;
            var velocityB = Vector3.Zero;

            if (request.BodyA != NoBody)
            {
                var body = _simulation.Bodies[new BodyHandle(request.BodyA)];
                velocityA = body.Velocity.Linear;
                inverseMassA = body.LocalInertia.InverseMass;
            }

            if (request.BodyB != NoBody)
            {
                var body = _simulation.Bodies[new BodyHandle(request.BodyB)];
                velocityB = body.Velocity.Linear;
                inverseMassB = body.LocalInertia.InverseMass;
            }

            var approachSpeed = Vector3.Dot(velocityA - velocityB, request.Normal);
            var wasStopped = approachSpeed < request.ApproachSpeed * 0.5f;
            var missingSeparation = request.Bounciness * request.ApproachSpeed + approachSpeed;
            var totalInverseMass = inverseMassA + inverseMassB;
            if (!wasStopped || missingSeparation <= 0f || totalInverseMass <= 0f)
                continue;

            var impulse = request.Normal * (missingSeparation / totalInverseMass);
            if (inverseMassA > 0f)
            {
                var body = _simulation.Bodies[new BodyHandle(request.BodyA)];
                body.Velocity.Linear -= impulse * inverseMassA;
                body.Awake = true;
            }

            if (inverseMassB > 0f)
            {
                var body = _simulation.Bodies[new BodyHandle(request.BodyB)];
                body.Velocity.Linear += impulse * inverseMassB;
                body.Awake = true;
            }
        }
    }

    private static int CompareBounceRequests(BounceRequest a, BounceRequest b)
    {
        return a.BodyA != b.BodyA ? a.BodyA.CompareTo(b.BodyA) : a.BodyB.CompareTo(b.BodyB);
    }

    private void ApplyDampingAndGravityScale(float dt)
    {
        foreach (var entry in _bodies.Values)
        {
            if (entry.IsStatic || entry.Kind != PhysicsBodyKind.Dynamic)
                continue;

            var reference = _simulation.Bodies[entry.Handle];
            if (!reference.Awake)
                continue;

            var velocity = reference.Velocity;
            var changed = false;

            if (entry.LinearDamping > 0)
            {
                velocity.Linear *= 1f / (1f + entry.LinearDamping * dt);
                changed = true;
            }

            if (entry.AngularDamping > 0)
            {
                velocity.Angular *= 1f / (1f + entry.AngularDamping * dt);
                changed = true;
            }

            // The pose integrator applies full gravity; this corrects the difference for bodies
            // with a custom scale. Applied as a velocity delta so sleeping bodies keep sleeping.
            if (MathF.Abs(entry.GravityScale - 1f) > 0.0001f)
            {
                velocity.Linear += _settings.Gravity * (entry.GravityScale - 1f) * dt;
                changed = true;
            }

            if (changed)
                reference.Velocity = velocity;
        }
    }

    private void ReadPose(BodyEntry entry)
    {
        if (entry.IsStatic)
        {
            entry.CurrentPose = entry.StaticPose;
            return;
        }

        var reference = _simulation.Bodies[entry.Handle];
        entry.CurrentPose = reference.Pose;
        entry.Speed = reference.Velocity.Linear.Length();
    }

    /// <summary>Sets a kinematic body's velocity so it reaches <paramref name="target"/> in <paramref name="seconds"/> of simulated time.</summary>
    private void TrackKinematicPose(BodyEntry entry, RigidPose target, float seconds)
    {
        var reference = _simulation.Bodies[entry.Handle];
        var current = reference.Pose;
        var offset = target.Position - current.Position;

        // A jump no body could make in one frame is a cut in the animation, not a motion: place the body instead.
        if (offset.LengthSquared() > KinematicTeleportDistance * KinematicTeleportDistance)
        {
            reference.Velocity = default;
            ApplyPose(entry, target);
            return;
        }

        var turn = target.Orientation * Quaternion.Conjugate(current.Orientation);
        if (turn.W < 0f)
            turn = Quaternion.Negate(turn);

        var angular = Vector3.Zero;
        var sinHalf = new Vector3(turn.X, turn.Y, turn.Z).Length();
        if (sinHalf > 1e-6f)
        {
            var angle = 2f * MathF.Atan2(sinHalf, turn.W);
            angular = new Vector3(turn.X, turn.Y, turn.Z) / sinHalf * (angle / seconds);
        }

        var linear = offset / seconds;
        if (linear == Vector3.Zero && angular == Vector3.Zero && reference.Velocity.Linear == Vector3.Zero && reference.Velocity.Angular == Vector3.Zero)
            return;

        reference.Velocity = new BodyVelocity(linear, angular);
        reference.Awake = true;
    }

    private void ApplyPose(BodyEntry entry, RigidPose pose)
    {
        if (entry.IsStatic)
        {
            var description = _simulation.Statics.GetDescription(entry.StaticHandle);
            description.Pose = pose;
            _simulation.Statics.ApplyDescription(entry.StaticHandle, description);
            entry.StaticPose = pose;
        }
        else
        {
            var reference = _simulation.Bodies[entry.Handle];
            reference.Pose = pose;
            reference.UpdateBounds();
            reference.Awake = true;
        }

        // A teleport should snap, not smear across one interpolation interval.
        entry.PreviousPose = pose;
        entry.CurrentPose = pose;
    }

    private void RemoveBody(BodyEntry entry)
    {
        // Bodies.Remove removes every constraint attached to the body, which invalidates the handles
        // cached in _joints: forget exactly the joints touching this body so the next reconcile re-adds
        // them, and leave unrelated joints - and their warm-started solver state - untouched. Joints
        // ignore static partners, so a static body has nothing to forget.
        if (!entry.IsStatic)
        {
            foreach (var pair in _joints)
            {
                if (pair.Value.Touches(entry.Handle))
                    _removeJoints.Add(pair.Key);
            }

            foreach (var id in _removeJoints)
            {
                RemoveJoint(_joints[id]);
                _joints.Remove(id);
            }

            _removeJoints.Clear();
        }

        if (entry.IsStatic)
        {
            _simulation.Statics.Remove(entry.StaticHandle);
        }
        else
        {
            // Bodies.Remove also removes every constraint attached to the body, which
            // invalidates the handles cached in _joints.
            _simulation.Bodies.Remove(entry.Handle);
        }

        if (entry.SharedShape.HasValue)
        {
            // Hulls and meshes are shared between instances; the shape goes with its last user.
            var key = entry.SharedShape.Value;
            if (_sharedShapes.TryGetValue(key, out var shared) && --shared.ReferenceCount <= 0)
            {
                if (shared.OwnPool != null)
                {
                    _simulation.Shapes.Remove(shared.Index);
                    shared.OwnPool.Clear();
                }
                else if (shared.IsCompound)
                {
                    _simulation.Shapes.RecursivelyRemoveAndDispose(shared.Index, _pool);
                }
                else
                {
                    _simulation.Shapes.RemoveAndDispose(shared.Index, _pool);
                }

                _sharedShapes.Remove(key);
            }

            return;
        }

        if (entry.ShapeIndex.Exists)
            _simulation.Shapes.RemoveAndDispose(entry.ShapeIndex, _pool);
    }

    /// <summary>Rotation that turns +Z onto the given direction, so a mesh drawn at a contact points along its normal.</summary>
    private static Quaternion OrientationAlong(Vector3 direction)
    {
        var lengthSquared = direction.LengthSquared();
        if (lengthSquared < 1e-8f || !IsFinite(direction))
            return Quaternion.Identity;

        direction /= MathF.Sqrt(lengthSquared);
        var dot = Vector3.Dot(Vector3.UnitZ, direction);
        if (dot > 0.9999f)
            return Quaternion.Identity;

        if (dot < -0.9999f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, direction));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(dot, -1f, 1f)));
    }

    private static RigidPose InterpolatePose(in RigidPose from, in RigidPose to, float t)
    {
        if (t <= 0f)
            return from;

        if (t >= 1f)
            return to;

        return new RigidPose(Vector3.Lerp(from.Position, to.Position, t),
                             Quaternion.Normalize(Quaternion.Slerp(from.Orientation, to.Orientation, t)));
    }

    /// <summary>Replaces a NaN or infinite input with the operator's default; graphs can produce both easily.</summary>
    private static float FiniteOr(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    private static bool IsFinite(in Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Quaternion NormalizeOrientation(Quaternion orientation)
    {
        var lengthSquared = orientation.LengthSquared();
        return !float.IsFinite(lengthSquared) || lengthSquared < 1e-8f ? Quaternion.Identity : Quaternion.Normalize(orientation);
    }

    private static Vector3 NormalizeAxis(Vector3 axis)
    {
        var lengthSquared = axis.LengthSquared();
        return lengthSquared < 1e-8f ? Vector3.UnitY : Vector3.Normalize(axis);
    }

    private static Vector3 RenderScaleFor(PhysicsBody descriptor)
    {
        switch (descriptor.Shape)
        {
            case PhysicsShapeType.Sphere:
                return new Vector3(descriptor.Radius * 2f);
            case PhysicsShapeType.Capsule:
                return new Vector3(descriptor.Radius * 2f, descriptor.Length + descriptor.Radius * 2f, descriptor.Radius * 2f);
            case PhysicsShapeType.Cylinder:
                return new Vector3(descriptor.Radius * 2f, descriptor.Length, descriptor.Radius * 2f);
            default:
                // Also the hull and mesh fallback: without usable geometry the body is the box it is drawn as.
                return descriptor.Size;
        }
    }

    private static int JointHash(PhysicsJoint descriptor, BodyHandle a, BodyHandle b)
    {
        var hash = new HashCode();
        hash.Add(descriptor.Type);
        hash.Add(a.Value);
        hash.Add(b.Value);
        hash.Add(descriptor.LocalOffsetA);
        hash.Add(descriptor.LocalOffsetB);
        hash.Add(descriptor.HingeAxisA);
        hash.Add(descriptor.HingeAxisB);
        hash.Add(descriptor.Frequency);
        hash.Add(descriptor.DampingRatio);
        hash.Add(descriptor.TargetDistance);
        hash.Add(descriptor.Mode);
        hash.Add(descriptor.MotorSpeed);
        hash.Add(descriptor.SwingLimit);
        hash.Add(descriptor.AutoAnchor);
        return hash.ToHashCode();
    }

    private Guid ResolveId(PhysicsBody descriptor)
    {
        if (descriptor.Id != Guid.Empty)
            return descriptor.Id;

        // Defensive: a body built outside an operator has no id. Key it by reference so it
        // survives frames instead of being recreated every evaluation. The table holds the key
        // weakly, so a descriptor that leaves the graph can be collected instead of pinning
        // entry forever - a plain dictionary keyed by reference would leak one entry per body.
        return _implicitIds.GetValue(descriptor, static _ => new IdBox { Id = Guid.NewGuid() }).Id;
    }

    private Guid ResolveId(PhysicsJoint descriptor)
    {
        if (descriptor.Id != Guid.Empty)
            return descriptor.Id;

        return _implicitJointIds.GetValue(descriptor, static _ => new IdBox { Id = Guid.NewGuid() }).Id;
    }

    private Guid ResolveId(PhysicsForce descriptor)
    {
        if (descriptor.Id != Guid.Empty)
            return descriptor.Id;

        return _implicitForceIds.GetValue(descriptor, static _ => new IdBox { Id = Guid.NewGuid() }).Id;
    }

    /// <summary>What turns a shared description into one particular body.</summary>
    private readonly struct InstanceInfo(Vector3 scale, Vector4 color, PhysicsHull hull)
    {
        public readonly Vector3 Scale = scale;
        public readonly Vector4 Color = color;
        public readonly PhysicsHull Hull = hull;
    }

    /// <summary>Identifies a hull or mesh shape: the source data by reference (producers replace it, never mutate it) and the scale baked into it.</summary>
    private readonly struct SharedShapeKey(object source, Vector3 scale, bool isMesh) : IEquatable<SharedShapeKey>
    {
        private readonly object _source = source;
        private readonly Vector3 _scale = scale;
        private readonly bool _isMesh = isMesh;

        public bool Equals(SharedShapeKey other) => ReferenceEquals(_source, other._source) && _scale == other._scale && _isMesh == other._isMesh;

        public override bool Equals(object obj) => obj is SharedShapeKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(_source), _scale, _isMesh);
    }

    private sealed class SharedShape
    {
        public TypedIndex Index;
        public Vector3 Center;
        public BodyInertia UnitInertia;
        public BodyActivityDescription Activity;
        public int ReferenceCount;

        /** Volume of the scaled shape, which density-based mass is taken from; 0 for meshes. */
        public float Volume;

        /** Compound children are separate shapes that have to be released with the compound. */
        public bool IsCompound;

        /** The pool a mesh built in the background was allocated from, released with the shape; null for the simulation's pool. */
        public BufferPool OwnPool;
    }

    private sealed record MeshBuild(BufferPool Pool, Task<Mesh?> Task)
    {
        /** How often this mesh build was re-queued after a fault. */
        public int Attempts;
    }

    private sealed class NearbyLinks
    {
        public readonly List<(int, int)> Pairs = new();
        public int Hash;
        public bool IsValid;
        public bool Seen;
    }

    private sealed class ContactComparer : IComparer<PhysicsContact>
    {
        public static readonly ContactComparer Instance = new();

        public int Compare(PhysicsContact a, PhysicsContact b)
        {
            var result = a.Position.X.CompareTo(b.Position.X);
            if (result != 0)
                return result;

            result = a.Position.Y.CompareTo(b.Position.Y);
            if (result != 0)
                return result;

            result = a.Position.Z.CompareTo(b.Position.Z);
            return result != 0 ? result : a.Speed.CompareTo(b.Speed);
        }
    }

    private sealed class IdBox
    {
        public Guid Id;
    }

    private readonly struct ShapeKey : IEquatable<ShapeKey>
    {
        private readonly PhysicsShapeType _shape;
        private readonly PhysicsBodyKind _kind;
        private readonly Vector3 _size;
        private readonly float _radius;
        private readonly float _length;
        private readonly float _mass;
        private readonly float _density;

        // Hull and mesh bodies: the geometry they were built from and the scale baked into the shape.
        private readonly object _geometry;
        private readonly Vector3 _scale;

        private ShapeKey(PhysicsBody descriptor, in InstanceInfo info)
        {
            _shape = descriptor.Shape;
            _kind = descriptor.Kind;
            _size = descriptor.Size;
            _radius = descriptor.Radius;
            _length = descriptor.Length;
            _mass = descriptor.Kind == PhysicsBodyKind.Dynamic ? descriptor.Mass : 0f;
            _density = descriptor.Kind == PhysicsBodyKind.Dynamic && descriptor.MassFromDensity ? descriptor.Density : 0f;

            switch (EffectiveShape(descriptor))
            {
                case PhysicsShapeType.ConvexHull:
                    _geometry = info.Hull ?? (object)descriptor.MeshTriangles;
                    _scale = info.Scale;
                    break;

                case PhysicsShapeType.Mesh:
                    _geometry = descriptor.MeshTriangles;
                    _scale = info.Scale;
                    break;

                default:
                    _geometry = null;
                    _scale = default;
                    break;
            }
        }

        public static ShapeKey From(PhysicsBody descriptor, in InstanceInfo info) => new(descriptor, info);

        public bool Equals(ShapeKey other)
        {
            return _shape == other._shape
                   && _kind == other._kind
                   && _size == other._size
                   && _radius.Equals(other._radius)
                   && _length.Equals(other._length)
                   && _mass.Equals(other._mass)
                   && _density.Equals(other._density)
                   && ReferenceEquals(_geometry, other._geometry)
                   && _scale == other._scale;
        }

        public override bool Equals(object obj) => obj is ShapeKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)_shape, (int)_kind, _size, _radius, _length, _mass, _scale);
    }

    private sealed class BodyEntry
    {
        /** The mesh this placeholder body is waiting for; set while its collider builds in the background. */
        public SharedShapeKey? PendingMesh;

        public Guid Id;
        public PhysicsBodyKind Kind;
        public bool IsStatic;
        public BodyHandle Handle;
        public StaticHandle StaticHandle;
        public TypedIndex ShapeIndex;
        public ShapeKey ShapeKey;

        public RigidPose PreviousPose;
        public RigidPose CurrentPose;
        public RigidPose StaticPose;

        public Vector3 LastInputPosition;
        public Quaternion LastInputOrientation;
        public Vector3 LastInputLinearVelocity;
        public Vector3 LastInputAngularVelocity;

        public Vector3 RenderScale = Vector3.One;
        public float Friction;
        public float Bounciness;
        public float LinearDamping;
        public float AngularDamping;
        public float GravityScale = 1f;
        public int Group;
        public Vector4 Color = Vector4.One;

        /// <summary>Linear speed at the latest fixed step, published in the point's F2.</summary>
        public float Speed;

        /// <summary>Centre of mass relative to the pivot the body is published at. Zero for primitives and meshes.</summary>
        public Vector3 LocalCenter;

        /// <summary>Set when the shape is shared with other bodies and must be released through the shared table.</summary>
        public SharedShapeKey? SharedShape;
    }

    private sealed class JointEntry
    {
        public ConstraintHandle Handle;

        /// <summary>A world weld is two Bepu constraints: one holds the position, one the orientation.</summary>
        public ConstraintHandle SecondHandle;

        public bool HasSecondHandle;
        public int Hash;

        /** The bodies the constraints were created against; RemoveBody tears down only joints touching the removed body. */
        public BodyHandle BodyA;
        public BodyHandle BodyB;
        public bool HasBodyB;

        /** True when one of the joint's constraints was created against this body handle. */
        public bool Touches(BodyHandle handle) => BodyA.Value == handle.Value || HasBodyB && BodyB.Value == handle.Value;
    }

    private readonly struct PhysicsMaterial
    {
        public readonly float Friction;
        public readonly float Bounciness;

        /// <summary>Collision group; two collidables with the same non-zero group pass through each other.</summary>
        public readonly int Group;

        public PhysicsMaterial(float friction, float bounciness, int group = 0)
        {
            Friction = Math.Clamp(friction, 0f, 4f);
            Bounciness = Math.Clamp(bounciness, 0f, 1f);
            Group = group;
        }
    }

    /// <summary>
    /// Per-handle materials read from the narrow-phase callback. Handles are dense, so arrays
    /// are cheaper than dictionaries on the contact path. Only written between timesteps.
    /// </summary>
    private sealed class PhysicsMaterials
    {
        private PhysicsMaterial[] _bodies = [];
        private PhysicsMaterial[] _statics = [];
        private static readonly PhysicsMaterial DefaultMaterial = new(0.5f, 0f);

        public void Clear()
        {
            Array.Clear(_bodies);
            Array.Clear(_statics);
        }

        public void SetBody(int handle, float friction, float bounciness, int group = 0)
        {
            EnsureCapacity(ref _bodies, handle);
            _bodies[handle] = new PhysicsMaterial(friction, bounciness, group);
        }

        public void SetStatic(int handle, float friction, float bounciness, int group = 0)
        {
            EnsureCapacity(ref _statics, handle);
            _statics[handle] = new PhysicsMaterial(friction, bounciness, group);
        }

        public PhysicsMaterial Get(CollidableReference reference)
        {
            var materials = reference.Mobility == CollidableMobility.Static ? _statics : _bodies;
            var handle = reference.Mobility == CollidableMobility.Static ? reference.StaticHandle.Value : reference.BodyHandle.Value;
            return (uint)handle < (uint)materials.Length ? materials[handle] : DefaultMaterial;
        }

        private static void EnsureCapacity(ref PhysicsMaterial[] materials, int handle)
        {
            if (handle < materials.Length)
                return;

            Array.Resize(ref materials, Math.Max(handle + 1, Math.Max(4, materials.Length * 2)));
        }
    }

    private sealed class PhysicsSettings
    {
        public Vector3 Gravity = new(0f, -9.81f, 0f);
        public float ContactFrequency = 30f;
        public float ContactSpeedThreshold = 1f;
    }

    private struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public PhysicsMaterials Materials;
        public PhysicsSettings Settings;

        /** One list per worker thread, so recording an impact needs no synchronization. */
        public List<BounceRequest>[] BounceRequests;

        /** Impacts to publish, recorded the same way. */
        public List<PhysicsContact>[] ContactRequests;

        public void Initialize(Simulation simulation)
        {
            _simulation = simulation;
        }

        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        {
            var group = Materials.Get(a).Group;
            return group == 0 || group != Materials.Get(b).Group;
        }

        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
        {
            return true;
        }

        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold,
                                                        out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            var materialA = Materials.Get(pair.A);
            var materialB = Materials.Get(pair.B);

            var friction = MathF.Sqrt(materialA.Friction * materialB.Friction);
            var bounciness = MathF.Max(materialA.Bounciness, materialB.Bounciness);

            // Speculative contacts stop a body exactly at the surface, so no spring setting can make it
            // rebound. Bounce is added after the step from the impacts recorded here.
            pairMaterial = new PairMaterialProperties(friction, 2f, new SpringSettings(Settings.ContactFrequency, 1f));

            if (manifold.Count > 0)
                RecordImpact(workerIndex, pair, manifold.GetNormal(0), manifold.GetOffset(0), manifold.GetDepth(0), bounciness);

            return true;
        }

        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB,
                                             ref ConvexContactManifold manifold)
        {
            return true;
        }

        public void Dispose()
        {
        }

        private void RecordImpact(int workerIndex, CollidablePair pair, Vector3 normal, Vector3 offset, float depth, float bounciness)
        {
            var bodyA = ReadMotion(pair.A, out var velocityA, out var inverseMassA);
            var bodyB = ReadMotion(pair.B, out var velocityB, out var inverseMassB);

            // Bepu's manifold normal points from B to A; flipped, a positive value means the pair is closing.
            normal = -normal;
            var approachSpeed = Vector3.Dot(velocityA - velocityB, normal);

            // A speculative contact exists before the surfaces meet. Reporting it only once the pair reaches
            // touching distance within this step keeps a fall from being reported on every frame of its approach.
            if (approachSpeed >= Settings.ContactSpeedThreshold && depth > -approachSpeed * SpeculativeContactWindow)
            {
                ContactRequests[workerIndex].Add(new PhysicsContact
                                                     {
                                                         // Contact offsets are relative to the first collidable of the pair.
                                                         Position = ReadPosition(pair.A) + offset,
                                                         Normal = normal,
                                                         Speed = approachSpeed,
                                                         PairKey = ((ulong)pair.A.Packed << 32) | pair.B.Packed,
                                                     });
            }

            if (bounciness <= 0f || approachSpeed < MinBounceSpeed)
                return;

            BounceRequests[workerIndex].Add(new BounceRequest
                                                {
                                                    BodyA = bodyA,
                                                    BodyB = bodyB,
                                                    Normal = normal,
                                                    ApproachSpeed = approachSpeed,
                                                    Bounciness = bounciness,
                                                });
        }

        private int ReadMotion(CollidableReference reference, out Vector3 velocity, out float inverseMass)
        {
            if (reference.Mobility == CollidableMobility.Static)
            {
                velocity = Vector3.Zero;
                inverseMass = 0f;
                return NoBody;
            }

            var body = _simulation.Bodies[reference.BodyHandle];
            velocity = body.Velocity.Linear;
            inverseMass = body.LocalInertia.InverseMass;
            return reference.BodyHandle.Value;
        }

        private Vector3 ReadPosition(CollidableReference reference)
        {
            return reference.Mobility == CollidableMobility.Static
                       ? _simulation.Statics[reference.StaticHandle].Pose.Position
                       : _simulation.Bodies[reference.BodyHandle].Pose.Position;
        }

        private Simulation _simulation;
    }

    /// <summary>A pair that was closing fast when its contact was configured, to be given a rebound once the step has resolved it. The normal points from A to B.</summary>
    private struct BounceRequest
    {
        public int BodyA;
        public int BodyB;
        public Vector3 Normal;
        public float ApproachSpeed;
        public float Bounciness;
    }

    private struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        public PhysicsSettings Settings;

        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;
        public bool AllowSubstepsForUnconstrainedBodies => false;

        // Kinematic bodies are moved by their velocity input, so their poses must integrate.
        public bool IntegrateVelocityForKinematics => true;

        public void Initialize(Simulation simulation)
        {
        }

        public void PrepareForIntegration(float dt)
        {
        }

        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
                                      BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex,
                                      Vector<float> dt, ref BodyVelocityWide velocity)
        {
            var gravity = Settings.Gravity;
            var hasMass = Vector.GreaterThan(localInertia.InverseMass, Vector<float>.Zero);

            velocity.Linear.X = Vector.ConditionalSelect(hasMass, velocity.Linear.X + new Vector<float>(gravity.X) * dt, velocity.Linear.X);
            velocity.Linear.Y = Vector.ConditionalSelect(hasMass, velocity.Linear.Y + new Vector<float>(gravity.Y) * dt, velocity.Linear.Y);
            velocity.Linear.Z = Vector.ConditionalSelect(hasMass, velocity.Linear.Z + new Vector<float>(gravity.Z) * dt, velocity.Linear.Z);
        }
    }

    private const float MaxTimeJump = 0.5f;
    private const float MaxFrameDelta = 0.1f;

    /// <summary>Metres a position-driven kinematic body may move in one frame before the move counts as a cut and it is placed.</summary>
    private const float KinematicTeleportDistance = 2f;

    /** Closing speed in m/s below which a bounce is skipped, so resting contacts do not jitter. */
    private const float MinBounceSpeed = 1f;

    private const int NoBody = -1;

    /** Seconds of approach within which a not-yet-touching pair counts as an impact; one 60 Hz step. */
    private const float SpeculativeContactWindow = 1f / 60f;

    /** Contacts kept per update. A pile settling can produce thousands; past this they add cost, not information. */
    private const int MaxContactsPerUpdate = 4096;

    /** Acceleration in m/s² a continuous force needs to wake a sleeping body. */
    private const float MinWakeAcceleration = 0.05f;

    private const int MaxNearbyLinks = 50000;

    /** Times a background mesh build may fault before the body is left on its fallback shape. */
    private const int MaxMeshBuildAttempts = 3;

    /** Softness of a hinge motor: how readily it gives way under load instead of forcing its speed. */
    private const float MotorSoftness = 0.0001f;

    private readonly object _logSource;
    private Simulation _simulation;
    private BufferPool _pool;
    private float _frameDelta;
    private bool _buildMeshesInBackground;
    private readonly Dictionary<SharedShapeKey, MeshBuild> _meshBuilds = new();
    private readonly List<(BodyEntry Entry, RigidPose Target)> _trackedKinematics = new();
    private ThreadDispatcher _dispatcher;
    private List<BounceRequest>[] _bounceRequests = [];
    private readonly List<BounceRequest> _pendingBounces = new();
    private readonly PhysicsSettings _settings = new();
    private readonly PhysicsMaterials _materials = new();
    private readonly Dictionary<Guid, BodyEntry> _bodies = new();
    private readonly Dictionary<Guid, JointEntry> _joints = new();
    private readonly ConditionalWeakTable<PhysicsBody, IdBox> _implicitIds = new();
    private readonly ConditionalWeakTable<PhysicsJoint, IdBox> _implicitJointIds = new();
    private readonly List<BodyEntry> _renderOrder = new();
    private readonly HashSet<Guid> _seenBodies = new();
    private readonly HashSet<Guid> _seenJoints = new();
    private readonly List<Guid> _removeBodies = new();
    private readonly List<Guid> _removeJoints = new();
    private readonly List<Guid> _staleImpulseIds = new();
    private readonly HashSet<SharedShapeKey> _abandonedMeshes = new();
    private List<PhysicsBody> _frameBodies;
    private List<PhysicsJoint> _frameJoints;
    private List<PhysicsForce> _frameForces;

    private List<PhysicsContact>[] _contactRequests = [];
    private readonly List<PhysicsContact> _contacts = new();
    private readonly List<PhysicsContact> _contactOrder = new();
    private readonly List<PhysicsForce> _continuousForces = new();
    private readonly Dictionary<Guid, (int TriggerCount, int LastFrame)> _impulseCounts = new();
    private readonly ConditionalWeakTable<PhysicsForce, IdBox> _implicitForceIds = new();
    private readonly Dictionary<SharedShapeKey, SharedShape> _sharedShapes = new();
    private readonly Dictionary<Guid, NearbyLinks> _nearbyLinks = new();
    private readonly List<Vector3> _linkPositions = new();
    private readonly Dictionary<(int, int, int), List<int>> _linkCells = new();

    /** Reused description for one instance of an instanced body; only valid inside ReconcileBodies. */
    private readonly PhysicsBody _instanceBody = new();

    /** Reused sanitized copy for a description with non-finite inputs; only valid inside ReconcileBody. */
    private readonly PhysicsBody _finiteBody = new();
    private readonly List<(PhysicsBody Descriptor, int Start, int Count)> _bodyRanges = new();

    private static readonly PhysicsBodyInstance[] SingleInstance =
        [new() { Orientation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, HullIndex = -1 }];

    private HashSet<ulong> _previousContactPairs = new();
    private HashSet<ulong> _currentContactPairs = new();
    private bool _hasLoggedFieldForceError;
    private bool _hasLoggedLinkLimitWarning;

    private float _fixedRate = 60f;
    private int _substeps = 2;
    private int _solverIterations = 4;
    private bool _multithreaded = true;
    private bool _deterministic;
    private double _accumulator;
    private double _lastTimeSeconds = double.NaN;
    private float _simTime;
    private float _interpolationAlpha = 1f;
    private int _dynamicRenderCount;
    private bool _hasLoggedStaticJointWarning;
    private bool _wasResetPressed;
    private int _impulseFrame;
}
