using T3.Core.Utils;

namespace Lib.physics;

/// <summary>
/// Describes rigid bodies for <see cref="PhysicsWorld"/>. This operator owns no simulation
/// state: it only publishes a description that the world turns into Bepu bodies. The body id
/// is fixed for the lifetime of this operator instance, which is how the world recognises the
/// same body across evaluations.
///
/// On its own the description is one primitive body. With <see cref="Points"/> it is one body per
/// point, and with <see cref="Geometry"/> the bodies take their collision shape from the geometry:
/// one body per geometry part, or the whole geometry as one body - at every point when points are connected.
/// A concave piece collides as a compound of convex hulls, so holes and hollows stay open.
/// </summary>
[Guid("efa358f6-8f07-448f-ad9e-d911c41a2098")]
internal sealed class RigidBody : Instance<RigidBody>
{
    /// <summary>What the parts of connected geometry become.</summary>
    private enum GeometryPieces
    {
        /// <summary>Every part is a body of its own, like the cells of a fracture.</summary>
        EachPartIsABody = 0,

        /// <summary>All parts move together as one body, like the pieces of an imported model.</summary>
        WholeObject = 1,
    }

    private enum MassModes
    {
        /// <summary>Weighs geometry bodies by their volume and primitives by <see cref="Mass"/>.</summary>
        Auto = 0,
        Fixed = 1,
        FromDensity = 2,
    }

    [Output(Guid = "165a613b-df3a-4d84-b5af-1c72a381fa15", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<PhysicsBody> OutBody = new();

    public RigidBody()
    {
        _descriptor.Id = _id;
        OutBody.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var shape = (PhysicsShapeType)Shape.GetValue(context);
        var kind = (PhysicsBodyKind)Kind.GetValue(context);
        var points = Points.GetValue(context) as StructuredList<Point>;
        var hasPoints = points != null && points.NumElements > 0;
        var geometry = Geometry.GetValue(context);
        var hasGeometry = geometry != null && geometry.FaceCount > 0;

        var mergeParts = hasPoints || (GeometryPieces)Pieces.GetValue(context) == GeometryPieces.WholeObject;
        var split = PhysicsGeometry.HullSplit.None;
        if (hasGeometry)
        {
            (shape, split) = ResolveGeometryShape(shape, kind);
        }
        else if (shape == PhysicsShapeType.Auto)
        {
            shape = PhysicsShapeType.Box;
        }

        try
        {
            UpdateCollisionData(hasGeometry ? geometry : null, shape, split, Math.Clamp(MaxHulls.GetValue(context), 1, 64), mergeParts);
            _hasLoggedGeometryError = false;
        }
        catch (Exception e)
        {
            // Geometry arrives from the graph in any state; a bad index must not take the evaluator down.
            // With no hulls or triangles the world resolves the shape back to its primitive.
            _descriptor.Hulls = null;
            _descriptor.MeshTriangles = null;
            _pendingSplit = null;
            _hulls = null;
            _triangles = null;
            _geometryStamp = 0;
            if (!_hasLoggedGeometryError)
                Log.Warning($"RigidBody: building the collision data failed: {e.Message}", this);
            _hasLoggedGeometryError = true;
        }

        UpdateInstances(hasPoints ? points : null, shape);
        _descriptor.SourceGeometry = hasGeometry ? geometry : null;
        _descriptor.SourcePerPart = hasGeometry && !mergeParts && _descriptor.Hulls != null && geometry.Parts.Length > 0;

        var massMode = (MassModes)MassMode.GetValue(context);
        _descriptor.MassFromDensity = massMode == MassModes.FromDensity || (massMode == MassModes.Auto && hasGeometry);
        _descriptor.Density = Density.GetValue(context);

        _descriptor.Shape = shape;
        _descriptor.Kind = kind;
        _descriptor.CollisionGroup = CollisionGroup.GetValue(context);
        _descriptor.Position = Position.GetValue(context);

        var rotation = Rotation.GetValue(context);
        _descriptor.Orientation = Quaternion.CreateFromYawPitchRoll(MathUtils.ToRad * rotation.Y,
                                                                    MathUtils.ToRad * rotation.X,
                                                                    MathUtils.ToRad * rotation.Z);

        _descriptor.Size = Size.GetValue(context);
        _descriptor.Radius = Radius.GetValue(context);
        _descriptor.Length = Length.GetValue(context);
        _descriptor.Mass = Mass.GetValue(context);
        _descriptor.LinearVelocity = LinearVelocity.GetValue(context);
        _descriptor.AngularVelocity = AngularVelocity.GetValue(context);
        _descriptor.Friction = Friction.GetValue(context);
        _descriptor.Bounciness = Bounciness.GetValue(context);
        _descriptor.LinearDamping = LinearDamping.GetValue(context);
        _descriptor.AngularDamping = AngularDamping.GetValue(context);
        _descriptor.GravityScale = GravityScale.GetValue(context);
        // A body whose first colliders are still being split appears once they are ready, at its start pose, rather than
        // falling as a stand-in hull and jumping back when the real shape arrives.
        _descriptor.Enabled = Enabled.GetValue(context) && !(_pendingSplit != null && _hulls == null);

        OutBody.Value = _descriptor;
        OutBody.DirtyFlag.Invalidate();
    }

    /// <summary>
    /// The collider a geometry body is built as. Connecting geometry is the request to collide as that geometry, so a
    /// primitive shape setting is read like Auto: the exact triangles for bodies that don't move by themselves, and for
    /// dynamic ones hulls that are split where the shape is concave.
    /// </summary>
    private static (PhysicsShapeType Shape, PhysicsGeometry.HullSplit Split) ResolveGeometryShape(PhysicsShapeType shape, PhysicsBodyKind kind)
    {
        var isDynamic = kind == PhysicsBodyKind.Dynamic;
        switch (shape)
        {
            case PhysicsShapeType.ConvexHull:
                return (PhysicsShapeType.ConvexHull, PhysicsGeometry.HullSplit.None);

            case PhysicsShapeType.ConvexDecomposition:
                return (PhysicsShapeType.ConvexHull, PhysicsGeometry.HullSplit.Always);

            default:
                return isDynamic
                           ? (PhysicsShapeType.ConvexHull, PhysicsGeometry.HullSplit.Concave)
                           : (PhysicsShapeType.Mesh, PhysicsGeometry.HullSplit.None);
        }
    }

    /// <summary>
    /// Rebuilds hulls and triangles only when the geometry's content or the way it is used changed. The arrays are
    /// replaced, never edited, because the world recognises a changed shape by the array it was built from.
    /// </summary>
    private void UpdateCollisionData(MeshGeometry geometry, PhysicsShapeType shape, PhysicsGeometry.HullSplit split, int maxHulls, bool mergeParts)
    {
        if (geometry == null)
        {
            _descriptor.Hulls = null;
            _descriptor.MeshTriangles = null;
            _hulls = null;
            _triangles = null;
            _pendingSplit = null;
            _geometryStamp = 0;
            _stampedGeometry = null;
            return;
        }

        var needsHulls = shape == PhysicsShapeType.ConvexHull;
        var stamp = HashCode.Combine(GeometryStamp(geometry), needsHulls, mergeParts, split, split == PhysicsGeometry.HullSplit.None ? 0 : maxHulls);
        // One split at a time: an abandoned split would keep burning a worker thread to a result
        // that is discarded on arrival. A geometry that changed meanwhile is picked up by the stamp
        // mismatch when the running split finishes, and rebuilt from the then-current snapshot.
        if (_pendingSplit == null && (stamp != _geometryStamp || (_hulls == null && _triangles == null)))
        {
            _geometryStamp = stamp;
            if (needsHulls && split != PhysicsGeometry.HullSplit.None)
            {
                // Splitting takes a noticeable fraction of a second, so it runs on a worker from a copy of the geometry:
                // producers rebuild their output in place on later frames. The current colliders stay until it is done.
                var snapshot = Snapshot(geometry);
                _pendingSplit = new PendingSplit(stamp, Task.Run(() => PhysicsGeometry.BuildHulls(snapshot, mergeParts, split, maxHulls)));
            }
            else
            {
                _pendingSplit = null;
                _hulls = needsHulls ? PhysicsGeometry.BuildHulls(geometry, mergeParts) : null;
                _triangles = needsHulls ? null : PhysicsGeometry.BuildTriangles(geometry);
            }
        }

        if (_pendingSplit is { Hulls.IsCompleted: true } finished)
        {
            _pendingSplit = null;
            if (finished.Stamp == _geometryStamp)
            {
                if (finished.Hulls.IsCompletedSuccessfully)
                {
                    _hulls = finished.Hulls.Result;
                }
                else
                {
                    Log.Warning($"RigidBody: splitting the geometry into hulls failed, using one hull per part: {finished.Hulls.Exception?.GetBaseException().Message}", this);
                    _hulls = PhysicsGeometry.BuildHulls(geometry, mergeParts);
                }

                _triangles = null;
            }
        }

        _descriptor.Hulls = _hulls;
        _descriptor.MeshTriangles = _triangles;
    }

    private void UpdateInstances(StructuredList<Point> points, PhysicsShapeType shape)
    {
        if (points != null)
        {
            // One body per point; with geometry every point carries the same merged piece.
            var source = points.TypedElements;
            var count = 0;
            for (var index = 0; index < source.Length; index++)
            {
                if (!Point.IsSeparator(source[index]))
                    count++;
            }

            EnsureInstanceCapacity(count);
            var write = 0;
            for (var index = 0; index < source.Length; index++)
            {
                ref readonly var point = ref source[index];
                if (Point.IsSeparator(point))
                    continue;

                _instances[write++] = new PhysicsBodyInstance
                                          {
                                              Position = point.Position,
                                              Orientation = point.Orientation,
                                              Scale = point.Scale,
                                              Color = point.Color,
                                              HullIndex = 0,
                                          };
            }

            _descriptor.Instances = _instances;
            _descriptor.InstanceCount = count;
            return;
        }

        var hulls = _descriptor.Hulls;
        if (hulls != null && hulls.Length > 1 && (shape == PhysicsShapeType.ConvexHull || shape == PhysicsShapeType.Mesh))
        {
            // One body per geometry part, each staying where the geometry has it.
            EnsureInstanceCapacity(hulls.Length);
            for (var index = 0; index < hulls.Length; index++)
            {
                _instances[index] = new PhysicsBodyInstance
                                        {
                                            Orientation = Quaternion.Identity,
                                            Scale = Vector3.One,
                                            Color = Vector4.One,
                                            HullIndex = index,
                                        };
            }

            _descriptor.Instances = _instances;
            _descriptor.InstanceCount = hulls.Length;
            return;
        }

        _descriptor.Instances = null;
        _descriptor.InstanceCount = 0;
    }

    /// <summary>A copy of what <see cref="PhysicsGeometry.BuildHulls"/> reads, safe to hand to a worker thread.</summary>
    private static MeshGeometry Snapshot(MeshGeometry geometry)
    {
        return new MeshGeometry
                   {
                       Positions = (Vector3[])geometry.Positions.Clone(),
                       FaceCornerOffsets = (int[])geometry.FaceCornerOffsets.Clone(),
                       CornerPointIndices = (int[])geometry.CornerPointIndices.Clone(),
                       Parts = (GeometryPart[])geometry.Parts.Clone(),
                   };
    }

    private void EnsureInstanceCapacity(int count)
    {
        if (_instances.Length < count)
            _instances = new PhysicsBodyInstance[Math.Max(count, _instances.Length * 2)];
    }

    /// <summary>
    /// The geometry's content stamp, recomputed only when the instance, its <see cref="MeshGeometry.Version"/>
    /// or its size changed. Producers bump the version when they rebuild their output, so a static input costs
    /// nothing per frame; hashing the buffers every evaluation reads megabytes for the same result.
    /// </summary>
    private int GeometryStamp(MeshGeometry geometry)
    {
        if (ReferenceEquals(geometry, _stampedGeometry)
            && geometry.Version == _stampedVersion
            && geometry.PointCount == _stampedPointCount
            && geometry.FaceCount == _stampedFaceCount
            && geometry.CornerCount == _stampedCornerCount)
            return _geometryContentStamp;

        _stampedGeometry = geometry;
        _stampedVersion = geometry.Version;
        _stampedPointCount = geometry.PointCount;
        _stampedFaceCount = geometry.FaceCount;
        _stampedCornerCount = geometry.CornerCount;
        _geometryContentStamp = PhysicsGeometry.Stamp(geometry);
        return _geometryContentStamp;
    }

    /** Hulls being split on a worker, for the geometry stamp they were started for. */
    private sealed record PendingSplit(int Stamp, Task<PhysicsHull[]> Hulls);

    private readonly Guid _id = Guid.NewGuid();
    private PendingSplit _pendingSplit;
    private readonly PhysicsBody _descriptor = new();
    private bool _hasLoggedGeometryError;
    private PhysicsBodyInstance[] _instances = [];
    private PhysicsHull[] _hulls;
    private Vector3[] _triangles;
    private int _geometryStamp;
    private MeshGeometry _stampedGeometry;
    private int _stampedVersion;
    private int _stampedPointCount;
    private int _stampedFaceCount;
    private int _stampedCornerCount;
    private int _geometryContentStamp;

    [Input(Guid = "c37e0055-1f11-4b35-8d1f-0a3eb0c64a57")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "7c155391-9271-4678-95b2-052795115586")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "5f6cc54b-9221-42b9-87d7-a0f9669c64b2", MappedType = typeof(PhysicsShapeType))]
    public readonly InputSlot<int> Shape = new((int)PhysicsShapeType.Auto);

    [Input(Guid = "e13e2b87-7a04-4197-82a8-a11fab87d1f9", MappedType = typeof(GeometryPieces))]
    public readonly InputSlot<int> Pieces = new(0);

    [Input(Guid = "dba98aeb-66df-41eb-b3a2-e91acb71d0be")]
    public readonly InputSlot<int> MaxHulls = new(8);

    [Input(Guid = "70f094d8-c499-40bf-ab28-0081297a5985", MappedType = typeof(PhysicsBodyKind))]
    public readonly InputSlot<int> Kind = new(0);

    [Input(Guid = "5072f21a-83a3-41a7-83cd-f702d9c89c84")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "890a457a-af9a-4d23-80b5-07014c95ee27")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "050f6d8c-40ff-4450-b4a1-69bf4ff2b3be")]
    public readonly InputSlot<Vector3> Size = new(Vector3.One);

    [Input(Guid = "c63d5ebe-108d-4afd-ba39-2cbda0d62011")]
    public readonly InputSlot<float> Radius = new(0.5f);

    [Input(Guid = "d8d048f0-6517-497e-878d-24fc95ed8a64")]
    public readonly InputSlot<float> Length = new(1f);

    [Input(Guid = "974ca0a2-5f02-4ccd-817b-5114070b243e")]
    public readonly InputSlot<float> Mass = new(1f);

    [Input(Guid = "10919c3d-5979-4499-a958-c347008f7c12", MappedType = typeof(MassModes))]
    public readonly InputSlot<int> MassMode = new(0);

    [Input(Guid = "cf7c8302-520f-4a41-84ad-18d9f0b7148b")]
    public readonly InputSlot<float> Density = new(1f);

    [Input(Guid = "e965e0ce-38ce-46da-a7ac-e730d7876d29")]
    public readonly InputSlot<Vector3> LinearVelocity = new();

    [Input(Guid = "3d837671-9883-43b0-a889-764ae0fbb6e6")]
    public readonly InputSlot<Vector3> AngularVelocity = new();

    [Input(Guid = "28d83ea4-beb0-4e39-bb27-dc714c48c69f")]
    public readonly InputSlot<float> Friction = new(0.5f);

    [Input(Guid = "847babc8-53b1-457d-ab45-f85b8eff80c9")]
    public readonly InputSlot<float> Bounciness = new();

    [Input(Guid = "a7a684cf-2d9b-4cc5-948b-a07dd990e08a")]
    public readonly InputSlot<float> LinearDamping = new();

    [Input(Guid = "acd5e765-3086-4489-bbab-aab68881a2c0")]
    public readonly InputSlot<float> AngularDamping = new();

    [Input(Guid = "1b9b7bbd-aba7-4a23-8f78-c1366f260f75")]
    public readonly InputSlot<float> GravityScale = new(1f);

    [Input(Guid = "38f46166-46ad-4f6c-94e8-5b1b4b1c7445")]
    public readonly InputSlot<int> CollisionGroup = new(0);

    [Input(Guid = "09fe2b00-ad21-4103-84fd-bf970c329bb2")]
    public readonly InputSlot<bool> Enabled = new(true);
}
