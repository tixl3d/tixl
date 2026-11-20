#nullable enable
using T3.Core.DataTypes;

namespace T3.Editor.Gui.OutputUi.Geometry;

/// <summary>
/// One domain of a CPU geometry as a grid of floats: built-in columns (positions, topology) followed by the domain's
/// attributes, one column per component. Values are refilled when the geometry changes and the previous fill is
/// kept, so the table can tint what changed. Column names are rebuilt only when the columns change, so animated
/// geometry doesn't allocate. Subclasses define the domains and fill the built-in columns.
/// </summary>
internal abstract class GeometryTableModel
{
    internal enum ColumnKinds
    {
        Float,
        Int,
        Bool,
    }

    public int RowCount { get; private set; }
    public int ColumnCount { get; private set; }
    public bool HasPrevious { get; private set; }

    public string GetColumnName(int column) => _columnNames[column];
    public ColumnKinds GetColumnKind(int column) => _columnKinds[column];
    public bool StartsColor(int column) => _columnStartsColor[column];
    public float GetValue(int row, int column) => _values[row * ColumnCount + column];
    public float GetPreviousValue(int row, int column) => _previousValues[row * ColumnCount + column];

    protected readonly record struct Column(string Name, ColumnKinds Kind);

    /// <param name="domainIndex">Identifies the subclass's domain, so switching domains resets the columns.</param>
    /// <param name="isSameSource">False when the rows belong to other data, so nothing is tinted as changed.</param>
    protected void Refill(GeometryAttributes attributes, AttributeDomain attributeDomain, int domainIndex,
                          Column[] builtInColumns, int rowCount, bool isSameSource)
    {
        var columnsChanged = UpdateColumns(attributes, attributeDomain, domainIndex, builtInColumns);
        HasPrevious = isSameSource && !columnsChanged && rowCount == RowCount;

        (_previousValues, _values) = (_values, _previousValues);
        RowCount = rowCount;
        var cellCount = rowCount * ColumnCount;
        if (_values.Length < cellCount)
            _values = new float[cellCount];

        FillBuiltInColumns();
        FillAttributeColumns(attributes, attributeDomain, builtInColumns.Length);
    }

    /** Called by <see cref="Refill"/> after the grid is sized; write with <see cref="Set(int,int,float)"/>. */
    protected abstract void FillBuiltInColumns();

    protected void Set(int row, int column, float value) => _values[row * ColumnCount + column] = value;

    protected void Set(int row, int column, Vector3 value)
    {
        Set(row, column, value.X);
        Set(row, column + 1, value.Y);
        Set(row, column + 2, value.Z);
    }

    #region columns
    private bool UpdateColumns(GeometryAttributes attributes, AttributeDomain attributeDomain, int domainIndex, Column[] builtIn)
    {
        if (domainIndex == _columnsDomainIndex && AttributesMatch(attributes, attributeDomain))
            return false;

        _columnsDomainIndex = domainIndex;
        _attributeCount = 0;
        var columnCount = builtIn.Length;
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

    private void FillAttributeColumns(GeometryAttributes attributes, AttributeDomain attributeDomain, int firstColumn)
    {
        var column = firstColumn;
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

    private static readonly string[] ComponentNames = ["X", "Y", "Z", "W"];

    private int _columnsDomainIndex = -1;
    private string[] _columnNames = [];
    private ColumnKinds[] _columnKinds = [];
    private bool[] _columnStartsColor = [];
    private string[] _attributeNames = [];
    private Type[] _attributeTypes = [];
    private int _attributeCount;

    private float[] _values = [];
    private float[] _previousValues = [];
}
