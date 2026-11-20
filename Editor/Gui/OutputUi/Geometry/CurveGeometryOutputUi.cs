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
/// Output view for curves: renders them like [CurvesToPoints] into [DrawLines], or shows counts, bounds and
/// attributes. The text is rebuilt only when the curves change.
/// </summary>
internal sealed class CurveGeometryOutputUi : OutputUi<CurveGeometry>
{
    private enum Modes
    {
        Render,
        Stats,
    }

    public CurveGeometryOutputUi()
    {
        _tooltipForMode = TooltipForMode;
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
                       Modes.Render => "Render the curves like [CurvesToPoints] into [DrawLines] with default settings.",
                       _            => "List counts, bounds and attributes. Bounds cover the anchors, not the handles.",
                   };
    }

    private static readonly Guid PreviewSymbolId = Guid.Parse("9a87212f-f305-4d66-bf0f-4c8983e10e76");
    private static readonly Guid PreviewInputId = Guid.Parse("61d89b08-ed0a-4c6e-a945-246de435651e");

    private readonly CommandPreview<CurveGeometry> _preview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewCurves");
    private readonly Func<Modes, string?> _tooltipForMode;

    /** Shared by all curve outputs, so switching ops keeps the chosen view. */
    private static Modes _mode = Modes.Render;
    private bool _isPresentingRender;

    private readonly GeometryStatsPanel _panel = new();
    private CurveGeometry? _lastCurves;
    private int _lastVersion;
}
