#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Output view for CPU geometry: renders it like [GeometryToMesh] into [VisualizeMesh], lists its points, faces,
/// corners, edges or parts with their attributes in a table, or shows counts, bounds, volume, watertightness, the
/// attributes and the part table. Rows selected in the table are highlighted on the render. The text is rebuilt only
/// when the geometry changes.
/// </summary>
internal sealed class MeshGeometryOutputUi : OutputUi<MeshGeometry>
{
    private enum Modes
    {
        Render,
        Table,
        Stats,
    }

    public MeshGeometryOutputUi()
    {
        _tooltipForMode = TooltipForMode;
        _isDomainDisabled = IsDomainDisabled;
        _tooltipForDomain = TooltipForDomain;
    }

    public override Type PresentedType => _isPresentingRender ? typeof(Command) : Type;

    public override IOutputUi Clone()
    {
        return new MeshGeometryOutputUi
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        _isPresentingRender = false;

        // The geometry is pulled through the preview, so it needn't be evaluated on its own first.
        if (slot is Slot<MeshGeometry> geometrySlot && _mode == Modes.Render && !PreviewLayout.IsCompactView())
        {
            _projection.CaptureView(context);
            _isPresentingRender = _preview.Render(geometrySlot, context);
            if (_isPresentingRender)
                return;
        }

        EvaluateWithoutGizmos(slot, context);
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<MeshGeometry> typedSlot)
        {
            Debug.Assert(false);
            return;
        }

        var geometry = typedSlot.Value;
        if (geometry == null)
        {
            _lastGeometry = null;
            _isPresentingRender = false;
            CustomComponents.EmptyWindowMessage("No geometry");
            return;
        }

        if (_stats.UpdateIfChanged(geometry) || !ReferenceEquals(geometry, _lastGeometry))
        {
            _lastGeometry = geometry;
            RebuildLines(geometry);
        }

        if (PreviewLayout.IsCompactView())
        {
            _isPresentingRender = false;
            DrawStats(showHeadline: true);
            return;
        }

        _tableView.SyncSelection(slot.Parent.SymbolChildId, (int)_domain, MeshTableModel.GetRowCount(geometry, _domain));
        if (_mode == Modes.Table || _tableView.SelectedCount > 0)
            _model.Update(geometry, _domain);

        if (_isPresentingRender)
        {
            _preview.DrawRendered(viewId);
            _selectionOverlay.Draw(geometry, _model, _tableView, _projection);
        }

        PreviewLayout.IndentControls();
        CustomComponents.SegmentedButton(ref _mode, null, _tooltipForMode);

        if (_mode == Modes.Table)
        {
            _hasParts = geometry.Parts.Length > 0;
            ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
            if (CustomComponents.SegmentedButton(ref _domain, _isDomainDisabled, _tooltipForDomain))
            {
                _tableView.SyncSelection(slot.Parent.SymbolChildId, (int)_domain, MeshTableModel.GetRowCount(geometry, _domain));
                _model.Update(geometry, _domain);
            }
        }

        UpdateSummary();
        PreviewLayout.DrawSummary(_summary);

        if (_mode == Modes.Table)
        {
            PreviewLayout.IndentControls();
            _tableView.Draw(_model);
        }
        else if (_mode == Modes.Stats)
        {
            DrawStats(showHeadline: false);
        }
    }

    /** Rebuilt only when one of its numbers changes, so drawing stays allocation-free. */
    private void UpdateSummary()
    {
        var isTable = _mode == Modes.Table;
        var rowCount = isTable ? _model.RowCount : -1;
        var selectedCount = _tableView.SelectedCount;
        if (ReferenceEquals(_summaryHeadline, _panel.Headline)
            && rowCount == _summaryRowCount
            && selectedCount == _summarySelectedCount
            && _domain == _summaryDomain)
            return;

        _summaryHeadline = _panel.Headline;
        _summaryRowCount = rowCount;
        _summarySelectedCount = selectedCount;
        _summaryDomain = _domain;

        var domainLabel = DomainLabels[(int)_domain];
        var main = isTable ? $"{rowCount:N0} {domainLabel}" : _panel.Headline;
        _summary = selectedCount == 0
                       ? main
                       : isTable
                           ? $"{main}, {selectedCount:N0} selected"
                           : $"{main}, {selectedCount:N0} {domainLabel} selected";
    }

    private bool IsDomainDisabled(MeshTableModel.Domains domain) => domain == MeshTableModel.Domains.Parts && !_hasParts;

    private string? TooltipForDomain(MeshTableModel.Domains domain)
    {
        return domain switch
                   {
                       MeshTableModel.Domains.Points  => "One row per point. Faces share points through their corners.",
                       MeshTableModel.Domains.Faces   => "One row per face (N-gon), with its first corner and corner count.",
                       MeshTableModel.Domains.Corners => "One row per face corner: which face and which point.\nPer-face values like UVs often live here.",
                       MeshTableModel.Domains.Edges   => "One row per unique edge, with the faces on either side (-1 for an open edge).",
                       _ when !_hasParts              => "This geometry has no parts.",
                       _                              => "One row per part, e.g. a fracture cell.",
                   };
    }

    private void DrawStats(bool showHeadline)
    {
        ImGui.BeginChild("GeometryStats");
        ImGui.Indent(10 * T3Ui.UiScaleFactor);
        FormInputs.AddVerticalSpace(5);

        _panel.Draw(showHeadline);

        if (_partRowCount > 0)
        {
            FormInputs.AddVerticalSpace(8);
            CustomComponents.StylizedText("Parts", Fonts.FontSmall, UiColors.TextMuted);
            DrawPartTable();
        }

        ImGui.Unindent(10 * T3Ui.UiScaleFactor);
        ImGui.EndChild();
    }

    private void DrawPartTable()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        var height = Math.Min(_partRowCount + 1, MaxVisiblePartRows) * ImGui.GetTextLineHeightWithSpacing();
        if (!ImGui.BeginTable("Parts", 6, flags, new Vector2(0, height)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Faces");
        ImGui.TableSetupColumn("Open Edges");
        ImGui.TableSetupColumn("Volume");
        ImGui.TableSetupColumn("Seed");
        ImGui.TableSetupColumn("Pivot");
        ImGui.TableHeadersRow();

        unsafe
        {
            var clipperData = new ImGuiListClipper();
            var clipper = new ImGuiListClipperPtr(&clipperData);
            clipper.Begin(_partRowCount, ImGui.GetTextLineHeightWithSpacing());
            while (clipper.Step())
            {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    DrawPartRow(row);
                }
            }

            clipper.End();
        }

        ImGui.EndTable();
    }

    private void DrawPartRow(int row)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partIndexTexts[row]);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partFaceTexts[row]);
        ImGui.TableNextColumn();
        if (_partOpenEdgeTexts[row].Length == 0)
        {
            CustomComponents.StylizedText("-", Fonts.FontNormal, UiColors.TextMuted);
        }
        else
        {
            CustomComponents.StylizedText(_partOpenEdgeTexts[row], Fonts.FontNormal, UiColors.StatusAttention);
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partVolumeTexts[row]);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partSeedTexts[row]);
        ImGui.TableNextColumn();
        CustomComponents.StylizedText(_partPivotTexts[row], Fonts.FontSmall, UiColors.TextMuted);
    }

    private void RebuildLines(MeshGeometry geometry)
    {
        var s = _stats;
        _panel.Clear(s.PartCount > 1
                         ? $"{s.FaceCount:N0} faces in {s.PartCount:N0} parts"
                         : $"{s.FaceCount:N0} faces");

        _panel.AddLine("Points", $"{s.PointCount:N0}", UiColors.Text);
        _panel.AddLine("Triangles", $"{s.TriangleCount:N0}", UiColors.Text);
        _panel.AddLine("Size", GeometryStatsPanel.FormatSize(s.Size), UiColors.Text);
        _panel.AddLine("Bounds", GeometryStatsPanel.FormatBounds(s.BoundsMin, s.BoundsMax), UiColors.Text);
        _panel.AddLine("Volume", $"{s.Volume:0.####}", UiColors.Text);

        if (s.FaceCount == 0)
        {
            _panel.AddLine("Surface", "point cloud", UiColors.TextMuted);
        }
        else if (s.BoundaryEdges == 0 && s.NonManifoldEdges == 0)
        {
            _panel.AddLine("Surface", "watertight", UiColors.StatusControlled);
        }
        else
        {
            var info = s.NonManifoldEdges > 0
                           ? $"{s.BoundaryEdges:N0} boundary edges, {s.NonManifoldEdges:N0} non-manifold"
                           : $"{s.BoundaryEdges:N0} boundary edges (open)";
            _panel.AddLine("Surface", info, UiColors.StatusAttention);
        }

        _panel.SetAttributes(geometry.Attributes);
        RebuildPartRows();
    }

    /// <summary>Row strings are formatted once per geometry change so the table draw stays allocation-free.</summary>
    private void RebuildPartRows()
    {
        var parts = _stats.Parts;
        _partRowCount = parts.Length;
        if (_partIndexTexts.Length < _partRowCount)
        {
            _partIndexTexts = new string[_partRowCount];
            _partFaceTexts = new string[_partRowCount];
            _partOpenEdgeTexts = new string[_partRowCount];
            _partVolumeTexts = new string[_partRowCount];
            _partSeedTexts = new string[_partRowCount];
            _partPivotTexts = new string[_partRowCount];
        }

        for (var i = 0; i < _partRowCount; i++)
        {
            var part = parts[i];
            _partIndexTexts[i] = i.ToString();
            _partFaceTexts[i] = part.FaceCount.ToString("N0");
            _partOpenEdgeTexts[i] = part.BoundaryEdges == 0 ? string.Empty : part.BoundaryEdges.ToString("N0");
            _partVolumeTexts[i] = part.Volume.ToString("0.####");
            _partSeedTexts[i] = part.SeedIndex.ToString();
            _partPivotTexts[i] = $"{part.Pivot.X:0.###}, {part.Pivot.Y:0.###}, {part.Pivot.Z:0.###}";
        }
    }

    private static string? TooltipForMode(Modes mode)
    {
        return mode switch
                   {
                       Modes.Render => "Render the geometry like [GeometryToMesh] into [VisualizeMesh] with default settings.\nRows selected in the table are highlighted.",
                       Modes.Table  => "List the elements of a domain with their attributes.\nClick rows to select them, Ctrl+click toggles, Shift+click extends.",
                       _            => "List counts, bounds, attributes and parts.",
                   };
    }

    private const int MaxVisiblePartRows = 24;

    private static readonly Guid PreviewSymbolId = Guid.Parse("91e30c88-bd71-42df-993a-54a0187ccd26");
    private static readonly Guid PreviewInputId = Guid.Parse("b8a7277a-cfac-4e46-8619-7054c1cc6369");

    private readonly CommandPreview<MeshGeometry> _preview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewGeometry");
    private static readonly string[] DomainLabels = ["points", "faces", "corners", "edges", "parts"];

    private readonly Func<Modes, string?> _tooltipForMode;
    private readonly Func<MeshTableModel.Domains, bool> _isDomainDisabled;
    private readonly Func<MeshTableModel.Domains, string?> _tooltipForDomain;

    /** Shared by all geometry outputs, so switching ops keeps the chosen view. */
    private static Modes _mode = Modes.Render;
    private bool _isPresentingRender;

    /** Shared by all geometry outputs, like the mode. */
    private static MeshTableModel.Domains _domain = MeshTableModel.Domains.Points;
    private bool _hasParts;

    private readonly MeshTableModel _model = new();
    private readonly GeometryTableView _tableView = new();
    private readonly MeshSelectionOverlay _selectionOverlay = new();
    private readonly PreviewProjection _projection = new();

    private string _summary = string.Empty;
    private string? _summaryHeadline;
    private int _summaryRowCount = -1;
    private int _summarySelectedCount = -1;
    private MeshTableModel.Domains _summaryDomain;

    private int _partRowCount;
    private string[] _partIndexTexts = [];
    private string[] _partFaceTexts = [];
    private string[] _partOpenEdgeTexts = [];
    private string[] _partVolumeTexts = [];
    private string[] _partSeedTexts = [];
    private string[] _partPivotTexts = [];

    private readonly MeshGeometryStats _stats = new();
    private readonly GeometryStatsPanel _panel = new();
    private MeshGeometry? _lastGeometry;
}
