#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.Geometry;

/// <summary>
/// Output view for CPU geometry: renders it like [GeometryToMesh] into [VisualizeMesh], or shows counts, bounds,
/// volume, watertightness, the attributes and the part table. The text is rebuilt only when the geometry changes.
/// </summary>
internal sealed class MeshGeometryOutputUi : OutputUi<MeshGeometry>
{
    private enum Modes
    {
        Render,
        Stats,
    }

    public MeshGeometryOutputUi()
    {
        _tooltipForMode = TooltipForMode;
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
            _isPresentingRender = _preview.Render(geometrySlot, context);
            if (_isPresentingRender)
                return;
        }

        base.Recompute(slot, context);
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

        if (_isPresentingRender)
            _preview.DrawRendered(viewId);

        PreviewLayout.IndentControls();
        CustomComponents.SegmentedButton(ref _mode, null, _tooltipForMode);
        PreviewLayout.DrawSummary(_panel.Headline);

        if (_mode == Modes.Stats)
            DrawStats(showHeadline: false);
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
                       Modes.Render => "Render the geometry like [GeometryToMesh] into [VisualizeMesh] with default settings.",
                       _            => "List counts, bounds, attributes and parts.",
                   };
    }

    private const int MaxVisiblePartRows = 24;

    private static readonly Guid PreviewSymbolId = Guid.Parse("91e30c88-bd71-42df-993a-54a0187ccd26");
    private static readonly Guid PreviewInputId = Guid.Parse("b8a7277a-cfac-4e46-8619-7054c1cc6369");

    private readonly CommandPreview<MeshGeometry> _preview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewGeometry");
    private readonly Func<Modes, string?> _tooltipForMode;

    /** Shared by all geometry outputs, so switching ops keeps the chosen view. */
    private static Modes _mode = Modes.Render;
    private bool _isPresentingRender;

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
