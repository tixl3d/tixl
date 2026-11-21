using System;
using System.Numerics;
using System.Collections.Generic;
using BepuUtilities.Memory;
using T3.Core.DataTypes;

namespace Lib.physics;

/// <summary>
/// Turns a <see cref="MeshGeometry"/> into the collision data a <see cref="PhysicsBody"/> carries: convex
/// pieces for hull bodies and a triangle list for mesh colliders. Kept free of operator and GPU types so it
/// can be tested next to <see cref="PhysicsSimulation"/>.
/// </summary>
internal static class PhysicsGeometry
{
    /// <summary>Most points one hull is built from. Bepu's hull builder is far from linear, and a collision hull
    /// gains nothing from thousands of surface points.</summary>
    public const int MaxHullPoints = 2048;

    /// <summary>
    /// Fingerprint of everything the collision data depends on. Costs linear time in the geometry, so the
    /// caller runs it only when the instance, its <see cref="MeshGeometry.Version"/> or its size changed -
    /// producers bump the version when they rebuild their output, matching the other geometry consumers.
    /// </summary>
    public static int Stamp(MeshGeometry geometry)
    {
        var hash = new HashCode();
        hash.Add(geometry.PointCount);
        hash.Add(geometry.FaceCount);
        hash.Add(geometry.CornerCount);
        hash.Add(geometry.Parts.Length);
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(geometry.Positions.AsSpan()));
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(geometry.CornerPointIndices.AsSpan()));
        foreach (var part in geometry.Parts)
        {
            hash.Add(part.FaceStart);
            hash.Add(part.FaceCount);
            hash.Add(part.Pivot);
        }

        return hash.ToHashCode();
    }

    /// <summary>How far a geometry part may be split into several convex hulls.</summary>
    public enum HullSplit
    {
        /// <summary>Every part is one hull.</summary>
        None,

        /// <summary>A part is split where one hull would clearly fill in its concave shape.</summary>
        Concave,

        /// <summary>Every part is split until its hulls follow its shape closely.</summary>
        Always,
    }

    /// <summary>
    /// One collision piece per geometry part, with its points relative to the part's pivot. Geometry without parts,
    /// or <paramref name="merge"/>, gives a single piece around the origin that keeps every part as a hull of its own, so
    /// the whole object moves as one compound body.
    /// </summary>
    public static PhysicsHull[] BuildHulls(MeshGeometry geometry, bool merge, HullSplit split = HullSplit.None, int maxHullsPerPart = 1)
    {
        var parts = geometry.Parts;
        var marks = new int[geometry.PointCount];
        var pool = split == HullSplit.None ? null : new BufferPool();
        try
        {
            if (merge || parts.Length == 0)
            {
                var whole = new PhysicsHull { Points = CollectPoints(geometry, 0, geometry.FaceCount, Vector3.Zero, marks, 1) };
                var convexParts = new List<Vector3[]>();
                if (parts.Length == 0)
                {
                    AddConvexParts(geometry, 0, geometry.FaceCount, Vector3.Zero, whole.Points, split, maxHullsPerPart, pool, convexParts);
                }
                else
                {
                    for (var index = 0; index < parts.Length; index++)
                    {
                        var part = parts[index];
                        var points = CollectPoints(geometry, part.FaceStart, part.FaceCount, Vector3.Zero, marks, index + 2);
                        AddConvexParts(geometry, part.FaceStart, part.FaceCount, Vector3.Zero, points, split, maxHullsPerPart, pool, convexParts);
                    }
                }

                SetConvexParts(whole, convexParts, pool);
                return [whole];
            }

            var hulls = new PhysicsHull[parts.Length];
            var scratch = new List<Vector3[]>();
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                var hull = new PhysicsHull
                               {
                                   Points = CollectPoints(geometry, part.FaceStart, part.FaceCount, part.Pivot, marks, index + 1),
                                   Pivot = part.Pivot,
                               };
                scratch.Clear();
                AddConvexParts(geometry, part.FaceStart, part.FaceCount, part.Pivot, hull.Points, split, maxHullsPerPart, pool, scratch);
                SetConvexParts(hull, scratch, pool);
                hulls[index] = hull;
            }

            return hulls;
        }
        finally
        {
            pool?.Clear();
        }
    }

    /// <summary>The hulls one face range is simulated as: its decomposition when it is split, otherwise its own points.</summary>
    private static void AddConvexParts(MeshGeometry geometry, int faceStart, int faceCount, Vector3 pivot, Vector3[] points,
                                       HullSplit split, int maxHulls, BufferPool pool, List<Vector3[]> convexParts)
    {
        if (points.Length < 4)
            return;

        if (split != HullSplit.None && maxHulls > 1)
        {
            var triangles = CollectTriangles(geometry, faceStart, faceCount, pivot);
            var tolerance = split == HullSplit.Always ? PhysicsDecomposition.ForcedConcavityTolerance : PhysicsDecomposition.AutoConcavityTolerance;
            var decomposed = PhysicsDecomposition.Decompose(triangles, maxHulls, tolerance, pool);
            if (decomposed != null)
            {
                convexParts.AddRange(decomposed);
                return;
            }
        }

        convexParts.Add(points);
    }

    /// <summary>Stores the hulls a piece is made of and its volume; a piece that is one hull keeps only its points.</summary>
    private static void SetConvexParts(PhysicsHull hull, List<Vector3[]> convexParts, BufferPool pool)
    {
        var ownPool = pool == null;
        pool ??= new BufferPool();
        try
        {
            var volume = 0f;
            foreach (var part in convexParts)
                volume += PhysicsDecomposition.HullVolume(part, pool);

            hull.Volume = convexParts.Count > 0 ? volume : PhysicsDecomposition.HullVolume(hull.Points, pool);
            hull.ConvexParts = convexParts.Count > 1 ? convexParts.ToArray() : null;
        }
        finally
        {
            if (ownPool)
                pool.Clear();
        }
    }

    /// <summary>The triangles of a face range relative to a pivot, three points per triangle; faces with more corners are fanned.</summary>
    private static Vector3[] CollectTriangles(MeshGeometry geometry, int faceStart, int faceCount, Vector3 pivot)
    {
        var positions = geometry.Positions;
        var corners = geometry.CornerPointIndices;
        var offsets = geometry.FaceCornerOffsets;
        faceStart = Math.Clamp(faceStart, 0, geometry.FaceCount);
        var faceEnd = Math.Clamp(faceStart + Math.Max(0, faceCount), faceStart, geometry.FaceCount);
        var triangles = new List<Vector3>();
        for (var face = faceStart; face < faceEnd; face++)
        {
            var start = offsets[face];
            var count = offsets[face + 1] - start;
            if (count < 3 || start < 0 || start + count > corners.Length)
                continue;

            for (var corner = 1; corner + 1 < count; corner++)
            {
                var a = corners[start];
                var b = corners[start + corner];
                var c = corners[start + corner + 1];
                if ((uint)a >= (uint)positions.Length || (uint)b >= (uint)positions.Length || (uint)c >= (uint)positions.Length)
                    continue;

                triangles.Add(positions[a] - pivot);
                triangles.Add(positions[b] - pivot);
                triangles.Add(positions[c] - pivot);
            }
        }

        return triangles.ToArray();
    }

    /// <summary>All faces as a triangle list, three points per triangle; faces with more corners are fanned.</summary>
    public static Vector3[] BuildTriangles(MeshGeometry geometry)
    {
        var positions = geometry.Positions;
        var corners = geometry.CornerPointIndices;
        var offsets = geometry.FaceCornerOffsets;
        var triangles = new Vector3[geometry.GetTriangleCount() * 3];
        var write = 0;

        for (var face = 0; face < geometry.FaceCount; face++)
        {
            var start = offsets[face];
            var count = offsets[face + 1] - start;
            if (count < 3 || start < 0 || start + count > corners.Length)
                continue;

            for (var corner = 1; corner + 1 < count; corner++)
            {
                var a = corners[start];
                var b = corners[start + corner];
                var c = corners[start + corner + 1];
                if ((uint)a >= (uint)positions.Length || (uint)b >= (uint)positions.Length || (uint)c >= (uint)positions.Length)
                    continue;

                triangles[write++] = positions[a];
                triangles[write++] = positions[b];
                triangles[write++] = positions[c];
            }
        }

        if (write != triangles.Length)
            Array.Resize(ref triangles, write);

        return triangles;
    }

    /// <summary>The distinct points used by a face range, relative to a pivot. <paramref name="marks"/> remembers
    /// which piece last took each point, so one array serves every part without clearing.</summary>
    private static Vector3[] CollectPoints(MeshGeometry geometry, int faceStart, int faceCount, Vector3 pivot, int[] marks, int mark)
    {
        var positions = geometry.Positions;
        var corners = geometry.CornerPointIndices;
        var offsets = geometry.FaceCornerOffsets;
        faceStart = Math.Clamp(faceStart, 0, geometry.FaceCount);
        var faceEnd = Math.Clamp(faceStart + Math.Max(0, faceCount), faceStart, geometry.FaceCount);
        var cornerStart = Math.Clamp(offsets[faceStart], 0, corners.Length);
        var cornerEnd = Math.Clamp(offsets[faceEnd], cornerStart, corners.Length);

        var count = 0;
        for (var corner = cornerStart; corner < cornerEnd; corner++)
        {
            var point = corners[corner];
            if ((uint)point >= (uint)positions.Length || marks[point] == mark)
                continue;

            marks[point] = mark;
            count++;
        }

        // Thin out evenly when there are too many; the stride keeps points from all over the piece.
        var stride = count > MaxHullPoints ? (count + MaxHullPoints - 1) / MaxHullPoints : 1;
        var points = new Vector3[(count + stride - 1) / stride];
        var write = 0;
        var seen = 0;
        for (var corner = cornerStart; corner < cornerEnd && write < points.Length; corner++)
        {
            var point = corners[corner];
            if ((uint)point >= (uint)positions.Length || marks[point] != mark)
                continue;

            // Negating the mark visits each point once in this second pass and leaves it unequal to any later mark.
            marks[point] = -mark;
            if (seen++ % stride == 0)
                points[write++] = positions[point] - pivot;
        }

        if (write != points.Length)
            Array.Resize(ref points, write);

        return points;
    }

    /// <summary>
    /// Writes the bodies of one description as geometry at their simulated poses: the way back from physics into the
    /// geometry operators. A body made from geometry returns that geometry - each part moved with its body, or one
    /// copy per body - and a primitive body returns a box, sphere, capsule or cylinder of its size. One part per body.
    /// Returns the number of bodies written; 0 leaves <paramref name="output"/> untouched.
    /// </summary>
    public static int BuildPosed(PhysicsBody body, MeshGeometry output, ref MeshGeometry primitiveCache, ref (PhysicsShapeType, Vector3, float, float) primitiveKey)
    {
        var cache = new PosedBody { Primitive = primitiveCache, PrimitiveKey = primitiveKey };
        var count = BuildPosed(body, output, cache, false);
        primitiveCache = cache.Primitive;
        primitiveKey = cache.PrimitiveKey;
        return count;
    }

    /// <summary>
    /// <see cref="BuildPosed(PhysicsBody, MeshGeometry, ref MeshGeometry, ref ValueTuple{PhysicsShapeType, Vector3, float, float})"/>
    /// with a cache kept by the caller. With <paramref name="showColliders"/> a hull body is drawn as the convex hulls it
    /// collides as, each hull in a face color of its own, instead of as its source geometry.
    /// </summary>
    private static int BuildPosed(PhysicsBody body, MeshGeometry output, PosedBody cache, bool showColliders)
    {
        var poses = body.PublishedPoints;
        var count = body.PublishedCount;
        var start = body.PublishedStart;
        if (poses == null || count <= 0 || start < 0 || start + count > poses.Length)
            return 0;

        var source = body.SourceGeometry;
        var perPart = body.SourcePerPart;
        if (showColliders && body.Hulls is { Length: > 0 } hulls && source != null)
        {
            if (!ReferenceEquals(cache.ColliderSource, hulls))
            {
                cache.Collider = BuildColliderGeometry(hulls);
                cache.ColliderSource = hulls;
            }

            source = cache.Collider;
        }

        if (source == null || source.FaceCount == 0)
        {
            var key = (body.Shape, body.Size, body.Radius, body.Length);
            if (cache.Primitive == null || key != cache.PrimitiveKey)
            {
                cache.Primitive = BuildPrimitive(body);
                cache.PrimitiveKey = key;
            }

            source = cache.Primitive;
            perPart = false;
        }

        return perPart
                   ? PoseParts(body, source, poses.AsSpan(start, count), output, cache)
                   : PoseCopies(body, source, poses.AsSpan(start, count), output, cache);
    }

    /// <summary>Per-body working data for <see cref="BuildPosed(System.Collections.Generic.IReadOnlyList{PhysicsBody}, MeshGeometry, System.Collections.Generic.List{PosedBody})"/>, kept by the caller between frames.</summary>
    public sealed class PosedBody
    {
        public readonly MeshGeometry Geometry = new();
        public MeshGeometry Primitive;
        public (PhysicsShapeType, Vector3, float, float) PrimitiveKey;
        public int Count;

        /** Collider view of <see cref="ColliderSource"/>, rebuilt when the body's hulls are replaced. */
        public MeshGeometry Collider;

        public PhysicsHull[] ColliderSource;

        /** Scratch for <see cref="PoseParts"/>: the posed parts of the last frame and its moved-point marks. */
        public GeometryPart[] PosedParts;

        public bool[] MovedPoints;

        /** The poses of the last build, kept by <see cref="Lib.physics.RigidBodyGeometry"/> to skip rebuilds of an unchanged pose. */
        public Point[] LastPoses;

        public Vector3[] LastScales;

        public int LastPoseStart = -1;

        public int LastPoseCount = -1;

        // Attribute buffers this entry owns and reuses from frame to frame, so posing allocates only when a count changes.
        internal GeometryAttribute<Vector3> Normals;
        internal GeometryAttribute<Vector4> Colors;
        internal GeometryAttribute<Vector4> FaceColors;
    }

    /// <summary>The attribute in <paramref name="slot"/>, created or resized to <paramref name="count"/>, added to <paramref name="output"/>.</summary>
    private static GeometryAttribute<T> AddOwned<T>(MeshGeometry output, ref GeometryAttribute<T> slot, string name, AttributeDomain domain, int count)
        where T : unmanaged
    {
        if (slot == null)
            slot = new GeometryAttribute<T>(name, domain, count);
        else
            slot.Resize(count);

        output.Attributes.Add(slot);
        return slot;
    }

    private static GeometryPart[] ReuseParts(PosedBody cache, int length)
    {
        if (cache.PosedParts == null || cache.PosedParts.Length != length)
            cache.PosedParts = new GeometryPart[length];
        return cache.PosedParts;
    }

    /// <summary>
    /// The hulls of a body as geometry in the space of its source: one part per piece, pivoted where the piece is, so it
    /// poses exactly like the source geometry would. Every convex hull gets a face color of its own.
    /// </summary>
    public static MeshGeometry BuildColliderGeometry(PhysicsHull[] hulls)
    {
        var positions = new List<Vector3>();
        var corners = new List<int>();
        var offsets = new List<int> { 0 };
        var faceColors = new List<Vector4>();
        var parts = new GeometryPart[hulls.Length];
        var pool = new BufferPool();
        var hullIndex = 0;
        try
        {
            for (var index = 0; index < hulls.Length; index++)
            {
                var hull = hulls[index];
                var faceStart = offsets.Count - 1;
                if (hull != null)
                {
                    var convexParts = hull.ConvexParts ?? [hull.Points];
                    foreach (var convex in convexParts)
                    {
                        var added = PhysicsDecomposition.AppendHullFaces(convex, hull.Pivot, pool, positions, corners, offsets);
                        var color = HullColor(hullIndex++);
                        for (var face = 0; face < added; face++)
                            faceColors.Add(color);
                    }
                }

                parts[index] = new GeometryPart(faceStart, offsets.Count - 1 - faceStart, hull?.Pivot ?? Vector3.Zero, index, index);
            }
        }
        finally
        {
            pool.Clear();
        }

        var geometry = new MeshGeometry
                           {
                               Positions = positions.ToArray(),
                               CornerPointIndices = corners.ToArray(),
                               FaceCornerOffsets = offsets.ToArray(),
                               Parts = parts,
                           };
        var colors = geometry.Attributes.GetOrCreate<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Face, faceColors.Count);
        faceColors.CopyTo(colors.Values);
        AddFlatNormals(geometry);
        geometry.InvalidateTopologyCaches();
        return geometry;
    }

    /** Evenly spread hues, so neighbouring hulls of one body are told apart. */
    private static Vector4 HullColor(int index)
    {
        var hue = index * 0.618034f % 1f;
        var r = Math.Clamp(MathF.Abs(hue * 6f - 3f) - 1f, 0f, 1f);
        var g = Math.Clamp(2f - MathF.Abs(hue * 6f - 2f), 0f, 1f);
        var b = Math.Clamp(2f - MathF.Abs(hue * 6f - 4f), 0f, 1f);
        return new Vector4(0.35f + 0.65f * r, 0.35f + 0.65f * g, 0.35f + 0.65f * b, 1f);
    }

    private static void AddFlatNormals(MeshGeometry geometry)
    {
        var normals = geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, geometry.CornerCount).Values;
        for (var face = 0; face < geometry.FaceCount; face++)
        {
            var start = geometry.FaceCornerOffsets[face];
            var end = geometry.FaceCornerOffsets[face + 1];
            var a = geometry.Positions[geometry.CornerPointIndices[start]];
            var normal = Vector3.Zero;
            for (var corner = start + 1; corner + 1 < end; corner++)
                normal += Vector3.Cross(geometry.Positions[geometry.CornerPointIndices[corner]] - a, geometry.Positions[geometry.CornerPointIndices[corner + 1]] - a);

            normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitY;
            for (var corner = start; corner < end; corner++)
                normals[corner] = normal;
        }
    }

    /// <summary>
    /// Several descriptions as one geometry, in the order given. One body keeps all its attributes; when several are
    /// joined only what they all have survives: positions, faces, parts, and normals and part colors if every one has them.
    /// Returns the number of bodies written; 0 leaves <paramref name="output"/> untouched.
    /// </summary>
    public static int BuildPosed(System.Collections.Generic.IReadOnlyList<PhysicsBody> bodies, MeshGeometry output, System.Collections.Generic.List<PosedBody> scratch,
                                 bool showColliders = false)
    {
        // One entry per body, plus one after them that owns the joined output's buffers.
        while (scratch.Count < bodies.Count + 1)
            scratch.Add(new PosedBody());

        if (bodies.Count == 1)
            return BuildPosed(bodies[0], output, scratch[0], showColliders);

        int total = 0, pointCount = 0, cornerCount = 0, faceCount = 0, partCount = 0;
        bool allNormals = true, allColors = true;
        for (var index = 0; index < bodies.Count; index++)
        {
            var entry = scratch[index];
            entry.Count = bodies[index] == null ? 0 : BuildPosed(bodies[index], entry.Geometry, entry, showColliders);
            if (entry.Count == 0)
                continue;

            total += entry.Count;
            pointCount += entry.Geometry.PointCount;
            cornerCount += entry.Geometry.CornerCount;
            faceCount += entry.Geometry.FaceCount;
            partCount += entry.Geometry.Parts.Length;
            allNormals &= entry.Geometry.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out _);
            allColors &= entry.Geometry.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Part, out _);
        }

        if (total == 0)
            return 0;

        if (output.Positions.Length != pointCount)
            output.Positions = new Vector3[pointCount];
        if (output.CornerPointIndices.Length != cornerCount)
            output.CornerPointIndices = new int[cornerCount];
        if (output.FaceCornerOffsets.Length != faceCount + 1)
            output.FaceCornerOffsets = new int[faceCount + 1];

        var join = scratch[bodies.Count];
        var parts = ReuseParts(join, partCount);
        output.Attributes.Clear();
        var normals = allNormals ? AddOwned(output, ref join.Normals, GeometryAttributeNames.Normal, AttributeDomain.Corner, cornerCount) : null;
        var colors = allColors ? AddOwned(output, ref join.Colors, GeometryAttributeNames.Color, AttributeDomain.Part, partCount) : null;

        int pointBase = 0, cornerBase = 0, faceBase = 0, partBase = 0;
        for (var index = 0; index < bodies.Count; index++)
        {
            if (scratch[index].Count == 0)
                continue;

            var piece = scratch[index].Geometry;
            Array.Copy(piece.Positions, 0, output.Positions, pointBase, piece.PointCount);
            for (var corner = 0; corner < piece.CornerCount; corner++)
                output.CornerPointIndices[cornerBase + corner] = piece.CornerPointIndices[corner] + pointBase;

            for (var face = 0; face < piece.FaceCount; face++)
                output.FaceCornerOffsets[faceBase + face] = piece.FaceCornerOffsets[face] + cornerBase;

            for (var part = 0; part < piece.Parts.Length; part++)
                parts[partBase + part] = piece.Parts[part] with { FaceStart = piece.Parts[part].FaceStart + faceBase, Id = partBase + part };

            if (normals != null && piece.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var pieceNormals))
                Array.Copy(pieceNormals.Values, 0, normals.Values, cornerBase, Math.Min(pieceNormals.Values.Length, piece.CornerCount));

            if (colors != null && piece.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Part, out var pieceColors))
                Array.Copy(pieceColors.Values, 0, colors.Values, partBase, Math.Min(pieceColors.Values.Length, piece.Parts.Length));

            pointBase += piece.PointCount;
            cornerBase += piece.CornerCount;
            faceBase += piece.FaceCount;
            partBase += piece.Parts.Length;
        }

        output.FaceCornerOffsets[faceCount] = cornerCount;
        output.Parts = parts;
        output.InvalidateTopologyCaches();
        return total;
    }

    /// <summary>Same topology as the source; every part is moved from its place in the source to the pose of its body.</summary>
    private static int PoseParts(PhysicsBody body, MeshGeometry source, ReadOnlySpan<Point> poses, MeshGeometry output, PosedBody cache)
    {
        var parts = source.Parts;
        var count = Math.Min(poses.Length, parts.Length);
        var sourcePositions = source.Positions;
        var corners = source.CornerPointIndices;
        var offsets = source.FaceCornerOffsets;

        output.FaceCornerOffsets = offsets;
        output.CornerPointIndices = corners;
        if (output.Positions.Length != sourcePositions.Length || ReferenceEquals(output.Positions, sourcePositions))
            output.Positions = new Vector3[sourcePositions.Length];

        var positions = output.Positions;
        Array.Copy(sourcePositions, positions, sourcePositions.Length);
        var posedParts = ReuseParts(cache, parts.Length);
        Array.Copy(parts, posedParts, parts.Length);

        output.Attributes.Clear();
        source.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var sourceNormals);
        GeometryAttribute<Vector3> normals = null;

        if (sourceNormals != null)
        {
            normals = AddOwned(output, ref cache.Normals, GeometryAttributeNames.Normal, AttributeDomain.Corner, sourceNormals.Values.Length);
            Array.Copy(sourceNormals.Values, normals.Values, sourceNormals.Values.Length);
        }

        // Every other attribute is shared with the source unchanged; indexed, because foreach boxes the enumerator.
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (!ReferenceEquals(attribute, sourceNormals))
                output.Attributes.Add(attribute);
        }

        // A point shared by two parts can only follow one of them; the first part to use it wins.
        if (cache.MovedPoints == null || cache.MovedPoints.Length != sourcePositions.Length)
            cache.MovedPoints = new bool[sourcePositions.Length];
        var moved = cache.MovedPoints;
        Array.Clear(moved, 0, sourcePositions.Length);   // the buffer persists between frames
        for (var index = 0; index < count; index++)
        {
            ref readonly var pose = ref poses[index];
            var part = parts[index];
            var scale = ScaleOf(body, index);
            var faceStart = Math.Clamp(part.FaceStart, 0, source.FaceCount);
            var faceEnd = Math.Clamp(faceStart + Math.Max(0, part.FaceCount), faceStart, source.FaceCount);
            var cornerStart = Math.Clamp(offsets[faceStart], 0, corners.Length);
            var cornerEnd = Math.Clamp(offsets[faceEnd], cornerStart, corners.Length);

            for (var corner = cornerStart; corner < cornerEnd; corner++)
            {
                var point = corners[corner];
                if ((uint)point < (uint)positions.Length && !moved[point])
                {
                    moved[point] = true;
                    positions[point] = Vector3.Transform((sourcePositions[point] - part.Pivot) * scale, pose.Orientation) + pose.Position;
                }

                if (normals != null && corner < normals.Values.Length)
                    normals.Values[corner] = Vector3.Transform(sourceNormals.Values[corner], pose.Orientation);
            }

            posedParts[index] = part with { Pivot = pose.Position };
        }

        output.Parts = posedParts;
        output.InvalidateTopologyCaches();
        return count;
    }

    /// <summary>One copy of the whole source per body, each copy one part.</summary>
    private static int PoseCopies(PhysicsBody body, MeshGeometry source, ReadOnlySpan<Point> poses, MeshGeometry output, PosedBody cache)
    {
        var count = poses.Length;
        var sourcePositions = source.Positions;
        var sourceOffsets = source.FaceCornerOffsets;
        var sourceCorners = source.CornerPointIndices;
        var pointCount = sourcePositions.Length;
        var faceCount = source.FaceCount;
        var cornerCount = sourceCorners.Length;

        if (output.Positions.Length != pointCount * count || ReferenceEquals(output.Positions, sourcePositions))
            output.Positions = new Vector3[pointCount * count];
        if (output.FaceCornerOffsets.Length != faceCount * count + 1 || ReferenceEquals(output.FaceCornerOffsets, sourceOffsets))
            output.FaceCornerOffsets = new int[faceCount * count + 1];
        if (output.CornerPointIndices.Length != cornerCount * count || ReferenceEquals(output.CornerPointIndices, sourceCorners))
            output.CornerPointIndices = new int[cornerCount * count];

        var positions = output.Positions;
        var offsets = output.FaceCornerOffsets;
        var corners = output.CornerPointIndices;
        var parts = ReuseParts(cache, count);

        output.Attributes.Clear();
        source.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var sourceNormals);
        var normals = sourceNormals != null
                          ? AddOwned(output, ref cache.Normals, GeometryAttributeNames.Normal, AttributeDomain.Corner, cornerCount * count)
                          : null;
        var colors = AddOwned(output, ref cache.Colors, GeometryAttributeNames.Color, AttributeDomain.Part, count);

        // Face colors, such as the hull colors of the collider view, repeat in every copy.
        source.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Face, out var sourceFaceColors);
        var faceColors = sourceFaceColors != null && sourceFaceColors.Values.Length >= faceCount
                             ? AddOwned(output, ref cache.FaceColors, GeometryAttributeNames.Color, AttributeDomain.Face, faceCount * count)
                             : null;

        // The pivot of a single-hull body is where its pose is published; the copy is built around it.
        var hulls = body.Hulls;
        var pivot = hulls != null && hulls.Length > 0 ? hulls[0].Pivot : Vector3.Zero;

        for (var index = 0; index < count; index++)
        {
            ref readonly var pose = ref poses[index];
            var scale = ScaleOf(body, index);
            var pointBase = index * pointCount;
            var faceBase = index * faceCount;
            var cornerBase = index * cornerCount;

            for (var point = 0; point < pointCount; point++)
                positions[pointBase + point] = Vector3.Transform((sourcePositions[point] - pivot) * scale, pose.Orientation) + pose.Position;

            for (var face = 0; face < faceCount; face++)
                offsets[faceBase + face] = cornerBase + sourceOffsets[face];

            if (faceColors != null)
                Array.Copy(sourceFaceColors.Values, 0, faceColors.Values, faceBase, faceCount);

            for (var corner = 0; corner < cornerCount; corner++)
                corners[cornerBase + corner] = pointBase + sourceCorners[corner];

            if (normals != null)
            {
                var inverse = new Vector3(1f / scale.X, 1f / scale.Y, 1f / scale.Z);
                var limit = Math.Min(cornerCount, sourceNormals.Values.Length);
                for (var corner = 0; corner < limit; corner++)
                {
                    var normal = Vector3.Transform(sourceNormals.Values[corner] * inverse, pose.Orientation);
                    var length = normal.Length();
                    normals.Values[cornerBase + corner] = length > 1e-8f ? normal / length : normal;
                }
            }

            colors.Values[index] = pose.Color;
            parts[index] = new GeometryPart(faceBase, faceCount, pose.Position, index, index);
        }

        offsets[faceCount * count] = cornerCount * count;
        output.Parts = parts;
        output.InvalidateTopologyCaches();
        return count;
    }

    /// <summary>The scale the world gave body <paramref name="index"/>: its instance scale, made positive and non-zero like the collider's.</summary>
    private static Vector3 ScaleOf(PhysicsBody body, int index)
    {
        if (body.Instances == null || index >= body.Instances.Length)
            return Vector3.One;

        var scale = body.Instances[index].Scale;
        scale = new Vector3(Positive(scale.X), Positive(scale.Y), Positive(scale.Z));

        // Primitive colliders cannot be squashed: round shapes take the larger of X and Z, as the world does.
        if (body.SourceGeometry == null && body.Shape is PhysicsShapeType.Sphere or PhysicsShapeType.Capsule or PhysicsShapeType.Cylinder)
        {
            var radial = MathF.Max(scale.X, scale.Z);
            scale = body.Shape == PhysicsShapeType.Sphere ? new Vector3(radial) : new Vector3(radial, scale.Y, radial);
        }

        return scale;
    }

    private static float Positive(float value) => float.IsFinite(value) && MathF.Abs(value) > 0.0001f ? MathF.Abs(value) : 1f;

    /// <summary>Geometry of a primitive collider at its real size, centred on the body, with smooth normals on round surfaces.</summary>
    public static MeshGeometry BuildPrimitive(PhysicsBody body)
    {
        var radius = MathF.Max(0.001f, body.Radius);
        var half = MathF.Max(0.001f, body.Length) * 0.5f;
        const int arc = 6;
        var profile = new System.Collections.Generic.List<(float Radius, float Y, float NormalR, float NormalY)>();

        void Cap(float centerY, bool top)
        {
            for (var step = 0; step <= arc; step++)
            {
                var angle = MathF.PI * 0.5f * step / arc;
                var (sin, cos) = MathF.SinCos(top ? angle : MathF.PI * 0.5f - angle);
                profile.Add(top ? (radius * cos, centerY + radius * sin, cos, sin) : (radius * cos, centerY - radius * sin, cos, -sin));
            }
        }

        switch (body.Shape)
        {
            case PhysicsShapeType.Sphere:
                Cap(0f, false);
                Cap(0f, true);
                break;

            case PhysicsShapeType.Capsule:
                Cap(-half, false);
                Cap(half, true);
                break;

            case PhysicsShapeType.Cylinder:
                profile.Add((0f, -half, 0f, -1f));
                profile.Add((radius, -half, 0f, -1f));
                profile.Add((radius, -half, 1f, 0f));
                profile.Add((radius, half, 1f, 0f));
                profile.Add((radius, half, 0f, 1f));
                profile.Add((0f, half, 0f, 1f));
                break;

            default:
                return BuildBox(body.Size);
        }

        return Lathe(profile, 12);
    }

    private static MeshGeometry BuildBox(Vector3 size)
    {
        var h = new Vector3(MathF.Max(0.001f, size.X), MathF.Max(0.001f, size.Y), MathF.Max(0.001f, size.Z)) * 0.5f;
        var geometry = new MeshGeometry { Positions = new Vector3[8], CornerPointIndices = new int[24], FaceCornerOffsets = new int[7] };
        for (var index = 0; index < 8; index++)
            geometry.Positions[index] = new Vector3((index & 1) == 0 ? -h.X : h.X, (index & 2) == 0 ? -h.Y : h.Y, (index & 4) == 0 ? -h.Z : h.Z);

        // Counter-clockwise seen from outside.
        int[] quads = [0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5];
        Vector3[] faceNormals = [-Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitY, -Vector3.UnitX, Vector3.UnitX];
        var normals = geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, 24);
        for (var corner = 0; corner < 24; corner++)
        {
            geometry.CornerPointIndices[corner] = quads[corner];
            normals.Values[corner] = faceNormals[corner / 4];
        }

        for (var face = 0; face <= 6; face++)
            geometry.FaceCornerOffsets[face] = face * 4;

        return geometry;
    }

    /// <summary>Revolves a profile of (radius, height) around Y. Consecutive entries at the same place only change the normal.</summary>
    private static MeshGeometry Lathe(System.Collections.Generic.List<(float Radius, float Y, float NormalR, float NormalY)> profile, int segments)
    {
        var rings = profile.Count;
        var positions = new Vector3[rings * segments];
        for (var ring = 0; ring < rings; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var (sin, cos) = MathF.SinCos(MathF.Tau * segment / segments);
                positions[ring * segments + segment] = new Vector3(profile[ring].Radius * cos, profile[ring].Y, profile[ring].Radius * sin);
            }
        }

        var cornerList = new System.Collections.Generic.List<int>();
        var normalList = new System.Collections.Generic.List<Vector3>();
        var offsetList = new System.Collections.Generic.List<int> { 0 };
        for (var ring = 0; ring + 1 < rings; ring++)
        {
            var a = profile[ring];
            var b = profile[ring + 1];
            if (MathF.Abs(a.Radius - b.Radius) < 1e-6f && MathF.Abs(a.Y - b.Y) < 1e-6f)
                continue;

            for (var segment = 0; segment < segments; segment++)
            {
                var next = (segment + 1) % segments;
                Span<(int Ring, int Segment)> quad = [(ring, segment), (ring + 1, segment), (ring + 1, next), (ring, next)];
                foreach (var (quadRing, quadSegment) in quad)
                {
                    // A ring of zero radius is a pole: its points coincide, so the quad is a triangle there.
                    var (sin, cos) = MathF.SinCos(MathF.Tau * quadSegment / segments);
                    cornerList.Add(quadRing * segments + quadSegment);
                    normalList.Add(new Vector3(profile[quadRing].NormalR * cos, profile[quadRing].NormalY, profile[quadRing].NormalR * sin));
                }

                offsetList.Add(cornerList.Count);
            }
        }

        var geometry = new MeshGeometry { Positions = positions, CornerPointIndices = cornerList.ToArray(), FaceCornerOffsets = offsetList.ToArray() };
        var normals = geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, normalList.Count);
        normalList.CopyTo(normals.Values);
        return geometry;
    }
}
