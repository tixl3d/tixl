#nullable enable
using T3.Core.DataTypes;

namespace T3.Editor.Gui.OutputUi.Geometry;

/// <summary>
/// One domain of a <see cref="CurveGeometry"/> as a grid of floats: built-in columns (positions, handles, topology)
/// followed by the domain's attributes, one column per component. Values are refilled when the curves change and the
/// previous fill is kept, so the table can tint what changed. Column names are rebuilt only when the columns change,
/// so animated curves don't allocate.
/// </summary>
internal sealed class CurveTableModel
{
    internal enum Domains
    {
        Anchors,
        Segments,
        Contours,
        Parts,
    }

    internal enum ColumnKinds
    {
        Float,
        Int,
        Bool,
    }

    public Domains Domain { get; private set; }
    public int RowCount { get; private set; }
    public int ColumnCount { get; private set; }
    public bool HasPrevious { get; private set; }

    public string GetColumnName(int column) => _columnNames[column];
    public ColumnKinds GetColumnKind(int column) => _columnKinds[column];
    public bool StartsColor(int column) => _columnStartsColor[column];
    public float GetValue(int row, int column) => _values[row * ColumnCount + column];
    public float GetPreviousValue(int row, int column) => _previousValues[row * ColumnCount + column];

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
        if (ReferenceEquals(curves, _lastCurves) && curves.Version == _lastVersion && domain == Domain)
            return;

        var isSameDomain = domain == Domain && _lastCurves != null;
        _lastCurves = curves;
        _lastVersion = curves.Version;
        Domain = domain;

        var columnsChanged = UpdateColumns(curves, domain);
        var rowCount = GetRowCount(curves, domain);
        HasPrevious = isSameDomain && !columnsChanged && rowCount == RowCount;

        (_previousValues, _values) = (_values, _previousValues);
        RowCount = rowCount;
        var cellCount = rowCount * ColumnCount;
        if (_values.Length < cellCount)
            _values = new float[cellCount];

        var builtInCount = FillBuiltInColumns(curves, domain);
        FillAttributeColumns(curves, builtInCount);
    }

    #region columns
    private bool UpdateColumns(CurveGeometry curves, Domains domain)
    {
        var attributeDomain = ToAttributeDomain(domain);
        var builtIn = GetBuiltInColumns(domain);

        if (domain == _columnsDomain && AttributesMatch(curves.Attributes, attributeDomain))
            return false;

        _columnsDomain = domain;
        _attributeCount = 0;
        var columnCount = builtIn.Length;
        var attributes = curves.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain != attributeDomain)
                continue;

            if (_attributeCount == _attributeNames.Length)
            {
                Array.Resize(ref _attributeNames, _attributeCount * 2 + 4);
                Array.Resize(ref _attributeTypes, _attributeCount * 2 + 4);
            }

            _attributeNames[_attributeCount] = attribute.Name;
            _attributeTypes[_attributeCount] = attribute.ValueType;
            _attributeCount++;
            columnCount += ComponentCount(attribute.ValueType);
        }

        if (_columnNames.Length < columnCount)
        {
            _columnNames = new string[columnCount];
            _columnKinds = new ColumnKinds[columnCount];
            _columnStartsColor = new bool[columnCount];
        }

        for (var column = 0; column < builtIn.Length; column++)
        {
            _columnNames[column] = builtIn[column].Name;
            _columnKinds[column] = builtIn[column].Kind;
            _columnStartsColor[column] = false;
        }

        var nextColumn = builtIn.Length;
        for (var attributeIndex = 0; attributeIndex < _attributeCount; attributeIndex++)
        {
            var name = _attributeNames[attributeIndex];
            var type = _attributeTypes[attributeIndex];
            var components = ComponentCount(type);
            var kind = type == typeof(int) ? ColumnKinds.Int : ColumnKinds.Float;
            for (var component = 0; component < components; component++)
            {
                _columnNames[nextColumn] = components == 1 ? name : $"{name}.{ComponentNames[component]}";
                _columnKinds[nextColumn] = kind;
                _columnStartsColor[nextColumn] = component == 0
                                                 && type == typeof(Vector4)
                                                 && name.Contains("Color", StringComparison.OrdinalIgnoreCase);
                nextColumn++;
            }
        }

        ColumnCount = columnCount;
        return true;
    }

    private bool AttributesMatch(GeometryAttributes attributes, AttributeDomain attributeDomain)
    {
        var matched = 0;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain != attributeDomain)
                continue;

            if (matched == _attributeCount
                || attribute.ValueType != _attributeTypes[matched]
                || !string.Equals(attribute.Name, _attributeNames[matched], StringComparison.Ordinal))
                return false;

            matched++;
        }

        return matched == _attributeCount;
    }

    private static Column[] GetBuiltInColumns(Domains domain)
    {
        return domain switch
                   {
                       Domains.Anchors  => AnchorColumns,
                       Domains.Segments => SegmentColumns,
                       Domains.Contours => ContourColumns,
                       _                => PartColumns,
                   };
    }

    private static AttributeDomain ToAttributeDomain(Domains domain)
    {
        return domain switch
                   {
                       Domains.Anchors  => AttributeDomain.ControlPoint,
                       Domains.Segments => AttributeDomain.Segment,
                       Domains.Contours => AttributeDomain.Contour,
                       _                => AttributeDomain.Part,
                   };
    }

    private static int ComponentCount(Type type)
    {
        if (type == typeof(Vector2))
            return 2;

        if (type == typeof(Vector3))
            return 3;

        if (type == typeof(Vector4))
            return 4;

        return 1;
    }
    #endregion

    #region values
    /// <returns>The number of built-in columns filled.</returns>
    private int FillBuiltInColumns(CurveGeometry curves, Domains domain)
    {
        switch (domain)
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

                return AnchorColumns.Length;

            case Domains.Segments:
                EnsureSegmentMapping(RowCount);
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

                return SegmentColumns.Length;

            case Domains.Contours:
                for (var contour = 0; contour < curves.ContourCount; contour++)
                {
                    Set(contour, 0, curves.ContourOffsets[contour]);
                    Set(contour, 1, curves.GetContourPointCount(contour));
                    Set(contour, 2, curves.GetContourSegmentCount(contour));
                    Set(contour, 3, curves.ContourClosed[contour] ? 1 : 0);
                    Set(contour, 4, FindPart(curves.Parts, contour));
                }

                return ContourColumns.Length;

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

                return PartColumns.Length;
        }
    }

    private void FillAttributeColumns(CurveGeometry curves, int firstColumn)
    {
        var attributeDomain = ToAttributeDomain(Domain);
        var column = firstColumn;
        var attributes = curves.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain != attributeDomain)
                continue;

            // A count that doesn't match the domain is a producer bug; missing rows show as NaN, highlighted.
            switch (attribute)
            {
                case GeometryAttribute<float> floats:
                    for (var row = 0; row < RowCount; row++)
                        Set(row, column, row < floats.Values.Length ? floats.Values[row] : float.NaN);
                    break;
                case GeometryAttribute<int> ints:
                    for (var row = 0; row < RowCount; row++)
                        Set(row, column, row < ints.Values.Length ? ints.Values[row] : float.NaN);
                    break;
                case GeometryAttribute<Vector2> vectors2:
                    for (var row = 0; row < RowCount; row++)
                    {
                        var v = row < vectors2.Values.Length ? vectors2.Values[row] : new Vector2(float.NaN);
                        Set(row, column, v.X);
                        Set(row, column + 1, v.Y);
                    }

                    break;
                case GeometryAttribute<Vector3> vectors3:
                    for (var row = 0; row < RowCount; row++)
                        Set(row, column, row < vectors3.Values.Length ? vectors3.Values[row] : new Vector3(float.NaN));
                    break;
                case GeometryAttribute<Vector4> vectors4:
                    for (var row = 0; row < RowCount; row++)
                    {
                        var v = row < vectors4.Values.Length ? vectors4.Values[row] : new Vector4(float.NaN);
                        Set(row, column, v.X);
                        Set(row, column + 1, v.Y);
                        Set(row, column + 2, v.Z);
                        Set(row, column + 3, v.W);
                    }

                    break;
                default:
                    for (var row = 0; row < RowCount; row++)
                        Set(row, column, float.NaN);
                    break;
            }

            column += ComponentCount(attribute.ValueType);
        }
    }

    private void Set(int row, int column, float value) => _values[row * ColumnCount + column] = value;

    private void Set(int row, int column, Vector3 value)
    {
        Set(row, column, value.X);
        Set(row, column + 1, value.Y);
        Set(row, column + 2, value.Z);
    }

    private void EnsureSegmentMapping(int rowCount)
    {
        if (_segmentContours.Length >= rowCount)
            return;

        _segmentContours = new int[rowCount];
        _segmentIndicesInContour = new int[rowCount];
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
    #endregion

    private readonly record struct Column(string Name, ColumnKinds Kind);

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

    private static readonly string[] ComponentNames = ["X", "Y", "Z", "W"];

    private CurveGeometry? _lastCurves;
    private int _lastVersion;

    private Domains _columnsDomain = (Domains)(-1);
    private string[] _columnNames = [];
    private ColumnKinds[] _columnKinds = [];
    private bool[] _columnStartsColor = [];
    private string[] _attributeNames = [];
    private Type[] _attributeTypes = [];
    private int _attributeCount;

    private float[] _values = [];
    private float[] _previousValues = [];
    private int[] _segmentContours = [];
    private int[] _segmentIndicesInContour = [];
}
