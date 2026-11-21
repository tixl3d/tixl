namespace Lib.physics;

/// <summary>
/// The simulated bodies of one or more <see cref="RigidBody"/> operators as geometry: the way back from physics into the geometry
/// operators. A body made from geometry returns that same geometry with every part moved by its body, so one
/// geometry describes the object, collides and is drawn. A primitive body returns a box, sphere, capsule or
/// cylinder of its size, so it needs no separate mesh either. Every body is one part of the result.
/// </summary>
[Guid("8e417a73-0f16-4f5e-b253-39ec13c2ace0")]
internal sealed class RigidBodyGeometry : Instance<RigidBodyGeometry>
{
    [Output(Guid = "27dd76aa-73b8-4a36-97d5-f6a10b334f48", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<MeshGeometry> Result = new();

    [Output(Guid = "d1e9cddf-c53f-4380-afbd-2ac91a0304bc", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Count = new();

    public RigidBodyGeometry()
    {
        Result.UpdateAction += Update;
        Count.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Pulling the world's buffer is what makes the world step this frame and publish the poses read below.
        var worldPoints = World.GetValue(context);
        _bodies.Clear();
        var inputs = Body.GetCollectedTypedInputs();
        if (inputs != null)
        {
            for (var index = 0; index < inputs.Count; index++)
            {
                var body = inputs[index].GetValue(context);
                if (body != null)
                    _bodies.Add(body);
            }
        }

        // Ranges published on a description belong to the buffer pulled above; skip descriptions whose
        // last publisher is another world, so a body feeding two worlds is never drawn with the wrong poses.
        if (worldPoints != null)
        {
            for (var index = _bodies.Count - 1; index >= 0; index--)
            {
                if (_bodies[index].PublishedBuffer != worldPoints)
                    _bodies.RemoveAt(index);
            }
        }

        var showColliders = ShowColliders.GetValue(context);
        var staticStamp = StaticStamp(showColliders);
        var count = 0;
        try
        {
            if (worldPoints != null && _bodies.Count > 0)
            {
                if (_hasStamp && staticStamp == _staticStamp && Result.Value == _output && !PosesChanged())
                {
                    // Nothing that feeds the pose changed since the last build - bodies that are awake
                    // but quiet publish poses that wander in the last float bits. Keeping the built
                    // geometry lets the downstream mesh compilers and GPU uploads cache instead of
                    // rebuilding megabytes per frame.
                    count = _lastCount;
                }
                else
                {
                    count = PhysicsGeometry.BuildPosed(_bodies, _output, _scratch, showColliders);
                    SnapshotPoses();
                    _lastCount = count;
                    _staticStamp = staticStamp;
                    _hasStamp = true;
                }
            }
            else
            {
                _hasStamp = false;
            }

            _hasLoggedError = false;
        }
        catch (Exception e)
        {
            // Geometry arrives from the graph in any state; a bad index must not take the evaluator down.
            count = 0;
            _hasStamp = false;
            if (!_hasLoggedError)
                Log.Warning($"RigidBodyGeometry failed: {e.Message}", this);
            _hasLoggedError = true;
        }

        Result.Value = count > 0 ? _output : null;
        Result.DirtyFlag.Invalidate();
        Count.Value = count;
        Count.DirtyFlag.Invalidate();
    }

    /// <summary>
    /// Hash of everything <see cref="PhysicsGeometry.BuildPosed"/> reads except the published poses and
    /// instance scales: the bodies, their source geometry's version, the collider view source and the
    /// primitive dimensions. The pose contents are compared with a tolerance against the last snapshot
    /// (<see cref="PosesChanged"/>), because a world of awake-but-quiet bodies re-publishes poses that
    /// wander in the last float bits every frame.
    /// </summary>
    private int StaticStamp(bool showColliders)
    {
        var hash = new HashCode();
        hash.Add(showColliders);
        for (var index = 0; index < _bodies.Count; index++)
        {
            var body = _bodies[index];
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(body));
            var source = body.SourceGeometry;
            hash.Add(source == null ? 0 : source.Version);
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source));
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(body.Hulls));
            hash.Add(body.SourcePerPart);
            hash.Add(body.Shape);
            hash.Add(body.Size);
            hash.Add(body.Radius);
            hash.Add(body.Length);
            hash.Add(body.PublishedCount);
            hash.Add(body.PublishedStart);
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether any published pose or instance scale moved by more than a fraction of a
    /// millimetre since the last snapshot. A resting world's last-bit wander does not count.</summary>
    private bool PosesChanged()
    {
        if (_scratch.Count < _bodies.Count)
            return true;

        for (var index = 0; index < _bodies.Count; index++)
        {
            var body = _bodies[index];
            var cache = _scratch[index];
            var start = body.PublishedStart;
            var count = body.PublishedCount;
            var poses = body.PublishedPoints;
            if (cache.LastPoseStart != start || cache.LastPoseCount != count
                || poses == null || count <= 0 || start < 0 || start + count > poses.Length
                || cache.LastPoses == null || cache.LastPoses.Length < count)
                return true;

            for (var poseIndex = 0; poseIndex < count; poseIndex++)
            {
                ref readonly var pose = ref poses[start + poseIndex];
                ref readonly var last = ref cache.LastPoses[poseIndex];
                if (Moved(pose.Position, last.Position) || Moved(pose.Orientation, last.Orientation)
                    || Moved(pose.Scale, last.Scale) || Moved(pose.Color, last.Color))
                    return true;
            }

            var instances = body.Instances;
            if (instances != null && start + count <= instances.Length)
            {
                if (cache.LastScales == null || cache.LastScales.Length < count)
                    return true;

                for (var instanceIndex = 0; instanceIndex < count; instanceIndex++)
                {
                    if (Moved(instances[start + instanceIndex].Scale, cache.LastScales[instanceIndex]))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>Keeps the pose snapshot the next <see cref="PosesChanged"/> compares against.</summary>
    private void SnapshotPoses()
    {
        for (var index = 0; index < _bodies.Count; index++)
        {
            var body = _bodies[index];
            var cache = _scratch[index];
            var start = body.PublishedStart;
            var count = body.PublishedCount;
            var poses = body.PublishedPoints;
            if (poses == null || count <= 0 || start < 0 || start + count > poses.Length)
            {
                cache.LastPoseStart = -1;
                cache.LastPoseCount = -1;
                continue;
            }

            if (cache.LastPoses == null || cache.LastPoses.Length < count)
                cache.LastPoses = new Point[count];
            for (var poseIndex = 0; poseIndex < count; poseIndex++)
                cache.LastPoses[poseIndex] = poses[start + poseIndex];

            var instances = body.Instances;
            if (instances != null && start + count <= instances.Length)
            {
                if (cache.LastScales == null || cache.LastScales.Length < count)
                    cache.LastScales = new Vector3[count];
                for (var instanceIndex = 0; instanceIndex < count; instanceIndex++)
                    cache.LastScales[instanceIndex] = instances[start + instanceIndex].Scale;
            }

            cache.LastPoseStart = start;
            cache.LastPoseCount = count;
        }
    }

    private const float PoseTolerance = 0.0001f;

    private static bool Moved(Vector3 a, Vector3 b)
        => MathF.Abs(a.X - b.X) > PoseTolerance || MathF.Abs(a.Y - b.Y) > PoseTolerance || MathF.Abs(a.Z - b.Z) > PoseTolerance;

    private static bool Moved(Quaternion a, Quaternion b)
        => MathF.Abs(a.X - b.X) > PoseTolerance || MathF.Abs(a.Y - b.Y) > PoseTolerance
        || MathF.Abs(a.Z - b.Z) > PoseTolerance || MathF.Abs(a.W - b.W) > PoseTolerance;

    private static bool Moved(Vector4 a, Vector4 b)
        => MathF.Abs(a.X - b.X) > PoseTolerance || MathF.Abs(a.Y - b.Y) > PoseTolerance
        || MathF.Abs(a.Z - b.Z) > PoseTolerance || MathF.Abs(a.W - b.W) > PoseTolerance;

    private readonly MeshGeometry _output = new();
    private readonly List<PhysicsBody> _bodies = new();
    private readonly List<PhysicsGeometry.PosedBody> _scratch = new();
    private bool _hasLoggedError;
    private int _staticStamp;
    private bool _hasStamp;
    private int _lastCount;

    [Input(Guid = "eb4be503-426c-4ea3-af0d-47933b2fd641")]
    public readonly InputSlot<BufferWithViews> World = new();

    [Input(Guid = "9039d3ba-121e-4956-8b74-944bd4170f69")]
    public readonly MultiInputSlot<PhysicsBody> Body = new();

    [Input(Guid = "4b8e2f61-93c7-4d0a-b5e8-7f21c6a9d304")]
    public readonly InputSlot<bool> ShowColliders = new();
}
