#nullable enable
using T3.Core.DataTypes;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Rows of the points, faces, corners, edges or parts of a <see cref="MeshGeometry"/>. Edges are the geometry's
/// derived edge topology, which is built on first access after a topology change.
/// </summary>
internal sealed class MeshTableModel : GeometryTableModel
{
    internal enum Domains
    {
        Points,
        Faces,
        Corners,
        Edges,
        Parts,
    }

    public Domains Domain { get; private set; }

    /** For corner rows: the face the corner belongs to. */
    public int GetCornerFace(int row) => _cornerFaces[row];

    public static int GetRowCount(MeshGeometry geometry, Domains domain)
    {
        return domain switch
                   {
                       Domains.Points  => geometry.PointCount,
                       Domains.Faces   => geometry.FaceCount,
                       Domains.Corners => geometry.CornerCount,
                       Domains.Edges   => geometry.Edges.Edges.Length,
                       Domains.Parts   => geometry.Parts.Length,
                       _               => 0,
                   };
    }

    /// <summary>Refills the grid if the geometry, its version or the domain changed.</summary>
    public void Update(MeshGeometry geometry, Domains domain)
    {
        if (ReferenceEquals(geometry, _geometry) && geometry.Version == _lastVersion && domain == Domain)
            return;

        var isSameSource = domain == Domain && _geometry != null;
        _geometry = geometry;
        _lastVersion = geometry.Version;
        Domain = domain;

        var (attributeDomain, columns) = domain switch
                                             {
                                                 Domains.Points  => (AttributeDomain.Point, PointColumns),
                                                 Domains.Faces   => (AttributeDomain.Face, FaceColumns),
                                                 Domains.Corners => (AttributeDomain.Corner, CornerColumns),
                                                 Domains.Edges   => (AttributeDomain.Edge, EdgeColumns),
                                                 _               => (AttributeDomain.Part, PartColumns),
                                             };
        Refill(geometry.Attributes, attributeDomain, (int)domain, columns, GetRowCount(geometry, domain), isSameSource);
    }

    protected override void FillBuiltInColumns()
    {
        var geometry = _geometry!;
        switch (Domain)
        {
            case Domains.Points:
                for (var point = 0; point < geometry.PointCount; point++)
                    Set(point, 0, geometry.Positions[point]);

                break;

            case Domains.Faces:
                for (var face = 0; face < geometry.FaceCount; face++)
                {
                    var start = geometry.FaceCornerOffsets[face];
                    var cornerCount = geometry.GetFaceCornerCount(face);
                    Set(face, 0, start);
                    Set(face, 1, cornerCount);
                    Set(face, 2, FindPart(geometry.Parts, face));
                    Set(face, 3, GetFaceCenter(geometry, face));
                }

                break;

            case Domains.Corners:
                if (_cornerFaces.Length < RowCount)
                    _cornerFaces = new int[RowCount];

                for (var face = 0; face < geometry.FaceCount; face++)
                {
                    for (var corner = geometry.FaceCornerOffsets[face]; corner < geometry.FaceCornerOffsets[face + 1]; corner++)
                    {
                        _cornerFaces[corner] = face;
                        Set(corner, 0, face);
                        Set(corner, 1, geometry.CornerPointIndices[corner]);
                    }
                }

                break;

            case Domains.Edges:
                var edges = geometry.Edges.Edges;
                for (var edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    var edge = edges[edgeIndex];
                    Set(edgeIndex, 0, edge.PointA);
                    Set(edgeIndex, 1, edge.PointB);
                    Set(edgeIndex, 2, edge.Face0);
                    Set(edgeIndex, 3, edge.Face1);
                }

                break;

            default:
                for (var part = 0; part < geometry.Parts.Length; part++)
                {
                    var p = geometry.Parts[part];
                    Set(part, 0, p.FaceStart);
                    Set(part, 1, p.FaceCount);
                    Set(part, 2, p.Pivot);
                    Set(part, 5, p.Id);
                    Set(part, 6, p.SeedIndex);
                }

                break;
        }
    }

    public static Vector3 GetFaceCenter(MeshGeometry geometry, int face)
    {
        var start = geometry.FaceCornerOffsets[face];
        var end = geometry.FaceCornerOffsets[face + 1];
        if (end == start)
            return Vector3.Zero;

        var sum = Vector3.Zero;
        for (var corner = start; corner < end; corner++)
            sum += geometry.Positions[geometry.CornerPointIndices[corner]];

        return sum / (end - start);
    }

    private static int FindPart(GeometryPart[] parts, int face)
    {
        for (var part = 0; part < parts.Length; part++)
        {
            if (face >= parts[part].FaceStart && face < parts[part].FaceStart + parts[part].FaceCount)
                return part;
        }

        return -1;
    }

    private static readonly Column[] PointColumns =
        [
            new("Pos.X", ColumnKinds.Float), new("Pos.Y", ColumnKinds.Float), new("Pos.Z", ColumnKinds.Float),
        ];

    private static readonly Column[] FaceColumns =
        [
            new("Corner", ColumnKinds.Int), new("Corners", ColumnKinds.Int), new("Part", ColumnKinds.Int),
            new("Center.X", ColumnKinds.Float), new("Center.Y", ColumnKinds.Float), new("Center.Z", ColumnKinds.Float),
        ];

    private static readonly Column[] CornerColumns =
        [
            new("Face", ColumnKinds.Int), new("Point", ColumnKinds.Int),
        ];

    private static readonly Column[] EdgeColumns =
        [
            new("PointA", ColumnKinds.Int), new("PointB", ColumnKinds.Int),
            new("Face0", ColumnKinds.Int), new("Face1", ColumnKinds.Int),
        ];

    private static readonly Column[] PartColumns =
        [
            new("Face", ColumnKinds.Int), new("Faces", ColumnKinds.Int),
            new("Pivot.X", ColumnKinds.Float), new("Pivot.Y", ColumnKinds.Float), new("Pivot.Z", ColumnKinds.Float),
            new("Id", ColumnKinds.Int), new("Seed", ColumnKinds.Int),
        ];

    private MeshGeometry? _geometry;
    private int _lastVersion;
    private int[] _cornerFaces = [];
}
