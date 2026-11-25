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
/// Shows a GPU buffer: renders it as points and, in a sidebar (see <see cref="PreviewSidebar"/>), lists a readback of
/// its values or its count, stride and size. Buffers don't carry their element type, so only a 64-byte stride is
/// rendered as points; other buffers show the sidebar over the whole view. Hovering a row of a point buffer marks the
/// point on the render. Views too small for the controls, like connection tooltips, only show the summary.
/// </summary>
internal sealed class BufferWithViewsOutputUi : OutputUi<BufferWithViews>
{
    public BufferWithViewsOutputUi()
    {
        _sidebar = new PreviewSidebar(_sidebarState);
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
            EvaluateWithoutGizmos(slot, context);
            return;
        }

        // The buffer is pulled through the visualizer, so it needn't be evaluated on its own first.
        if (CanShowPoints(bufferSlot.Value) && _sidebar.ShowsPreview)
        {
            _sidebar.BeginView(context);
            var previousOverlay = context.PreviewOverlay;
            var overlay = previousOverlay;
            var hasHovered = _tableView.TryGetHoveredPosition(out var hovered) && _sidebar.Selected == TableSegment;
            overlay.Hovered = _hoveredPoint.Get(hasHovered, hovered);
            context.PreviewOverlay = overlay;
            _isPresentingPoints = _pointsPreview.Render(bufferSlot, context);
            context.PreviewOverlay = previousOverlay;
            _sidebar.EndView(context);
        }

        if (!_isPresentingPoints)
            EvaluateWithoutGizmos(slot, context);

        if (bufferSlot.Value != null && _sidebar.Selected == TableSegment)
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

        var canShowPoints = CanShowPoints(buffer);
        if (_isPresentingPoints)
        {
            DrawRendered(viewId);
        }

        if (!_sidebar.Begin(Segments, _counts, hasNoPreview: !canShowPoints))
        {
            _tableView.ClearHover();
            PreviewLayout.DrawSummary(_summary);
            return;
        }

        if (_sidebar.Selected == TableSegment)
        {
            _tableView.DrawRangeControls(buffer!.ElementCount);
            _tableView.DrawTable();
        }
        else
        {
            _tableView.ClearHover();
            ImGui.TextUnformatted(_summary);
        }

        _sidebar.End();
    }

    /** Into the rectangle right of the sidebar while it is open, otherwise onto the output canvas as usual. */
    private void DrawRendered(string viewId)
    {
        if (_sidebar.TryGetViewRect(out var viewRect))
        {
            _pointsPreview.DrawRenderedInRect(viewRect);
            return;
        }
        _pointsPreview.DrawRendered(viewId);
    }

    private void DrawSummaryOnly(bool isDefined)
    {
        CustomComponents.StylizedText(nameof(BufferWithViews), Fonts.FontSmall, UiColors.TextMuted);
        ImGui.TextUnformatted(isDefined ? _summary : "Undefined");
    }

    private static bool CanShowPoints(BufferWithViews? buffer)
    {
        return buffer is { Srv: not null } && buffer.ElementStride == BufferTableView.PointStride;
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
        _counts[TableSegment] = PreviewSidebar.FormatCount(count);
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

    private const int TableSegment = 1;

    private static readonly PreviewSidebar.Segment[] Segments =
        [
            new(Icon.Hidden, "Hide the sidebar; the points use the whole view."),
            new(Icon.DomainPoints, "Read the buffer back from the GPU and list its values.\nOrange values grew since the last frame, blue ones shrank."),
            new(Icon.Info, "Element count, stride and size."),
        ];

    private static readonly Guid PreviewSymbolId = Guid.Parse("6bf2c69e-0ab5-40b2-8ea1-b469333f4247");
    private static readonly Guid PreviewInputId = Guid.Parse("89d49547-bbf2-4acf-80b8-aba4175acade");

    /** Shared by all buffer outputs, so switching ops keeps the layout. */
    private static readonly PreviewSidebar.State _sidebarState = new();

    private readonly CommandPreview<BufferWithViews> _pointsPreview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewPoints");
    private readonly BufferTableView _tableView = new();
    private readonly PreviewSidebar _sidebar;
    private readonly PreviewPointGeometry _hoveredPoint = new();
    private readonly string?[] _counts = new string?[Segments.Length];
    private bool _isPresentingPoints;
    private bool _isCompact;

    private string _summary = string.Empty;
    private int _summaryCount = -1;
    private int _summaryStride = -1;
}
