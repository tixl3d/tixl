#nullable enable
using T3.Core.DataTypes;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>Rows of the anchors, segments, contours or parts of a <see cref="CurveGeometry"/>.</summary>
internal sealed class CurveTableModel : GeometryTableModel
{
    internal enum Domains
    {
        Anchors,
        Segments,
        Contours,
        Parts,
    }

    public Domains Domain { get; private set; }

    /** For segment rows: the contour and the segment's index within it. */
    public int GetSegmentContour(int row) => _segmentContours[row];

    public int GetSegmentInContour(int row) => _segmentIndicesInContour[row];

    public static int GetRowCount(CurveGeometry curves, Domains domain)
    {
        return domain switch
                   {
                       Domains.Anchors  => curves.PointCount,
                       Domains.Segments => curves.GetSegmentCount(),
                       Domains.Contours => curves.ContourCount,
                       Domains.Parts    => curves.Parts.Length,
                       _                => 0,
                   };
    }

    /// <summary>Refills the grid if the curves, their version or the domain changed.</summary>
    public void Update(CurveGeometry curves, Domains domain)
    {
        if (ReferenceEquals(curves, _curves) && curves.Version == _lastVersion && domain == Domain)
            return;

        var isSameSource = domain == Domain && _curves != null;
        _curves = curves;
        _lastVersion = curves.Version;
        Domain = domain;

        var (attributeDomain, columns) = domain switch
                                             {
                                                 Domains.Anchors  => (AttributeDomain.ControlPoint, AnchorColumns),
                                                 Domains.Segments => (AttributeDomain.Segment, SegmentColumns),
                                                 Domains.Contours => (AttributeDomain.Contour, ContourColumns),
                                                 _                => (AttributeDomain.Part, PartColumns),
                                             };
        Refill(curves.Attributes, attributeDomain, (int)domain, columns, GetRowCount(curves, domain), isSameSource);
    }

    protected override void FillBuiltInColumns()
    {
        var curves = _curves!;
        switch (Domain)
        {
            case Domains.Anchors:
                for (var contour = 0; contour < curves.ContourCount; contour++)
                {
                    for (var anchor = curves.ContourOffsets[contour]; anchor < curves.ContourOffsets[contour + 1]; anchor++)
                    {
                        var position = curves.Positions[anchor];
                        Set(anchor, 0, contour);
                        Set(anchor, 1, position);
                        Set(anchor, 4, curves.HandlesIn[anchor] - position);
                        Set(anchor, 7, curves.HandlesOut[anchor] - position);
                    }
                }

                break;

            case Domains.Segments:
                if (_segmentContours.Length < RowCount)
                {
                    _segmentContours = new int[RowCount];
                    _segmentIndicesInContour = new int[RowCount];
                }

                var row = 0;
                for (var contour = 0; contour < curves.ContourCount; contour++)
                {
                    var start = curves.ContourOffsets[contour];
                    var anchorCount = curves.GetContourPointCount(contour);
                    var segmentCount = curves.GetContourSegmentCount(contour);
                    for (var segment = 0; segment < segmentCount; segment++)
                    {
                        var from = start + segment;
                        var to = start + (segment + 1) % anchorCount;
                        _segmentContours[row] = contour;
                        _segmentIndicesInContour[row] = segment;
                        Set(row, 0, contour);
                        Set(row, 1, from);
                        Set(row, 2, to);
                        Set(row, 3, Vector3.Distance(curves.Positions[from], curves.Positions[to]));
                        row++;
                    }
                }

                break;

            case Domains.Contours:
                for (var contour = 0; contour < curves.ContourCount; contour++)
                {
                    Set(contour, 0, curves.ContourOffsets[contour]);
                    Set(contour, 1, curves.GetContourPointCount(contour));
                    Set(contour, 2, curves.GetContourSegmentCount(contour));
                    Set(contour, 3, curves.ContourClosed[contour] ? 1 : 0);
                    Set(contour, 4, FindPart(curves.Parts, contour));
                }

                break;

            default:
                for (var part = 0; part < curves.Parts.Length; part++)
                {
                    var p = curves.Parts[part];
                    Set(part, 0, p.ContourStart);
                    Set(part, 1, p.ContourCount);
                    Set(part, 2, p.Pivot);
                    Set(part, 5, p.Id);
                    Set(part, 6, p.SeedIndex);
                }

                break;
        }
    }

    private static int FindPart(CurvePart[] parts, int contour)
    {
        for (var part = 0; part < parts.Length; part++)
        {
            if (contour >= parts[part].ContourStart && contour < parts[part].ContourStart + parts[part].ContourCount)
                return part;
        }

        return -1;
    }

    private static readonly Column[] AnchorColumns =
        [
            new("Contour", ColumnKinds.Int),
            new("Pos.X", ColumnKinds.Float), new("Pos.Y", ColumnKinds.Float), new("Pos.Z", ColumnKinds.Float),
            new("In.X", ColumnKinds.Float), new("In.Y", ColumnKinds.Float), new("In.Z", ColumnKinds.Float),
            new("Out.X", ColumnKinds.Float), new("Out.Y", ColumnKinds.Float), new("Out.Z", ColumnKinds.Float),
        ];

    private static readonly Column[] SegmentColumns =
        [
            new("Contour", ColumnKinds.Int), new("From", ColumnKinds.Int), new("To", ColumnKinds.Int),
            new("Chord", ColumnKinds.Float),
        ];

    private static readonly Column[] ContourColumns =
        [
            new("Start", ColumnKinds.Int), new("Anchors", ColumnKinds.Int), new("Segments", ColumnKinds.Int),
            new("Closed", ColumnKinds.Bool), new("Part", ColumnKinds.Int),
        ];

    private static readonly Column[] PartColumns =
        [
            new("Contour", ColumnKinds.Int), new("Contours", ColumnKinds.Int),
            new("Pivot.X", ColumnKinds.Float), new("Pivot.Y", ColumnKinds.Float), new("Pivot.Z", ColumnKinds.Float),
            new("Id", ColumnKinds.Int), new("Seed", ColumnKinds.Int),
        ];

    private CurveGeometry? _curves;
    private int _lastVersion;
    private int[] _segmentContours = [];
    private int[] _segmentIndicesInContour = [];
}
