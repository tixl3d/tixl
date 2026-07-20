#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Interactive corner-pin editor for a selected output. Draws the output's pixel canvas and every
/// surface mapped to it as a draggable quad; surfaces without a mapping can be added with a centered
/// default quad. Corner drags go through an undo command and persist to the setup file.
///
/// The view composes a <see cref="ScalableCanvas"/> (pan/zoom, fit, transitions — matching the
/// image viewer) and hands the editing widgets its transform through the backend-neutral
/// <see cref="ICanvasProjection"/> seam, so a camera-backed projection can replace it later without
/// touching the widgets. One instance per output window.
/// </summary>
internal sealed class SetupOutputView
{
    public SetupOutputView()
    {
        _canvas.FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion;
        _projection = new ScalableCanvasProjection(_canvas);
    }

    public void Draw(Guid outputId)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var output = setup.Outputs.Find(o => o.Id == outputId);
        if (output == null)
            return;

        DrawHeader(setup, output, outputId);

        _canvas.UpdateCanvas(out _);

        var canvasSize = new Vector2(Math.Max(1, output.CanvasResolution.Width),
                                     Math.Max(1, output.CanvasResolution.Height));

        // Fit the output frame into view when the shown output changes (instant so the first frame
        // is already correct — no jump-then-settle).
        if (_fittedOutputId != outputId)
        {
            _canvas.FitAreaOnCanvas(ImRect.RectWithSize(Vector2.Zero, canvasSize));
            _canvas.SetScopeInstant(_canvas.GetTargetScope());
            _fittedOutputId = outputId;
        }

        var dl = ImGui.GetWindowDrawList();
        var frameMin = _projection.CanvasToScreen(Vector2.Zero);
        var frameMax = _projection.CanvasToScreen(canvasSize);
        dl.AddRectFilled(frameMin, frameMax, UiColors.BackgroundFull.Fade(0.4f));

        // Live composite behind the handles: what the output manager would send to this output.
        var composite = OutputManager.RenderOutput(outputId);
        var hasContent = false;
        if (composite is { IsDisposed: false })
        {
            var srv = SrvManager.GetSrvForTexture(composite);
            if (srv is { IsDisposed: false })
            {
                dl.AddImage(srv.NativePointer, frameMin, frameMax);
                hasContent = true;
            }
        }

        dl.AddRect(frameMin, frameMax, UiColors.ForegroundFull.Fade(0.25f));

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var mappingData = surface.OutputMappings.Find(m => m.OutputId == outputId);
            if (mappingData == null)
                continue;

            ImGui.PushID(surface.Id.GetHashCode());
            // The checker is only a stand-in; drop it once the real composite fills the surface.
            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable: true);
            style.DrawChecker = !hasContent;
            var phase = CornerPinHandles.Draw(mappingData.Quad, _projection, style, out _);
            HandleDrag(phase, surface.Id, outputId, mappingData);
            ImGui.PopID();
        }
    }

    private void DrawHeader(Setup setup, OutputDefinition output, Guid outputId)
    {
        CustomComponents.StylizedText($"{output.Name} · {output.CanvasResolution.Width}×{output.CanvasResolution.Height}",
                                      Fonts.FontSmall, UiColors.TextMuted);

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.OutputMappings.Exists(m => m.OutputId == outputId))
                continue;

            ImGui.SameLine();
            ImGui.PushID(surface.Id.GetHashCode());
            var label = string.IsNullOrEmpty(surface.Name) ? "untitled" : surface.Name;
            if (ImGui.SmallButton("+ " + label))
            {
                AddMapping(surface, output, outputId);
                OutputSetupHandling.SaveActive();
            }

            CustomComponents.TooltipForLastItem("Map this surface onto the output",
                                                "Drops a centered corner-pin quad you can then drag into place.");
            ImGui.PopID();
        }
    }

    private static void AddMapping(Surface surface, OutputDefinition output, Guid outputId)
    {
        var canvasW = Math.Max(1, output.CanvasResolution.Width);
        var canvasH = Math.Max(1, output.CanvasResolution.Height);

        var aspect = surface.SizeInMeters.Y > 0.0001f ? surface.SizeInMeters.X / surface.SizeInMeters.Y : 1f;
        var maxW = canvasW * 0.6f;
        var maxH = canvasH * 0.6f;
        var w = maxW;
        var h = w / aspect;
        if (h > maxH)
        {
            h = maxH;
            w = h * aspect;
        }

        var cx = canvasW * 0.5f;
        var cy = canvasH * 0.5f;
        var quad = new[]
                       {
                           new Vector2(cx - w * 0.5f, cy - h * 0.5f), // top-left
                           new Vector2(cx + w * 0.5f, cy - h * 0.5f), // top-right
                           new Vector2(cx + w * 0.5f, cy + h * 0.5f), // bottom-right
                           new Vector2(cx - w * 0.5f, cy + h * 0.5f), // bottom-left
                       };

        surface.OutputMappings.Add(new Surface.OutputMapping { OutputId = outputId, Quad = quad });
    }

    private void HandleDrag(CanvasPointHandle.DragPhase phase, Guid surfaceId, Guid outputId, Surface.OutputMapping mappingData)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                // Quad still holds its pre-drag value on the activation frame.
                _dragOldQuad = (Vector2[])mappingData.Quad.Clone();
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_dragOldQuad != null)
                {
                    var command = new ChangeOutputMappingQuadCommand(surfaceId, outputId, _dragOldQuad, mappingData.Quad);
                    UndoRedoStack.Add(command); // value already applied live during the drag
                    OutputSetupHandling.SaveActive();
                    _dragOldQuad = null;
                }

                break;
        }
    }

    private readonly ScalableCanvas _canvas = new();
    private readonly ScalableCanvasProjection _projection;
    private Guid _fittedOutputId;

    // Pre-drag quad snapshot; only one corner drags at a time, so a single slot suffices.
    private Vector2[]? _dragOldQuad;
}
