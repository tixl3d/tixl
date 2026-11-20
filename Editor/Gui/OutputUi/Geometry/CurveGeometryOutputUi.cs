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
/// Output view for curves: renders them like [CurvesToPoints] into [DrawLines], lists the anchors, segments, contours
/// or parts with their attributes in a table, or shows counts, bounds and attributes. Rows selected in the table are
/// highlighted on the render. The text is rebuilt only when the curves change.
/// </summary>
internal sealed class CurveGeometryOutputUi : OutputUi<CurveGeometry>
{
    private enum Modes
    {
        Render,
        Table,
        Stats,
    }

    public CurveGeometryOutputUi()
    {
        _tooltipForMode = TooltipForMode;
        _isDomainDisabled = IsDomainDisabled;
        _tooltipForDomain = TooltipForDomain;
    }

    public override Type PresentedType => _isPresentingRender ? typeof(Command) : Type;

    public override IOutputUi Clone()
    {
        return new CurveGeometryOutputUi
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        _isPresentingRender = false;

        // The curves are pulled through the preview, so they needn't be evaluated on their own first.
        if (slot is Slot<CurveGeometry> curveSlot && _mode == Modes.Render && !PreviewLayout.IsCompactView())
        {
            _selectionOverlay.CaptureView(context);
            _isPresentingRender = _preview.Render(curveSlot, context);
            if (_isPresentingRender)
                return;
        }

        base.Recompute(slot, context);
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<CurveGeometry> typedSlot)
        {
            Debug.Assert(false);
            return;
        }

        var curves = typedSlot.Value;
        if (curves == null)
        {
            _lastCurves = null;
            _isPresentingRender = false;
            CustomComponents.EmptyWindowMessage("No curves");
            return;
        }

        if (!ReferenceEquals(curves, _lastCurves) || curves.Version != _lastVersion)
        {
            _lastCurves = curves;
            _lastVersion = curves.Version;
            RebuildLines(curves);
        }

        if (PreviewLayout.IsCompactView())
        {
            _isPresentingRender = false;
            DrawStats(showHeadline: true);
            return;
        }

        _tableView.SyncSelection(slot.Parent.SymbolChildId, _domain, CurveTableModel.GetRowCount(curves, _domain));
        if (_mode == Modes.Table || _tableView.SelectedCount > 0)
            _model.Update(curves, _domain);

        if (_isPresentingRender)
        {
            _preview.DrawRendered(viewId);
            _selectionOverlay.Draw(curves, _model, _tableView);
        }

        PreviewLayout.IndentControls();
        CustomComponents.SegmentedButton(ref _mode, null, _tooltipForMode);

        if (_mode == Modes.Table)
        {
            _hasParts = curves.Parts.Length > 0;
            ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
            if (CustomComponents.SegmentedButton(ref _domain, _isDomainDisabled, _tooltipForDomain))
            {
                _tableView.SyncSelection(slot.Parent.SymbolChildId, _domain, CurveTableModel.GetRowCount(curves, _domain));
                _model.Update(curves, _domain);
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

    private bool IsDomainDisabled(CurveTableModel.Domains domain) => domain == CurveTableModel.Domains.Parts && !_hasParts;

    private string? TooltipForDomain(CurveTableModel.Domains domain)
    {
        return domain switch
                   {
                       CurveTableModel.Domains.Anchors  => "One row per anchor. Handles are listed relative to their anchor.",
                       CurveTableModel.Domains.Segments => "One row per bezier segment between two anchors.",
                       CurveTableModel.Domains.Contours => "One row per contour, open or closed.",
                       _ when !_hasParts                => "These curves have no parts.",
                       _                                => "One row per part, e.g. a glyph.",
                   };
    }

    private void DrawStats(bool showHeadline)
    {
        ImGui.BeginChild("CurveStats");
        ImGui.Indent(10 * T3Ui.UiScaleFactor);
        FormInputs.AddVerticalSpace(5);
        _panel.Draw(showHeadline);
        ImGui.Unindent(10 * T3Ui.UiScaleFactor);
        ImGui.EndChild();
    }

    private void RebuildLines(CurveGeometry curves)
    {
        var contourCount = curves.ContourCount;
        var partCount = curves.Parts.Length;
        _panel.Clear(partCount > 1
                         ? $"{contourCount:N0} contours in {partCount:N0} parts"
                         : $"{contourCount:N0} contours");

        var closedCount = 0;
        for (var contourIndex = 0; contourIndex < contourCount; contourIndex++)
        {
            if (curves.ContourClosed[contourIndex])
                closedCount++;
        }

        _panel.AddLine("Anchors", $"{curves.PointCount:N0}", UiColors.Text);
        _panel.AddLine("Segments", $"{curves.GetSegmentCount():N0}", UiColors.Text);
        _panel.AddLine("Closed", $"{closedCount:N0} of {contourCount:N0} contours", UiColors.Text);

        var positions = curves.Positions;
        if (positions.Length > 0)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            _panel.AddLine("Size", GeometryStatsPanel.FormatSize(max - min), UiColors.Text);
            _panel.AddLine("Bounds", GeometryStatsPanel.FormatBounds(min, max), UiColors.Text);
        }

        _panel.SetAttributes(curves.Attributes);
    }

    private static string? TooltipForMode(Modes mode)
    {
        return mode switch
                   {
                       Modes.Render => "Render the curves like [CurvesToPoints] into [DrawLines] with default settings.\nRows selected in the table are highlighted.",
                       Modes.Table  => "List the elements of a domain with their attributes.\nClick rows to select them, Ctrl+click toggles, Shift+click extends.",
                       _            => "List counts, bounds and attributes. Bounds cover the anchors, not the handles.",
                   };
    }

    private static readonly Guid PreviewSymbolId = Guid.Parse("9a87212f-f305-4d66-bf0f-4c8983e10e76");
    private static readonly Guid PreviewInputId = Guid.Parse("61d89b08-ed0a-4c6e-a945-246de435651e");

    private readonly CommandPreview<CurveGeometry> _preview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewCurves");
    private static readonly string[] DomainLabels = ["anchors", "segments", "contours", "parts"];

    private readonly Func<Modes, string?> _tooltipForMode;
    private readonly Func<CurveTableModel.Domains, bool> _isDomainDisabled;
    private readonly Func<CurveTableModel.Domains, string?> _tooltipForDomain;

    /** Shared by all curve outputs, so switching ops keeps the chosen view. */
    private static Modes _mode = Modes.Render;
    private bool _isPresentingRender;

    /** Shared by all curve outputs, like the mode. */
    private static CurveTableModel.Domains _domain = CurveTableModel.Domains.Anchors;
    private bool _hasParts;

    private readonly CurveTableModel _model = new();
    private readonly CurveTableView _tableView = new();
    private readonly CurveSelectionOverlay _selectionOverlay = new();

    private string _summary = string.Empty;
    private string? _summaryHeadline;
    private int _summaryRowCount = -1;
    private int _summarySelectedCount = -1;
    private CurveTableModel.Domains _summaryDomain;

    private readonly GeometryStatsPanel _panel = new();
    private CurveGeometry? _lastCurves;
    private int _lastVersion;
}
