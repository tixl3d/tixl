#nullable enable
using System.Globalization;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Rendering;
using T3.Core.Resource;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.TableView;
using T3.Graphics.Compat;

namespace T3.Editor.Gui.OutputUi.Buffers;

/// <summary>
/// Reads a range of a GPU buffer back to the CPU and lists it as floats, one row per element, styled by
/// <see cref="TableValueStyle"/>. A 64-byte stride is labelled as <see cref="Point"/>. Unlike <see cref="TableList"/>
/// it is read-only and needs no element type, so it lists buffers of any stride.
/// </summary>
internal sealed class BufferTableView
{
    public BufferTableView()
    {
        _onReadComplete = OnReadComplete;
    }

    /// <summary>
    /// The struct the buffer holds, for column names and color swatches when its size matches the stride (e.g.
    /// <see cref="PbrVertex"/>). Without it, a 64-byte stride is shown as <see cref="Point"/> and others as [0], [1], ...
    /// </summary>
    public Type? ElementType { get; set; }

    /// <summary>Shows the components as integers, e.g. the vertex indices of a mesh's triangles.</summary>
    public bool ShowsIntegers { get; set; }

    /// <summary>The row under the mouse in the last <see cref="DrawTable"/>, or -1.</summary>
    public int HoveredRow { get; private set; } = -1;

    public void ClearHover() => HoveredRow = -1;

    /// <summary>The position of the hovered row's point; only for point buffers.</summary>
    public bool TryGetHoveredPosition(out Vector3 position)
    {
        position = default;
        if (HoveredRow < 0 || HoveredRow >= _rowCount || ShowsIntegers || GetShownType(_stride) is not { } type
            || (type != typeof(Point) && type != typeof(PbrVertex)))
            return false;

        position = new Vector3(ReadFloat(_values, _stride, HoveredRow, 0),
                               ReadFloat(_values, _stride, HoveredRow, 1),
                               ReadFloat(_values, _stride, HoveredRow, 2));
        return true;
    }

    /// <summary>Completes last frame's readback and requests the next. Call once per frame after the buffer was evaluated.</summary>
    public void UpdateReadback(BufferWithViews buffer)
    {
        _readAccess.Update();

        var stride = buffer.ElementStride;
        var elementCount = buffer.ElementCount;
        if (buffer.Buffer == null || stride < sizeof(float) || elementCount == 0)
            return;

        _firstElement = Math.Clamp(_firstElement, 0, elementCount - 1);
        var count = Math.Min(_requestedCount, elementCount - _firstElement);
        _readAccess.InitiateRead(buffer.Buffer, count, stride, _onReadComplete, _firstElement);
    }

    public void DrawRangeControls(int elementCount)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText("Start", Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SameLine(0, 4 * scale);
        ImGui.SetNextItemWidth(70 * scale);
        ImGui.DragInt("##start", ref _firstElement, 1, 0, Math.Max(0, elementCount - 1));

        ImGui.SameLine(0, 8 * scale);
        CustomComponents.StylizedText("Count", Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SameLine(0, 4 * scale);
        ImGui.SetNextItemWidth(60 * scale);
        ImGui.DragInt("##count", ref _requestedCount, 1, 1, MaxCount);
        _requestedCount = Math.Clamp(_requestedCount, 1, MaxCount);
    }

    public void DrawTable()
    {
        HoveredRow = -1;
        if (_rowCount == 0)
            return;

        var componentCount = _stride / sizeof(float);
        var columnNames = GetColumnNames(_stride);
        var shownType = GetShownType(_stride);
        var colorColumnStarts = shownType != null && !ShowsIntegers ? TableValueStyle.GetColorColumnStarts(shownType) : null;
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY
                                      | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV;
        if (!ImGui.BeginTable("##bufferValues", componentCount + 1, flags, ImGui.GetContentRegionAvail()))
            return;

        ImGui.PushFont(Fonts.FontSmall);
        var scale = T3Ui.UiScaleFactor;
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, IndexColumnWidth * scale);
        for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            ImGui.TableSetupColumn(columnNames[componentIndex], ImGuiTableColumnFlags.WidthFixed, ValueColumnWidth * scale);
        }

        ImGui.TableHeadersRow();

        unsafe
        {
            var clipperData = new ImGuiListClipper();
            var clipper = new ImGuiListClipperPtr(&clipperData);
            clipper.Begin(_rowCount);
            while (clipper.Step())
            {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    DrawRow(row, componentCount, columnNames, colorColumnStarts);
                }
            }

            clipper.End();
        }

        ImGui.PopFont();
        ImGui.EndTable();
    }

    private void DrawRow(int row, int componentCount, string[] columnNames, bool[]? colorColumnStarts)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        // An empty full-width selectable only to detect hovering the row
        ImGui.PushID(row);
        ImGui.Selectable("##row", false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap);
        if (ImGui.IsItemHovered())
            HoveredRow = row;

        ImGui.PopID();
        ImGui.SameLine(0, 0);
        (_firstElementOfRows + row).TryFormat(_formatBuffer, out var indexLength, default, CultureInfo.InvariantCulture);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(_formatBuffer.AsSpan(0, indexLength));
        ImGui.PopStyleColor();

        var hasPreviousRow = _previousStride == _stride
                             && _previousFirstElement == _firstElementOfRows
                             && row < _previousRowCount;

        for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            ImGui.TableNextColumn();
            if (colorColumnStarts != null && colorColumnStarts[componentIndex] && componentIndex + 3 < componentCount)
            {
                var color = new Vector4(ReadFloat(_values, _stride, row, componentIndex),
                                        ReadFloat(_values, _stride, row, componentIndex + 1),
                                        ReadFloat(_values, _stride, row, componentIndex + 2),
                                        ReadFloat(_values, _stride, row, componentIndex + 3));
                TableValueStyle.DrawColorSwatch(ImGui.GetCursorScreenPos(), ImGui.GetTextLineHeight(), color);
            }

            var value = ShowsIntegers ? ReadInt(_values, _stride, row, componentIndex) : ReadFloat(_values, _stride, row, componentIndex);
            var previous = !hasPreviousRow ? value
                           : ShowsIntegers ? ReadInt(_previousValues, _stride, row, componentIndex)
                           : ReadFloat(_previousValues, _stride, row, componentIndex);

            var length = 0;
            if (ShowsIntegers)
                ((int)value).TryFormat(_formatBuffer, out length, default, CultureInfo.InvariantCulture);
            else
                value.TryFormat(_formatBuffer, out length, "0.000", CultureInfo.InvariantCulture);
            var text = _formatBuffer.AsSpan(0, length);
            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - textWidth));
            ImGui.PushStyleColor(ImGuiCol.Text, TableValueStyle.GetColor(value, previous).Rgba);
            ImGui.TextUnformatted(text);
            ImGui.PopStyleColor();
            TableValueStyle.DrawTooltipIfHovered(columnNames[componentIndex], value, previous);
        }
    }

    private static float ReadInt(byte[] values, int stride, int row, int componentIndex)
    {
        return MemoryMarshal.Read<int>(values.AsSpan(row * stride + componentIndex * sizeof(int)));
    }

    /** The struct whose fields name the columns, if its size matches the stride. */
    private Type? GetShownType(int stride)
    {
        if (ElementType != null)
            return Marshal.SizeOf(ElementType) == stride ? ElementType : null;

        return stride == PointStride ? typeof(Point) : null;
    }

    private static float ReadFloat(byte[] values, int stride, int row, int componentIndex)
    {
        return MemoryMarshal.Read<float>(values.AsSpan(row * stride + componentIndex * sizeof(float)));
    }

    private void OnReadComplete(StructuredBufferReadAccess.ReadRequestItem item, IntPtr dataPointer, DataStream dataStream)
    {
        (_previousValues, _values) = (_values, _previousValues);
        _previousRowCount = _rowCount;
        _previousStride = _stride;
        _previousFirstElement = _firstElementOfRows;

        var sizeInBytes = item.ElementCount * item.StructureByteStride;
        if (_values.Length < sizeInBytes)
            _values = new byte[sizeInBytes];

        Marshal.Copy(dataPointer, _values, 0, sizeInBytes);
        _rowCount = item.ElementCount;
        _stride = item.StructureByteStride;
        _firstElementOfRows = item.FirstElement;
    }

    private string[] GetColumnNames(int stride)
    {
        if (GetShownType(stride) is { } type)
            return TableValueStyle.GetFloatColumnLabels(type);

        var componentCount = stride / sizeof(float);
        if (_genericColumnNames.Length != componentCount)
        {
            _genericColumnNames = new string[componentCount];
            for (var index = 0; index < componentCount; index++)
            {
                _genericColumnNames[index] = $"[{index}]";
            }
        }

        return _genericColumnNames;
    }

    public const int PointStride = 64;
    private const int MaxCount = 100_000;
    private const float IndexColumnWidth = 45;
    private const float ValueColumnWidth = 70;

    private static string[] _genericColumnNames = [];

    private readonly StructuredBufferReadAccess _readAccess = new();
    private readonly StructuredBufferReadAccess.OnReadComplete _onReadComplete;
    private readonly char[] _formatBuffer = new char[32];

    private int _firstElement;
    private int _requestedCount = 256;

    private byte[] _values = [];
    private int _rowCount;
    private int _stride;
    private int _firstElementOfRows;

    private byte[] _previousValues = [];
    private int _previousRowCount;
    private int _previousStride;
    private int _previousFirstElement;
}
