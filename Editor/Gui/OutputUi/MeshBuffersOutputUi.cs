#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Rendering;
using T3.Editor.Gui.OutputUi.Buffers;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Shows <see cref="MeshBuffers"/>: renders them like [VisualizeMesh] and, in a sidebar (see <see cref="PreviewSidebar"/>),
/// lists a readback of the vertices (<see cref="PbrVertex"/>) or the triangles' vertex indices, or the counts. Hovering a
/// vertex row marks it on the render. Views too small for the controls, like connection tooltips, only show the summary.
/// </summary>
internal sealed class MeshBuffersOutputUi : OutputUi<MeshBuffers>
{
    public MeshBuffersOutputUi()
    {
        _sidebar = new PreviewSidebar(_sidebarState);
        _vertexTable.ElementType = typeof(PbrVertex);
        _triangleTable.ElementType = typeof(Int3);
        _triangleTable.ShowsIntegers = true;
    }

    public override Type PresentedType => _isPresentingMesh ? typeof(Command) : Type;

    public override IOutputUi Clone()
    {
        return new MeshBuffersOutputUi
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        _isPresentingMesh = false;
        if (slot is not Slot<MeshBuffers> meshSlot || PreviewLayout.IsCompactView())
        {
            EvaluateWithoutGizmos(slot, context);
            return;
        }

        // The mesh is pulled through the visualizer, so it needn't be evaluated on its own first.
        if (_sidebar.ShowsPreview)
        {
            _sidebar.BeginView(context);
            var previousOverlay = context.PreviewOverlay;
            var overlay = previousOverlay;
            var hasHovered = _vertexTable.TryGetHoveredPosition(out var hovered) && _sidebar.Selected == VerticesSegment;
            overlay.Hovered = _hoveredPoint.Get(hasHovered, hovered);
            context.PreviewOverlay = overlay;
            _isPresentingMesh = _preview.Render(meshSlot, context);
            context.PreviewOverlay = previousOverlay;
            _sidebar.EndView(context);
        }

        if (!_isPresentingMesh)
            EvaluateWithoutGizmos(slot, context);

        var mesh = meshSlot.Value;
        if (mesh == null)
            return;

        if (_sidebar.Selected == VerticesSegment && IsDefined(mesh.VertexBuffer))
            _vertexTable.UpdateReadback(mesh.VertexBuffer);
        else if (_sidebar.Selected == TrianglesSegment && IsDefined(mesh.IndicesBuffer))
            _triangleTable.UpdateReadback(mesh.IndicesBuffer);
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<MeshBuffers> meshSlot)
        {
            Debug.Assert(false);
            return;
        }

        var mesh = meshSlot.Value;
        var isDefined = mesh != null && IsDefined(mesh.VertexBuffer) && IsDefined(mesh.IndicesBuffer);
        if (isDefined)
            UpdateSummary(mesh!);

        if (PreviewLayout.IsCompactView() || !isDefined)
        {
            _isPresentingMesh = false;
            CustomComponents.StylizedText(nameof(MeshBuffers), Fonts.FontSmall, UiColors.TextMuted);
            ImGui.TextUnformatted(isDefined ? _summary : "Undefined");
            return;
        }

        if (_isPresentingMesh)
        {
            DrawRendered(viewId);
        }

        if (!_sidebar.Begin(Segments, _counts))
        {
            _vertexTable.ClearHover();
            PreviewLayout.DrawSummary(_summary);
            return;
        }

        switch (_sidebar.Selected)
        {
            case VerticesSegment:
                _vertexTable.DrawRangeControls(mesh!.VertexBuffer.ElementCount);
                _vertexTable.DrawTable();
                break;
            case TrianglesSegment:
                _vertexTable.ClearHover();
                _triangleTable.DrawRangeControls(mesh!.IndicesBuffer.ElementCount);
                _triangleTable.DrawTable();
                break;
            default:
                _vertexTable.ClearHover();
                ImGui.TextUnformatted(_summary);
                break;
        }

        _sidebar.End();
    }

    /** Into the rectangle right of the sidebar while it is open, otherwise onto the output canvas as usual. */
    private void DrawRendered(string viewId)
    {
        if (_sidebar.TryGetViewRect(out var viewRect))
        {
            _preview.DrawRenderedInRect(viewRect);
            return;
        }
        _preview.DrawRendered(viewId);
    }

    private static bool IsDefined(BufferWithViews? buffer)
    {
        return buffer?.Buffer is { IsDisposed: false } && buffer.ElementStride > 0;
    }

    /** Rebuilt only when the counts change, so drawing stays allocation-free. */
    private void UpdateSummary(MeshBuffers mesh)
    {
        var vertexCount = mesh.VertexBuffer.ElementCount;
        var triangleCount = mesh.IndicesBuffer.ElementCount;
        if (vertexCount == _summaryVertexCount && triangleCount == _summaryTriangleCount)
            return;

        _summaryVertexCount = vertexCount;
        _summaryTriangleCount = triangleCount;
        _summary = $"{vertexCount:N0} vertices, {triangleCount:N0} triangles";
        _counts[VerticesSegment] = PreviewSidebar.FormatCount(vertexCount);
        _counts[TrianglesSegment] = PreviewSidebar.FormatCount(triangleCount);
    }

    private const int VerticesSegment = 1;
    private const int TrianglesSegment = 2;

    private static readonly PreviewSidebar.Segment[] Segments =
        [
            new(Icon.Hidden, "Hide the sidebar; the mesh uses the whole view."),
            new(Icon.DomainVertices, "Vertices: read the vertex buffer back from the GPU, one row per vertex with position, normal, tangents, UVs and color."),
            new(Icon.DomainPrimitives, "Triangles: the three vertex indices of each triangle."),
            new(Icon.Info, "Vertex and triangle counts."),
        ];

    private static readonly Guid PreviewSymbolId = Guid.Parse("ca23d69c-9dbf-421a-9c75-205c3eab3101");
    private static readonly Guid PreviewInputId = Guid.Parse("f13f5753-d17d-4234-a605-a99fab1a57b5");

    /** Shared by all mesh outputs, so switching ops keeps the layout. */
    private static readonly PreviewSidebar.State _sidebarState = new();

    private readonly CommandPreview<MeshBuffers> _preview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewMesh");
    private readonly PreviewSidebar _sidebar;
    private readonly PreviewPointGeometry _hoveredPoint = new();
    private readonly BufferTableView _vertexTable = new();
    private readonly BufferTableView _triangleTable = new();
    private readonly string?[] _counts = new string?[Segments.Length];
    private bool _isPresentingMesh;

    private string _summary = string.Empty;
    private int _summaryVertexCount = -1;
    private int _summaryTriangleCount = -1;
}
