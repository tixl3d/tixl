#nullable enable
using System.Globalization;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.TableView;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Draws a <see cref="GeometryTableModel"/> as a table, styled like the buffer table, and keeps a row selection.
/// The selection is editor state for inspecting the shown op; it is never written into the geometry.
/// </summary>
internal sealed class GeometryTableView
{
    public int SelectedCount { get; private set; }

    /// <summary>Changes whenever the selection does, so consumers can rebuild only then.</summary>
    public int SelectionVersion { get; private set; }

    /// <summary>The row under the mouse in the last <see cref="Draw"/>, or -1.</summary>
    public int HoveredRow { get; private set; } = -1;

    public void ClearHover() => HoveredRow = -1;

    public bool IsSelected(int row) => row < _isSelected.Length && _isSelected[row];

    /// <summary>Keeps the selection while the same op shows the same domain; clears it otherwise.</summary>
    public void SyncSelection(Guid shownChildId, int domainIndex, int rowCount)
    {
        if (shownChildId != _selectionChildId || domainIndex != _selectionDomainIndex)
        {
            _selectionChildId = shownChildId;
            _selectionDomainIndex = domainIndex;
            ClearSelection();
        }

        if (_isSelected.Length < rowCount)
            Array.Resize(ref _isSelected, Math.Max(rowCount, _isSelected.Length * 2));

        // Rows past the end disappeared upstream; drop them so the count stays honest.
        for (var row = rowCount; row < _selectionRowLimit; row++)
        {
            if (!_isSelected[row])
                continue;

            _isSelected[row] = false;
            SelectedCount--;
            SelectionVersion++;
        }

        _selectionRowLimit = rowCount;
    }

    public void ClearSelection()
    {
        Array.Clear(_isSelected);
        SelectedCount = 0;
        _rangeStartRow = -1;
        SelectionVersion++;
    }

    public void Draw(GeometryTableModel model)
    {
        HoveredRow = -1;
        if (model.RowCount == 0)
        {
            CustomComponents.StylizedText("No elements in this domain.", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY
                                      | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV;
        if (!ImGui.BeginTable("##geometryValues", model.ColumnCount + 1, flags, ImGui.GetContentRegionAvail()))
            return;

        ImGui.PushFont(Fonts.FontSmall);
        var scale = T3Ui.UiScaleFactor;
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, IndexColumnWidth * scale);
        for (var column = 0; column < model.ColumnCount; column++)
        {
            var width = model.GetColumnKind(column) == GeometryTableModel.ColumnKinds.Float ? FloatColumnWidth : IntColumnWidth;
            ImGui.TableSetupColumn(model.GetColumnName(column), ImGuiTableColumnFlags.WidthFixed, width * scale);
        }

        ImGui.TableHeadersRow();

        unsafe
        {
            var clipperData = new ImGuiListClipper();
            var clipper = new ImGuiListClipperPtr(&clipperData);
            clipper.Begin(model.RowCount);
            while (clipper.Step())
            {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    DrawRow(model, row);
                }
            }

            clipper.End();
        }

        ImGui.PopFont();
        ImGui.EndTable();
    }

    private void DrawRow(GeometryTableModel model, int row)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        ImGui.PushID(row);
        if (ImGui.Selectable("##row", IsSelected(row), ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
            HandleRowClick(row);

        if (ImGui.IsItemHovered())
            HoveredRow = row;

        ImGui.PopID();
        ImGui.SameLine(0, 0);
        row.TryFormat(_formatBuffer, out var indexLength, default, CultureInfo.InvariantCulture);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(_formatBuffer.AsSpan(0, indexLength));
        ImGui.PopStyleColor();

        for (var column = 0; column < model.ColumnCount; column++)
        {
            ImGui.TableNextColumn();
            var value = model.GetValue(row, column);
            var previous = model.HasPrevious ? model.GetPreviousValue(row, column) : value;
            var kind = model.GetColumnKind(column);

            if (model.StartsColor(column) && column + 3 < model.ColumnCount)
            {
                var color = new Vector4(value,
                                        model.GetValue(row, column + 1),
                                        model.GetValue(row, column + 2),
                                        model.GetValue(row, column + 3));
                TableValueStyle.DrawColorSwatch(ImGui.GetCursorScreenPos(), ImGui.GetTextLineHeight(), color);
            }

            ReadOnlySpan<char> text;
            if (kind == GeometryTableModel.ColumnKinds.PrimitiveKind)
            {
                text = (int)value == (int)PrimitiveKinds.Curve ? "curve" : "polygon";
            }
            else if (kind == GeometryTableModel.ColumnKinds.Bool)
            {
                text = value > 0.5f ? "yes" : "no";
            }
            else if (kind == GeometryTableModel.ColumnKinds.Int && float.IsFinite(value))
            {
                ((int)value).TryFormat(_formatBuffer, out var length, default, CultureInfo.InvariantCulture);
                text = _formatBuffer.AsSpan(0, length);
            }
            else
            {
                value.TryFormat(_formatBuffer, out var length, "0.000", CultureInfo.InvariantCulture);
                text = _formatBuffer.AsSpan(0, length);
            }

            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - textWidth));
            ImGui.PushStyleColor(ImGuiCol.Text, TableValueStyle.GetColor(value, previous).Rgba);
            ImGui.TextUnformatted(text);
            ImGui.PopStyleColor();
            if (kind is GeometryTableModel.ColumnKinds.Float or GeometryTableModel.ColumnKinds.Int)
                TableValueStyle.DrawTooltipIfHovered(model.GetColumnName(column), value, previous);
        }
    }

    /** Click selects one row, Ctrl+click toggles, Shift+click extends from the last clicked row. */
    private void HandleRowClick(int row)
    {
        var io = ImGui.GetIO();
        if (io.KeyShift && _rangeStartRow >= 0)
        {
            var first = Math.Min(_rangeStartRow, row);
            var last = Math.Max(_rangeStartRow, row);
            for (var rangeRow = first; rangeRow <= last; rangeRow++)
                Select(rangeRow, true);

            return;
        }

        if (io.KeyCtrl)
        {
            Select(row, !IsSelected(row));
        }
        else
        {
            var wasOnlySelection = SelectedCount == 1 && IsSelected(row);
            ClearSelection();
            if (!wasOnlySelection)
                Select(row, true);
        }

        _rangeStartRow = row;
    }

    private void Select(int row, bool isSelected)
    {
        if (row >= _isSelected.Length || _isSelected[row] == isSelected)
            return;

        _isSelected[row] = isSelected;
        SelectedCount += isSelected ? 1 : -1;
        SelectionVersion++;
    }

    private const float IndexColumnWidth = 45;
    private const float FloatColumnWidth = 70;
    private const float IntColumnWidth = 55;

    private readonly char[] _formatBuffer = new char[32];
    private bool[] _isSelected = [];
    private int _selectionRowLimit;
    private int _rangeStartRow = -1;
    private Guid _selectionChildId;
    private int _selectionDomainIndex = -1;
}
