#nullable enable
using System;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// A rotation per point of a <see cref="Geometry"/>, for operations in point space. An <c>Orientation</c> point
/// attribute is used as is. Otherwise curve points get X along the curve's tangent, Z along the curve's plane normal
/// and Y = Z x X across the curve; polygon points get Z along their <c>Normal</c> attribute or their polygon's normal.
/// Curves in a part share the part's normal, so a hole (wound the other way) faces like its outline.
/// Points without primitives keep the identity. The first primitive that uses a point defines its orientation.
/// Reuse one instance per op; it keeps its buffers.
/// </summary>
internal sealed class GeometryPointOrientations
{
    public Quaternion[] Rotations => _rotations;

    public void Compute(Geometry geometry)
    {
        var pointCount = geometry.PointCount;
        if (_rotations.Length != pointCount)
        {
            _rotations = new Quaternion[pointCount];
            _hasOrientation = new bool[pointCount];
        }

        if (geometry.Attributes.TryGet<Vector4>(GeometryAttributeNames.Orientation, AttributeDomain.Point, out var orientations))
        {
            var values = orientations.Values;
            for (var point = 0; point < pointCount; point++)
            {
                var v = values[point];
                _rotations[point] = Quaternion.Normalize(new Quaternion(v.X, v.Y, v.Z, v.W));
            }

            return;
        }

        Array.Fill(_rotations, Quaternion.Identity);
        Array.Clear(_hasOrientation);

        // Parts first: their curves take the part's facing. Curves outside any part keep their own winding.
        var curves = geometry.Curves;
        foreach (var part in geometry.Parts)
        {
            var partNormal = Vector3.Zero;
            var partEnd = Math.Min(part.PrimitiveStart + part.PrimitiveCount, geometry.PrimitiveKind.Length);
            for (var primitive = part.PrimitiveStart; primitive < partEnd; primitive++)
            {
                if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Curve)
                    partNormal += CurveNormal(curves[primitive]);
            }

            for (var primitive = part.PrimitiveStart; primitive < partEnd; primitive++)
            {
                if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Curve)
                    AddCurveOrientations(curves[primitive], partNormal);
            }
        }

        foreach (var curve in curves)
            AddCurveOrientations(curve, Vector3.Zero);

        geometry.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Point, out var pointNormals);
        foreach (var polygon in geometry.Polygons)
            AddPolygonOrientations(geometry, polygon, pointNormals?.Values);
    }

    /// <param name="facing">The curve's normal is flipped to agree with it; zero keeps the curve's own winding.</param>
    private void AddCurveOrientations(CurveView curve, Vector3 facing)
    {
        var vertexCount = curve.VertexCount;
        if (vertexCount < 2)
            return;

        var normal = CurveNormal(curve);
        if (Vector3.Dot(normal, facing) < 0)
            normal = -normal;

        normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;

        for (var index = 0; index < vertexCount; index++)
        {
            var point = curve.GetPointIndex(index);
            if (_hasOrientation[point])
                continue;

            var tangent = curve.GetHandleOutPosition(index) - curve.GetHandleInPosition(index);
            if (tangent.LengthSquared() < 1e-12f)
            {
                var previous = curve.IsClosed || index > 0 ? (index - 1 + vertexCount) % vertexCount : index;
                var next = curve.IsClosed || index < vertexCount - 1 ? (index + 1) % vertexCount : index;
                tangent = curve.GetPosition(next) - curve.GetPosition(previous);
            }

            _rotations[point] = OrientationFromAxes(tangent, normal);
            _hasOrientation[point] = true;
        }
    }

    private void AddPolygonOrientations(Geometry geometry, PolygonView polygon, Vector3[]? pointNormals)
    {
        var vertexCount = polygon.VertexCount;
        if (vertexCount < 3)
            return;

        var faceNormal = Vector3.Zero;
        for (var index = 0; index < vertexCount; index++)
            faceNormal += NewellTerm(polygon.GetPosition(index), polygon.GetPosition((index + 1) % vertexCount));

        for (var index = 0; index < vertexCount; index++)
        {
            var point = polygon.GetPointIndex(index);
            if (_hasOrientation[point])
                continue;

            var normal = pointNormals != null ? pointNormals[point] : faceNormal;
            var edge = polygon.GetPosition((index + 1) % vertexCount) - geometry.Positions[point];
            _rotations[point] = OrientationFromAxes(edge, normal);
            _hasOrientation[point] = true;
        }
    }

    /** Area-weighted plane normal of a curve's points; its sign follows the winding. */
    private static Vector3 CurveNormal(CurveView curve)
    {
        var vertexCount = curve.VertexCount;
        var normal = Vector3.Zero;
        for (var index = 0; index < vertexCount; index++)
            normal += NewellTerm(curve.GetPosition(index), curve.GetPosition((index + 1) % vertexCount));

        return normal;
    }

    /// <summary>A rotation whose X follows <paramref name="tangent"/> and Z follows <paramref name="normal"/>.</summary>
    private static Quaternion OrientationFromAxes(Vector3 tangent, Vector3 normal)
    {
        var z = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        var x = tangent - z * Vector3.Dot(tangent, z);
        if (x.LengthSquared() < 1e-12f)
            x = MathF.Abs(z.X) < 0.9f ? Vector3.Cross(Vector3.UnitX, z) : Vector3.Cross(Vector3.UnitY, z);

        x = Vector3.Normalize(x);
        var y = Vector3.Cross(z, x);
        var basis = new Matrix4x4(x.X, x.Y, x.Z, 0,
                                  y.X, y.Y, y.Z, 0,
                                  z.X, z.Y, z.Z, 0,
                                  0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
    }

    /** One edge's contribution to a polygon normal by Newell's method; robust for concave and slightly non-planar loops. */
    private static Vector3 NewellTerm(Vector3 a, Vector3 b)
    {
        return new Vector3((a.Y - b.Y) * (a.Z + b.Z),
                           (a.Z - b.Z) * (a.X + b.X),
                           (a.X - b.X) * (a.Y + b.Y));
    }

    private Quaternion[] _rotations = [];
    private bool[] _hasOrientation = [];
}
