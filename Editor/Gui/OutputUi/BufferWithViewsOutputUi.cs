#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.OutputUi.Buffers;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Shows a GPU buffer's count, stride and size, and either renders it as points or lists a readback of its values.
/// Buffers don't carry their element type, so only a 64-byte stride is offered as points. Views too small for the
/// controls, like connection tooltips, only show the summary.
/// </summary>
internal sealed class BufferWithViewsOutputUi : OutputUi<BufferWithViews>
{
    private enum Modes
    {
        Points,
        Table,
    }

    public BufferWithViewsOutputUi()
    {
        _isModeDisabled = IsModeDisabled;
        _tooltipForMode = TooltipForMode;
    }

    public override Type PresentedType => _isPresentingPoints ? typeof(Command) : Type;

    public override IOutputUi Clone()
    {
        return new BufferWithViewsOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        _isPresentingPoints = false;
        _isCompact = PreviewLayout.IsCompactView();
        if (slot is not Slot<BufferWithViews> bufferSlot || _isCompact)
        {
            base.Recompute(slot, context);
            return;
        }

        // The buffer is pulled through the visualizer, so it needn't be evaluated on its own first.
        if (GetShownMode(bufferSlot.Value) == Modes.Points)
        {
            _isPresentingPoints = _pointsPreview.Render(bufferSlot, context);
            if (_isPresentingPoints)
                return;
        }

        base.Recompute(slot, context);
        if (bufferSlot.Value != null)
            _tableView.UpdateReadback(bufferSlot.Value);
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<BufferWithViews> bufferSlot)
        {
            Debug.Assert(false);
            return;
        }

        _isCompact = PreviewLayout.IsCompactView();
        var buffer = bufferSlot.Value;
        var isDefined = buffer?.Buffer is { IsDisposed: false } && buffer.ElementStride > 0;
        if (isDefined)
            UpdateSummary(buffer!);

        if (_isCompact || !isDefined)
        {
            _isPresentingPoints = false;
            DrawSummaryOnly(isDefined);
            return;
        }

        if (_isPresentingPoints)
            _pointsPreview.DrawRendered(viewId);

        _canShowPoints = CanShowPoints(buffer);
        var shownMode = GetShownMode(buffer);
        PreviewLayout.IndentControls();
        if (CustomComponents.SegmentedButton(ref shownMode, _isModeDisabled, _tooltipForMode))
            _mode = shownMode;

        if (shownMode == Modes.Table)
        {
            ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
            _tableView.DrawRangeControls(buffer!.ElementCount);
        }

        PreviewLayout.DrawSummary(_summary);

        if (shownMode == Modes.Table)
        {
            PreviewLayout.IndentControls();
            _tableView.DrawTable();
        }
    }

    private void DrawSummaryOnly(bool isDefined)
    {
        CustomComponents.StylizedText(nameof(BufferWithViews), Fonts.FontSmall, UiColors.TextMuted);
        ImGui.TextUnformatted(isDefined ? _summary : "Undefined");
    }

    private Modes GetShownMode(BufferWithViews? buffer)
    {
        return CanShowPoints(buffer) ? _mode : Modes.Table;
    }

    private static bool CanShowPoints(BufferWithViews? buffer)
    {
        return buffer is { Srv: not null } && buffer.ElementStride == BufferTableView.PointStride;
    }

    private bool IsModeDisabled(Modes mode) => mode == Modes.Points && !_canShowPoints;

    private string? TooltipForMode(Modes mode)
    {
        return mode switch
                   {
                       Modes.Points when !_canShowPoints => "Only buffers with a 64-byte stride can be shown as points.",
                       Modes.Points                      => "Render the buffer like a [VisualizePoints] with default settings.",
                       _                                 => "Read the buffer back from the GPU and list its values.\nOrange values grew since the last frame, blue ones shrank.",
                   };
    }

    /** Rebuilt only when the buffer layout changes, so drawing stays allocation-free. */
    private void UpdateSummary(BufferWithViews buffer)
    {
        var count = buffer.ElementCount;
        var stride = buffer.ElementStride;
        if (count == _summaryCount && stride == _summaryStride)
            return;

        _summaryCount = count;
        _summaryStride = stride;
        _summary = $"{count:N0} × {stride} bytes = {FormatByteSize((long)count * stride)}";
    }

    private static string FormatByteSize(long bytes)
    {
        return bytes switch
                   {
                       >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
                       >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
                       >= 1L << 10 => $"{bytes / (double)(1L << 10):0.##} KB",
                       _           => $"{bytes} bytes",
                   };
    }

    private static readonly Guid VisualizePointsSymbolId = Guid.Parse("37bdbafc-d14c-4b81-91c3-8f63c3b63812");
    private static readonly Guid VisualizePointsInputId = Guid.Parse("54fc4cd7-dfc3-4690-9fd1-2b555f7656d4");

    private readonly CommandPreview<BufferWithViews> _pointsPreview = new(VisualizePointsSymbolId, VisualizePointsInputId, "VisualizePoints");
    private readonly BufferTableView _tableView = new();
    private readonly Func<Modes, bool> _isModeDisabled;
    private readonly Func<Modes, string?> _tooltipForMode;

    /** Shared by all buffer outputs, so switching ops keeps the chosen view. */
    private static Modes _mode = Modes.Points;
    private bool _canShowPoints;
    private bool _isPresentingPoints;
    private bool _isCompact;

    private string _summary = string.Empty;
    private int _summaryCount = -1;
    private int _summaryStride = -1;
}
